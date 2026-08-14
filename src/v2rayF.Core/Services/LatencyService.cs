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
        "https://cp.cloudflare.com/generate_204",
        "https://www.gstatic.com/generate_204",
        "https://www.google.com/generate_204",
        "https://www.google.com/"
    ];

    /// <summary>Primary proxy-path probe URL (Cloudflare; Google retained as fallback).</summary>
    public const string GoogleProbeUrl = "https://cp.cloudflare.com/generate_204";

    private readonly ICoreEnvironment _environment;
    private readonly ICoreProcessHost _speedtestHost;
    private readonly SemaphoreSlim _speedtestLock = new(1, 1);

    public const int TimeoutMs = 10000;
    /// <summary>Per-probe budget during Smart Connect ranking (matches manual Test).</summary>
    public const int RankProbeTimeoutMs = 10000;
    /// <summary>Default max wait for speedtest SOCKS to bind before probing.</summary>
    public const int CoreReadyWaitMs = 2000;
    /// <summary>Post-connect live SOCKS health probe budget.</summary>
    public const int ConnectHealthProbeMs = 10000;

    /// <summary>Last core/speedtest failure detail (sanitized for UI).</summary>
    public string? LastProbeError { get; private set; }

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

    /// <summary>
    /// Proxy-path delay only. Returns -1 when the node does not successfully proxy an HTTPS probe
    /// (TCP-only reachability is never reported as a successful ping).
    /// </summary>
    public async Task<int?> MeasureAsync(
        ProxyServer server,
        CancellationToken cancellationToken = default,
        bool enableFragment = false)
    {
        var detailed = await MeasureDetailedAsync(server, cancellationToken, enableFragment).ConfigureAwait(false);
        if (detailed.ProxyPathOk && detailed.LatencyMs is >= 0)
            return detailed.LatencyMs;

        return -1;
    }

    public readonly record struct LatencyResult(int? LatencyMs, bool ProxyPathOk);

    /// <summary>
    /// Prefer proxy-path RTT via Cloudflare/Google probes. When the proxy path fails, LatencyMs may still
    /// contain a TCP fallback for Smart Connect prefilter scoring, but ProxyPathOk is false.
    /// UI Test/Test All must use <see cref="MeasureAsync"/> so TCP-only never looks like a ping.
    /// </summary>
    public async Task<LatencyResult> MeasureDetailedAsync(
        ProxyServer server,
        CancellationToken cancellationToken = default,
        bool enableFragment = false)
    {
        LastProbeError = null;
        if (string.IsNullOrWhiteSpace(server.Address) || server.Port <= 0)
            return new LatencyResult(null, false);

        await _environment.EnsureCoreAsync(cancellationToken).ConfigureAwait(false);
        if (File.Exists(_environment.GetCorePath()))
        {
            var proxyResult = await MeasureViaCoreAsync(server, enableFragment, cancellationToken)
                .ConfigureAwait(false);
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

    private async Task<int?> MeasureViaCoreAsync(
        ProxyServer server,
        bool enableFragment,
        CancellationToken cancellationToken)
    {
        await _speedtestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var socksPort = GetFreeTcpPort();
            var configDir = Path.Combine(_environment.GetDataDirectory(), "runtime");
            Directory.CreateDirectory(configDir);
            var configPath = Path.Combine(configDir, $"speedtest-{socksPort}.json");
            await File.WriteAllTextAsync(
                configPath,
                XrayConfigBuilder.BuildSpeedtest(server, socksPort, enableFragment),
                cancellationToken).ConfigureAwait(false);

            var corePath = _environment.GetCorePath();
            if (!File.Exists(corePath))
            {
                LastProbeError = "Xray core not found.";
                return null;
            }

            await _speedtestHost.StartAsync(
                corePath,
                configPath,
                _environment.GetCoresDirectory(),
                tunFd: null,
                cancellationToken).ConfigureAwait(false);

            await WaitForCoreReadyAsync(socksPort, server, cancellationToken).ConfigureAwait(false);
            if (_speedtestHost.HasExited)
            {
                await Task.Delay(100, CancellationToken.None).ConfigureAwait(false);
                var err = _speedtestHost.GetRecentError();
                LastProbeError = string.IsNullOrWhiteSpace(err)
                    ? "Speedtest core exited before SOCKS was ready."
                    : StatusSanitizer.Scrub(err);
                return -1;
            }

            if (!await IsPortOpenAsync("127.0.0.1", socksPort, cancellationToken).ConfigureAwait(false))
            {
                LastProbeError = $"Speedtest SOCKS did not bind on 127.0.0.1:{socksPort}.";
                return -1;
            }

            var ms = await ProbeThroughSocksAsync(socksPort, cancellationToken).ConfigureAwait(false);
            if (ms is null or < 0)
                LastProbeError ??= "Proxy-path HTTPS probe timed out.";
            return ms;
        }
        finally
        {
            await _speedtestHost.StopAsync(cancellationToken).ConfigureAwait(false);
            _speedtestLock.Release();
        }
    }

    /// <summary>Transport-aware SOCKS bind budget (WS/gRPC need longer than bare TCP).</summary>
    public static int GetCoreReadyWaitMs(ProxyServer server)
    {
        var network = ShareLinkParser.NormalizeNetwork(server.Network);
        return network switch
        {
            "grpc" or "httpupgrade" => 4000,
            "ws" or "h2" or "xhttp" => 3000,
            "tcp" when string.Equals(server.Security, "reality", StringComparison.OrdinalIgnoreCase) => 2500,
            _ => CoreReadyWaitMs
        };
    }

    private async Task WaitForCoreReadyAsync(int socksPort, ProxyServer server, CancellationToken cancellationToken)
    {
        var waitMs = GetCoreReadyWaitMs(server);
        var iterations = Math.Max(1, waitMs / 50);
        for (var i = 0; i < iterations; i++)
        {
            if (_speedtestHost.HasExited)
                return;

            if (await IsPortOpenAsync("127.0.0.1", socksPort, cancellationToken).ConfigureAwait(false))
                return;

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
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

    /// <summary>
    /// SOCKS scheme for HttpClient probes. Must be <c>socks5</c> — .NET rejects <c>socks5h</c>
    /// (<see cref="NotSupportedException"/>), which made every latency/connect probe look like a timeout.
    /// On .NET, socks5 already sends the hostname to the proxy.
    /// </summary>
    public const string SocksProxyScheme = "socks5";

    /// <summary>Returns on first successful HTTPS probe via the node (does not wait for every URL).</summary>
    private async Task<int?> ProbeThroughSocksAsync(int socksPort, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeoutMs);
        using var raceCts = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);

        var handler = new SocketsHttpHandler
        {
            Proxy = new WebProxy($"{SocksProxyScheme}://127.0.0.1:{socksPort}"),
            UseProxy = true,
            ConnectTimeout = TimeSpan.FromSeconds(4)
        };

        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(TimeoutMs) };

        var tasks = new Task<(bool Ok, int Ms, Exception? Error)>[PingUrls.Length];
        for (var i = 0; i < PingUrls.Length; i++)
        {
            var url = PingUrls[i];
            tasks[i] = ProbeOneAsync(client, url, raceCts.Token);
        }

        var pending = tasks.ToList();
        Exception? firstError = null;
        while (pending.Count > 0)
        {
            var finished = await Task.WhenAny(pending).ConfigureAwait(false);
            pending.Remove(finished);
            var (ok, ms, error) = await finished.ConfigureAwait(false);
            if (ok)
            {
                raceCts.Cancel();
                return ms;
            }

            firstError ??= error;
        }

        if (firstError is not null)
            LastProbeError = StatusSanitizer.Scrub(firstError.Message);

        return -1;
    }

    private static async Task<(bool Ok, int Ms, Exception? Error)> ProbeOneAsync(
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
                return (true, (int)sw.ElapsedMilliseconds, null);
        }
        catch (OperationCanceledException)
        {
            // Rival URL or overall timeout.
        }
        catch (Exception ex)
        {
            return (false, -1, ex);
        }

        return (false, -1, null);
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
