using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using v2rayF.Models;

namespace v2rayF.Services;

public sealed class LatencyService
{
    private static readonly string[] PingUrls =
    [
        "https://www.gstatic.com/generate_204",
        "https://cp.cloudflare.com/generate_204"
    ];

    private readonly ICoreEnvironment _environment;
    private readonly SemaphoreSlim _speedtestLock = new(1, 1);

    public const int TimeoutMs = 10000;

    public LatencyService(ICoreEnvironment environment)
    {
        _environment = environment;
    }

    public async Task<int?> MeasureAsync(ProxyServer server, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(server.Address) || server.Port <= 0)
            return null;

        await _environment.EnsureCoreAsync(cancellationToken).ConfigureAwait(false);
        if (File.Exists(_environment.GetCorePath()))
        {
            var proxyResult = await MeasureViaCoreAsync(server, cancellationToken).ConfigureAwait(false);
            if (proxyResult.HasValue && proxyResult.Value >= 0)
                return proxyResult;
        }

        return await MeasureTcpAsync(server, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int?> MeasureViaSocksAsync(int socksPort, CancellationToken cancellationToken = default)
    {
        return await ProbeThroughSocksAsync(socksPort, cancellationToken).ConfigureAwait(false);
    }

    private async Task<int?> MeasureViaCoreAsync(ProxyServer server, CancellationToken cancellationToken)
    {
        await _speedtestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        Process? process = null;
        try
        {
            var configDir = Path.Combine(_environment.GetDataDirectory(), "runtime");
            Directory.CreateDirectory(configDir);
            var configPath = Path.Combine(configDir, "speedtest.json");
            await File.WriteAllTextAsync(
                configPath,
                XrayConfigBuilder.BuildSpeedtest(server),
                cancellationToken).ConfigureAwait(false);

            process = StartCore(configPath);
            if (process is null)
                return null;

            await WaitForCoreReadyAsync(process, cancellationToken).ConfigureAwait(false);
            if (process.HasExited)
                return -1;

            return await ProbeThroughSocksAsync(XrayConfigBuilder.SpeedtestSocksPort, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            await StopCoreAsync(process).ConfigureAwait(false);
            _speedtestLock.Release();
        }
    }

    private static Process? StartCore(string configPath)
    {
        var corePath = AppServices.CoreEnvironment.GetCorePath();
        if (!File.Exists(corePath))
            return null;

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = corePath,
                Arguments = $"run -c \"{configPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = AppServices.CoreEnvironment.GetCoresDirectory()
            }
        };

        return process.Start() ? process : null;
    }

    private static async Task WaitForCoreReadyAsync(Process process, CancellationToken cancellationToken)
    {
        for (var i = 0; i < 20; i++)
        {
            if (process.HasExited)
                return;

            if (await IsPortOpenAsync("127.0.0.1", XrayConfigBuilder.SpeedtestSocksPort, cancellationToken)
                    .ConfigureAwait(false))
                return;

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        await Task.Delay(300, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> IsPortOpenAsync(string host, int port, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(200);
            await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task StopCoreAsync(Process? process)
    {
        if (process is null)
            return;

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                using var timeout = new CancellationTokenSource(2000);
                try
                {
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Timed out waiting for exit.
                }
            }
        }
        catch
        {
            // Best effort shutdown.
        }
        finally
        {
            process.Dispose();
        }
    }

    private static async Task<int?> ProbeThroughSocksAsync(int socksPort, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeoutMs);

        var handler = new SocketsHttpHandler
        {
            Proxy = new WebProxy($"socks5://127.0.0.1:{socksPort}"),
            UseProxy = true,
            ConnectTimeout = TimeSpan.FromSeconds(5)
        };

        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(TimeoutMs) };

        int? best = null;
        foreach (var url in PingUrls)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                    .ConfigureAwait(false);
                sw.Stop();

                if (response.IsSuccessStatusCode || (int)response.StatusCode == 204)
                {
                    var ms = (int)sw.ElapsedMilliseconds;
                    best = best is null ? ms : Math.Min(best.Value, ms);
                }
            }
            catch
            {
                // Try next URL.
            }
        }

        return best ?? -1;
    }

    private static async Task<int?> MeasureTcpAsync(ProxyServer server, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(5000);

            await client.ConnectAsync(server.Address, server.Port, timeout.Token).ConfigureAwait(false);
            sw.Stop();
            return (int)sw.ElapsedMilliseconds;
        }
        catch
        {
            return -1;
        }
    }
}
