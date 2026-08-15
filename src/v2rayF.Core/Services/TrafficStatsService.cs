using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace v2rayF.Services;

/// <summary>Queries Xray StatsService for outbound proxy uplink/downlink totals.</summary>
public sealed class TrafficStatsService
{
    public const int QueryTimeoutMs = 1500;

    public readonly record struct TrafficSnapshot(long UplinkBytes, long DownlinkBytes);

    private readonly ICoreEnvironment _environment;

    public TrafficStatsService(ICoreEnvironment environment)
    {
        _environment = environment;
    }

    public async Task<TrafficSnapshot?> QueryAsync(CancellationToken cancellationToken = default)
    {
        var corePath = _environment.GetCorePath();
        if (!File.Exists(corePath))
            return null;

        try
        {
            var json = await RunStatsQueryAsync(corePath, _environment.GetCoresDirectory(), cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            return ParseStatsQueryJson(json);
        }
        catch
        {
            return null;
        }
    }

    public static TrafficSnapshot? ParseStatsQueryJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("stat", out var stats) || stats.ValueKind != JsonValueKind.Array)
            return new TrafficSnapshot(0, 0);

        long uplink = 0;
        long downlink = 0;

        foreach (var item in stats.EnumerateArray())
        {
            if (!item.TryGetProperty("name", out var nameEl))
                continue;

            var name = nameEl.GetString() ?? "";
            if (!IsProxyTrafficStat(name))
                continue;

            var value = ReadStatValue(item);
            if (name.EndsWith(">>>uplink", StringComparison.Ordinal))
                uplink += value;
            else if (name.EndsWith(">>>downlink", StringComparison.Ordinal))
                downlink += value;
        }

        return new TrafficSnapshot(uplink, downlink);
    }

    public static bool IsProxyTrafficStat(string name)
    {
        // outbound>>>proxy>>>traffic>>>uplink|downlink
        // outbound>>>proxy-0>>>traffic>>>uplink|downlink
        if (!name.StartsWith("outbound>>>", StringComparison.Ordinal))
            return false;
        if (!name.Contains(">>>traffic>>>", StringComparison.Ordinal))
            return false;

        var parts = name.Split(">>>", StringSplitOptions.None);
        if (parts.Length < 4)
            return false;

        var tag = parts[1];
        return tag == "proxy" || tag.StartsWith("proxy-", StringComparison.Ordinal);
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0)
            bytes = 0;

        double value = bytes;
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes} {units[unit]}"
            : string.Create(CultureInfo.InvariantCulture, $"{value:0.##} {units[unit]}");
    }

    public static string FormatUpload(long bytes) => $"↑ {FormatBytes(bytes)}";

    public static string FormatDownload(long bytes) => $"↓ {FormatBytes(bytes)}";

    public static string FormatRate(long bytesPerSecond) => $"{FormatBytes(Math.Max(0, bytesPerSecond))}/s";

    public static string FormatUploadRate(long bps) => $"↑ {FormatRate(bps)}";

    public static string FormatDownloadRate(long bps) => $"↓ {FormatRate(bps)}";

    public static string FormatNotificationLine(long uplink, long downlink) =>
        $"{FormatUpload(uplink)}  ·  {FormatDownload(downlink)}";

    public static string FormatNotificationLine(long uplinkBps, long downlinkBps, int? pingMs) =>
        pingMs is > 0
            ? $"{FormatUploadRate(uplinkBps)}  ·  {FormatDownloadRate(downlinkBps)}  ·  {pingMs}"
            : $"{FormatUploadRate(uplinkBps)}  ·  {FormatDownloadRate(downlinkBps)}";

    private static long ReadStatValue(JsonElement item)
    {
        if (!item.TryGetProperty("value", out var valueEl))
            return 0;

        return valueEl.ValueKind switch
        {
            JsonValueKind.Number when valueEl.TryGetInt64(out var n) => n,
            JsonValueKind.String when long.TryParse(valueEl.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) => s,
            _ => 0
        };
    }

    private static async Task<string?> RunStatsQueryAsync(
        string corePath,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = corePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory
        };
        startInfo.ArgumentList.Add("api");
        startInfo.ArgumentList.Add("statsquery");
        startInfo.ArgumentList.Add($"--server=127.0.0.1:{XrayConfigBuilder.ApiPort}");

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            return null;

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(QueryTimeoutMs);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (OperatingSystem.IsAndroid())
                    process.Kill();
                else
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort.
            }

            return null;
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        _ = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            return null;

        // Some builds may prepend logs; take the JSON object.
        var trim = stdout.Trim();
        var start = trim.IndexOf('{');
        var end = trim.LastIndexOf('}');
        if (start < 0 || end < start)
            return null;

        return trim[start..(end + 1)];
    }
}
