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
    /// <summary>Sequential fallbacks — never raced on a cold SOCKS (that inflated delay to ~2000ms).</summary>
    public static readonly string[] PingUrls =
    [
        "https://cp.cloudflare.com/generate_204",
        "https://www.gstatic.com/generate_204",
        "https://www.google.com/generate_204"
    ];

    /// <summary>Primary proxy-path probe URL (Cloudflare; Google retained as fallback).</summary>
    public const string GoogleProbeUrl = "https://cp.cloudflare.com/generate_204";

    private readonly ICoreEnvironment _environment;
    private readonly ICoreProcessHost _speedtestHost;
    private readonly SemaphoreSlim _speedtestLock = new(1, 1);

    public const int TimeoutMs = 8000;
    /// <summary>Per-probe budget during Smart Connect ranking (warmup + timed GETs).</summary>
    public const int RankProbeTimeoutMs = 10000;
    /// <summary>Default max wait for speedtest SOCKS to bind before probing.</summary>
    public const int CoreReadyWaitMs = 2000;
    /// <summary>Post-connect live SOCKS health probe budget.</summary>
    public const int ConnectHealthProbeMs = 4000;
    /// <summary>TCP connect budget per attempt (v2rayN-style tcping).</summary>
    public const int TcpConnectTimeoutMs = 1500;
    public const int SocksPollTimeoutMs = 50;
    public const int HttpConnectTimeoutMs = 2000;
    public const int TimedProbeCount = 2;

    /// <summary>Last core/speedtest failure detail (sanitized for UI).</summary>
    public string? LastProbeError { get; private set; }

    public LatencyService(ICoreEnvironment environment)
    {
        _environment = environment;
        _speedtestHost = environment.CreateProcessHost();
    }

    public readonly record struct LatencyResult(int? TcpMs, int? ProxyPathMs, bool ProxyPathOk)
    {
        /// <summary>Warmed proxy-path RTT when the tunnel works; TCP otherwise (prefilter only).</summary>
        public int? LatencyMs => ProxyPathOk ? ProxyPathMs : TcpMs;

        /// <summary>UI column: TCP RTT after proxy-path OK; timeout otherwise. Never TCP-only.</summary>
        public int? UiLatencyMs =>
            !ProxyPathOk ? -1
            : TcpMs is > 0 ? TcpMs
            : ProxyPathMs is > 0 ? ProxyPathMs
            : -1;
    }

    public async Task<int?> MeasureTcpOnlyAsync(ProxyServer server, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(server.Address) || server.Port <= 0)
            return null;

        return await MeasureTcpAsync(server, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// UI Test / Test All: TCP RTT when the node also proxies HTTPS; otherwise -1 (timeout).
    /// TCP-only reachability is never shown as a successful ping.
    /// </summary>
    public async Task<int?> MeasureAsync(
        ProxyServer server,
        CancellationToken cancellationToken = default,
        bool enableFragment = false)
    {
        var detailed = await MeasureDetailedAsync(server, cancellationToken, enableFragment).ConfigureAwait(false);
        return detailed.UiLatencyMs;
    }

    /// <summary>
    /// TCP + warmed proxy-path. Smart Connect ranks by <see cref="LatencyResult.ProxyPathMs"/>;
    /// the list column uses <see cref="LatencyResult.UiLatencyMs"/>.
    /// </summary>
    public async Task<LatencyResult> MeasureDetailedAsync(
        ProxyServer server,
        CancellationToken cancellationToken = default,
        bool enableFragment = false)
    {
        LastProbeError = null;
        if (string.IsNullOrWhiteSpace(server.Address) || server.Port <= 0)
            return new LatencyResult(null, null, false);

        var tcpTask = MeasureTcpAsync(server, cancellationToken);

        int? proxyMs = null;
        try
        {
            await _environment.EnsureCoreAsync(cancellationToken).ConfigureAwait(false);
            if (File.Exists(_environment.GetCorePath()))
            {
                proxyMs = await MeasureViaCoreAsync(server, enableFragment, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            try
            {
                await tcpTask.ConfigureAwait(false);
            }
            catch
            {
                // Observed; TCP failure is represented as -1 below.
            }
        }

        var tcp = tcpTask.Status == TaskStatus.RanToCompletion ? tcpTask.Result : -1;
        var proxyOk = proxyMs is >= 0;
        return new LatencyResult(tcp, proxyMs, proxyOk);
    }

    /// <summary>HTTPS-through-SOCKS only (live connect health). Warmup + min of timed GETs.</summary>
    public async Task<int?> MeasureViaSocksAsync(int socksPort, CancellationToken cancellationToken = default)
    {
        return await ProbeThroughSocksAsync(socksPort, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Ephemeral-core proxy-path RTT (no TCP). Used by Test All after a parallel TCP pass.</summary>
    public async Task<int?> MeasureProxyPathAsync(
        ProxyServer server,
        CancellationToken cancellationToken = default,
        bool enableFragment = false)
    {
        LastProbeError = null;
        if (string.IsNullOrWhiteSpace(server.Address) || server.Port <= 0)
            return -1;

        await _environment.EnsureCoreAsync(cancellationToken).ConfigureAwait(false);
        if (!File.Exists(_environment.GetCorePath()))
        {
            LastProbeError = "Xray core not found.";
            return -1;
        }

        return await MeasureViaCoreAsync(server, enableFragment, cancellationToken).ConfigureAwait(false);
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
        var iterations = Math.Max(1, waitMs / SocksPollTimeoutMs);
        for (var i = 0; i < iterations; i++)
        {
            if (_speedtestHost.HasExited)
                return;

            if (await IsPortOpenAsync("127.0.0.1", socksPort, cancellationToken).ConfigureAwait(false))
                return;

            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
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
            timeout.CancelAfter(SocksPollTimeoutMs);
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

    /// <summary>
    /// Sequential URL fallbacks. Warmup GET is discarded; min of timed GETs is the proxy-path RTT
    /// (same pattern as v2rayN GetRealPingTime).
    /// </summary>
    private async Task<int?> ProbeThroughSocksAsync(int socksPort, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeoutMs);

        var handler = new SocketsHttpHandler
        {
            Proxy = new WebProxy($"{SocksProxyScheme}://127.0.0.1:{socksPort}"),
            UseProxy = true,
            ConnectTimeout = TimeSpan.FromMilliseconds(HttpConnectTimeoutMs)
        };

        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(TimeoutMs) };

        Exception? firstError = null;
        try
        {
            foreach (var url in PingUrls)
            {
                timeout.Token.ThrowIfCancellationRequested();

                var warm = await ProbeOneAsync(client, url, timeout.Token).ConfigureAwait(false);
                if (!warm.Ok)
                {
                    firstError ??= warm.Error;
                    continue;
                }

                var times = new List<int>(TimedProbeCount);
                for (var i = 0; i < TimedProbeCount; i++)
                {
                    var timed = await ProbeOneAsync(client, url, timeout.Token).ConfigureAwait(false);
                    if (timed.Ok && timed.Ms >= 0)
                        times.Add(timed.Ms);

                    if (i + 1 < TimedProbeCount)
                        await Task.Delay(100, timeout.Token).ConfigureAwait(false);
                }

                if (times.Count > 0)
                    return times.Min();
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LastProbeError ??= "Proxy-path HTTPS probe timed out.";
            return -1;
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
            // Overall timeout.
        }
        catch (Exception ex)
        {
            return (false, -1, ex);
        }

        return (false, -1, null);
    }

    /// <summary>Two TCP connects; first warms DNS, second is the reported RTT (v2rayN tcping).</summary>
    private static async Task<int?> MeasureTcpAsync(ProxyServer server, CancellationToken cancellationToken)
    {
        var first = await TcpConnectOnceAsync(server, cancellationToken).ConfigureAwait(false);
        var second = await TcpConnectOnceAsync(server, cancellationToken).ConfigureAwait(false);
        if (second is > 0)
            return second;
        if (first is > 0)
            return first;
        return -1;
    }

    private static async Task<int?> TcpConnectOnceAsync(ProxyServer server, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TcpConnectTimeoutMs);

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
