using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
        "https://www.google.com/generate_204",
        "https://www.google.com/"
    ];

    /// <summary>Proxy-path destination used for latency probes and multipath observatory.</summary>
    public const string GoogleProbeUrl = "https://www.google.com/generate_204";

    private readonly ICoreEnvironment _environment;
    private readonly ICoreProcessHost _speedtestHost;
    private readonly SemaphoreSlim _speedtestLock = new(1, 1);

    public const int TimeoutMs = 10000;
    /// <summary>Per-probe budget during Smart Connect ranking (keeps connect agile).</summary>
    public const int RankProbeTimeoutMs = 4500;

    public LatencyService(ICoreEnvironment environment)
    {
        _environment = environment;
        _speedtestHost = environment.CreateProcessHost();
    }

    public async Task<int?> MeasureTcpOnlyAsync(ProxyServer server, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(server.Address) || server.Port <= 0)
            return null;

        return await MeasureTcpAsync(server, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int?> MeasureAsync(ProxyServer server, CancellationToken cancellationToken = default)
    {
        var detailed = await MeasureDetailedAsync(server, cancellationToken).ConfigureAwait(false);
        return detailed.LatencyMs;
    }

    public readonly record struct LatencyResult(int? LatencyMs, bool ProxyPathOk);

    /// <summary>
    /// Prefer proxy-path RTT to www.google.com; fall back to TCP. ProxyPathOk is true only when Google via the node succeeded.
    /// </summary>
    public async Task<LatencyResult> MeasureDetailedAsync(ProxyServer server, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(server.Address) || server.Port <= 0)
            return new LatencyResult(null, false);

        await _environment.EnsureCoreAsync(cancellationToken).ConfigureAwait(false);
        if (File.Exists(_environment.GetCorePath()))
        {
            var proxyResult = await MeasureViaCoreAsync(server, cancellationToken).ConfigureAwait(false);
            if (proxyResult.HasValue && proxyResult.Value >= 0)
                return new LatencyResult(proxyResult, true);
        }

        var tcp = await MeasureTcpAsync(server, cancellationToken).ConfigureAwait(false);
        if (tcp.HasValue && tcp.Value >= 0)
            return new LatencyResult(tcp, false);

        return new LatencyResult(tcp ?? -1, false);
    }

    public async Task<int?> MeasureViaSocksAsync(int socksPort, CancellationToken cancellationToken = default)
    {
        return await ProbeThroughSocksAsync(socksPort, cancellationToken).ConfigureAwait(false);
    }

    private async Task<int?> MeasureViaCoreAsync(ProxyServer server, CancellationToken cancellationToken)
    {
        await _speedtestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var configDir = Path.Combine(_environment.GetDataDirectory(), "runtime");
            Directory.CreateDirectory(configDir);
            var configPath = Path.Combine(configDir, "speedtest.json");
            await File.WriteAllTextAsync(
                configPath,
                XrayConfigBuilder.BuildSpeedtest(server),
                cancellationToken).ConfigureAwait(false);

            var corePath = _environment.GetCorePath();
            if (!File.Exists(corePath))
                return null;

            await _speedtestHost.StartAsync(
                corePath,
                configPath,
                _environment.GetCoresDirectory(),
                tunFd: null,
                cancellationToken).ConfigureAwait(false);

            await WaitForCoreReadyAsync(cancellationToken).ConfigureAwait(false);
            if (_speedtestHost.HasExited)
                return -1;

            return await ProbeThroughSocksAsync(XrayConfigBuilder.SpeedtestSocksPort, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            await _speedtestHost.StopAsync(cancellationToken).ConfigureAwait(false);
            _speedtestLock.Release();
        }
    }

    private async Task WaitForCoreReadyAsync(CancellationToken cancellationToken)
    {
        for (var i = 0; i < 40; i++)
        {
            if (_speedtestHost.HasExited)
                return;

            if (await IsPortOpenAsync("127.0.0.1", XrayConfigBuilder.SpeedtestSocksPort, cancellationToken)
                    .ConfigureAwait(false))
                return;

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<bool> IsPortOpenAsync(string host, int port, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(150);
            await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Returns on first successful response from www.google.com via the node (does not wait for every URL).</summary>
    private static async Task<int?> ProbeThroughSocksAsync(int socksPort, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeoutMs);
        using var raceCts = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);

        var handler = new SocketsHttpHandler
        {
            Proxy = new WebProxy($"socks5h://127.0.0.1:{socksPort}"),
            UseProxy = true,
            ConnectTimeout = TimeSpan.FromSeconds(4)
        };

        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(TimeoutMs) };

        var tasks = new Task<(bool Ok, int Ms)>[PingUrls.Length];
        for (var i = 0; i < PingUrls.Length; i++)
        {
            var url = PingUrls[i];
            tasks[i] = ProbeOneAsync(client, url, raceCts.Token);
        }

        var pending = tasks.ToList();
        while (pending.Count > 0)
        {
            var finished = await Task.WhenAny(pending).ConfigureAwait(false);
            pending.Remove(finished);
            var (ok, ms) = await finished.ConfigureAwait(false);
            if (ok)
            {
                raceCts.Cancel();
                return ms;
            }
        }

        return -1;
    }

    private static async Task<(bool Ok, int Ms)> ProbeOneAsync(
        HttpClient client,
        string url,
        CancellationToken cancellationToken)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            sw.Stop();
            if (response.IsSuccessStatusCode || (int)response.StatusCode == 204)
                return (true, (int)sw.ElapsedMilliseconds);
        }
        catch
        {
            // Rival URL or timeout.
        }

        return (false, -1);
    }

    private static async Task<int?> MeasureTcpAsync(ProxyServer server, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(3500);

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
