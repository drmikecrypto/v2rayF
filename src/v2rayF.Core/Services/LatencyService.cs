using System;
using System.Collections.Concurrent;
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
    /// <summary>Raced Connect/rank HTTPS probes — first success wins.</summary>
    public static readonly string[] PingUrls =
    [
        "https://cp.cloudflare.com/generate_204",
        "https://www.gstatic.com/generate_204",
        "https://www.google.com/generate_204"
    ];

    public const string GoogleProbeUrl = "https://cp.cloudflare.com/generate_204";

    public const int TimeoutMs = 4000;
    public const int RankProbeTimeoutMs = 4000;
    /// <summary>Cold Vision/REALITY core boot often needs more than 4s for rank proxy-path.</summary>
    public const int RankProbeTimeoutVisionMs = 6000;
    public const int CoreReadyWaitMs = 2000;
    /// <summary>Connect gate budget (non-Vision). Warmup + one timed GET.</summary>
    public const int ConnectHealthProbeMs = 12000;
    /// <summary>Connect gate budget for Vision / REALITY.</summary>
    public const int ConnectHealthProbeVisionMs = 16000;
    public const int TcpConnectTimeoutMs = 1500;
    public const int SocksPollTimeoutMs = 50;
    public const int HttpConnectTimeoutMs = 2000;
    /// <summary>Test All / rank: single GET (no warmup). Connect health uses warmup separately.</summary>
    public const int TimedProbeCount = 1;
    /// <summary>Connect gate: discard one warmup GET, then this many timed samples.</summary>
    public const int ConnectHealthTimedProbeCount = 1;
    /// <summary>TUN default-route probe budget (app traffic path, not localhost SOCKS).</summary>
    public const int TunAppPathProbeMs = 4000;
    public const int DesktopSpeedtestWorkers = 3;
    public const int MobileSpeedtestWorkers = 2;

    private readonly ICoreEnvironment _environment;
    private readonly ConcurrentQueue<ICoreProcessHost> _idleHosts = new();
    private readonly SemaphoreSlim _speedtestLock;

    public string? LastProbeError { get; private set; }

    public int WorkerCount { get; }

    public LatencyService(ICoreEnvironment environment)
    {
        _environment = environment;
        WorkerCount = ResolveWorkerCount(AppServices.Platform?.IsMobile == true);
        _speedtestLock = new SemaphoreSlim(WorkerCount, WorkerCount);
        for (var i = 0; i < WorkerCount; i++)
            _idleHosts.Enqueue(environment.CreateProcessHost());
    }

    public static int ResolveWorkerCount(bool mobile) =>
        mobile ? MobileSpeedtestWorkers : DesktopSpeedtestWorkers;

    public static int GetConnectHealthProbeMs(ProxyServer server)
    {
        if (ShareLinkParser.IsVisionFlow(server) ||
            string.Equals(server.Security, "reality", StringComparison.OrdinalIgnoreCase))
            return ConnectHealthProbeVisionMs;
        return ConnectHealthProbeMs;
    }

    public static int GetRankProbeTimeoutMs(ProxyServer server)
    {
        if (ShareLinkParser.IsVisionFlow(server) ||
            string.Equals(server.Security, "reality", StringComparison.OrdinalIgnoreCase))
            return RankProbeTimeoutVisionMs;
        return RankProbeTimeoutMs;
    }

    public readonly record struct LatencyResult(int? TcpMs, int? ProxyPathMs, bool ProxyPathOk)
    {
        public int? LatencyMs => ProxyPathOk ? ProxyPathMs : TcpMs;

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

    public async Task<int?> MeasureAsync(
        ProxyServer server,
        CancellationToken cancellationToken = default,
        bool enableFragment = false)
    {
        var detailed = await MeasureDetailedAsync(server, cancellationToken, enableFragment).ConfigureAwait(false);
        return detailed.UiLatencyMs;
    }

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
            var corePath = CoreRuntime.UseSingBoxForSpeedtest(server)
                ? _environment.GetSingBoxPath()
                : _environment.GetCorePath();
            if (File.Exists(corePath))
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
                // Observed.
            }
        }

        var tcp = tcpTask.Status == TaskStatus.RanToCompletion ? tcpTask.Result : -1;
        return new LatencyResult(tcp, proxyMs, proxyMs is >= 0);
    }

    public async Task<int?> MeasureViaSocksAsync(
        int socksPort,
        CancellationToken cancellationToken = default,
        int timeoutMs = TimeoutMs)
    {
        return await ProbeThroughSocksAsync(socksPort, cancellationToken, timeoutMs, warmThenMeasure: false)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Connect gate only: one discarded warmup GET, then one timed GET (avoids cold-handshake false fails into Survive).
    /// </summary>
    public async Task<int?> MeasureConnectHealthViaSocksAsync(
        int socksPort,
        CancellationToken cancellationToken = default,
        int timeoutMs = ConnectHealthProbeMs)
    {
        return await ProbeThroughSocksAsync(socksPort, cancellationToken, timeoutMs, warmThenMeasure: true)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Android TUN Connect gate: verify VPN HTTP proxy path (Play Store / Translate use 10809).
    /// </summary>
    public async Task<int?> MeasureConnectHealthViaHttpAsync(
        int httpPort,
        CancellationToken cancellationToken = default,
        int timeoutMs = ConnectHealthProbeMs,
        bool warmThenMeasure = false)
    {
        return await ProbeThroughHttpProxyAsync(httpPort, cancellationToken, timeoutMs, warmThenMeasure)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verify default-route app traffic through TUN (not localhost SOCKS/HTTP).
    /// Used when VPN routes all apps — localhost can stay healthy while TUN/FakeIP is stale.
    /// </summary>
    public async Task<int?> MeasureTunAppPathAsync(
        CancellationToken cancellationToken = default,
        int timeoutMs = TunAppPathProbeMs)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMs);

        var connectMs = Math.Min(HttpConnectTimeoutMs, timeoutMs);
        using var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromMilliseconds(connectMs),
            UseProxy = false
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };

        try
        {
            var gen204Task = RacePingUrlsAsync(client, timeout.Token, warmThenMeasure: false);
            var pushTask = ProbePushHostAsync(timeout.Token, timeoutMs);
            await Task.WhenAll(gen204Task, pushTask).ConfigureAwait(false);

            var gen204 = await gen204Task.ConfigureAwait(false);
            if (gen204 is null or < 0)
                return gen204;

            var pushMs = await pushTask.ConfigureAwait(false);
            if (pushMs is null or < 0)
                return pushMs;

            return Math.Max(gen204.Value, pushMs.Value);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LastProbeError ??= "TUN app-path HTTPS probe timed out.";
            return -1;
        }
    }

    private static async Task<int?> ProbePushHostAsync(CancellationToken cancellationToken, int timeoutMs)
    {
        var connectMs = Math.Min(HttpConnectTimeoutMs, timeoutMs);
        using var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromMilliseconds(connectMs),
            UseProxy = false
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };

        try
        {
            var sw = Stopwatch.StartNew();
            using var request = new HttpRequestMessage(HttpMethod.Head, "https://mtalk.google.com/");
            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            sw.Stop();
            _ = response;
            return (int)sw.ElapsedMilliseconds;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return -1;
        }
    }

    public async Task<int?> MeasureProxyPathAsync(
        ProxyServer server,
        CancellationToken cancellationToken = default,
        bool enableFragment = false)
    {
        LastProbeError = null;
        if (string.IsNullOrWhiteSpace(server.Address) || server.Port <= 0)
            return -1;

        await _environment.EnsureCoreAsync(cancellationToken).ConfigureAwait(false);
        var corePath = CoreRuntime.UseSingBoxForSpeedtest(server)
            ? _environment.GetSingBoxPath()
            : _environment.GetCorePath();
        if (!File.Exists(corePath))
        {
            LastProbeError = CoreRuntime.UseSingBoxForSpeedtest(server)
                ? "sing-box core not found."
                : "Xray core not found.";
            return -1;
        }

        return await MeasureViaCoreAsync(server, enableFragment, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Skip expensive Xray boot when TCP to an IP already failed.</summary>
    public static bool ShouldSkipProxyPath(ProxyServer server, int? tcpMs)
    {
        if (tcpMs is > 0)
            return false;
        if (string.IsNullOrWhiteSpace(server.Address))
            return true;
        if (!IPAddress.TryParse(server.Address, out _))
            return false;

        var network = ShareLinkParser.NormalizeNetwork(server.Network);
        return network is not ("ws" or "grpc" or "httpupgrade" or "h2" or "xhttp");
    }

    private async Task<int?> MeasureViaCoreAsync(
        ProxyServer server,
        bool enableFragment,
        CancellationToken cancellationToken)
    {
        await _speedtestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        ICoreProcessHost? host = null;
        try
        {
            if (!_idleHosts.TryDequeue(out host))
                host = _environment.CreateProcessHost();

            var socksPort = GetFreeTcpPort();
            var configDir = Path.Combine(_environment.GetDataDirectory(), "runtime");
            Directory.CreateDirectory(configDir);
            // Speedtest: classic on Xray even when live Connect prefers sing-box on Android.
            var useSingBox = CoreRuntime.UseSingBoxForSpeedtest(server);
            var configPath = Path.Combine(configDir, useSingBox ? $"speedtest-sb-{socksPort}.json" : $"speedtest-{socksPort}.json");
            var configJson = useSingBox
                ? SingBoxConfigBuilder.BuildSpeedtest(server, socksPort)
                : XrayConfigBuilder.BuildSpeedtest(server, socksPort, enableFragment);
            await File.WriteAllTextAsync(configPath, configJson, cancellationToken).ConfigureAwait(false);

            var corePath = useSingBox ? _environment.GetSingBoxPath() : _environment.GetCorePath();
            if (!File.Exists(corePath))
            {
                LastProbeError = useSingBox ? "sing-box core not found." : "Xray core not found.";
                return null;
            }

            await host.StartAsync(
                corePath,
                configPath,
                _environment.GetCoresDirectory(),
                tunFd: null,
                cancellationToken).ConfigureAwait(false);

            await WaitForCoreReadyAsync(host, socksPort, server, cancellationToken).ConfigureAwait(false);
            if (host.HasExited)
            {
                await Task.Delay(100, CancellationToken.None).ConfigureAwait(false);
                var err = host.GetRecentError();
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

            var ms = await ProbeThroughSocksAsync(socksPort, cancellationToken, TimeoutMs).ConfigureAwait(false);
            if (ms is null or < 0)
                LastProbeError ??= "Proxy-path HTTPS probe timed out.";
            return ms;
        }
        catch (InvalidOperationException ex)
        {
            LastProbeError = StatusSanitizer.Scrub(ex.Message);
            return -1;
        }
        finally
        {
            if (host is not null)
            {
                await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
                _idleHosts.Enqueue(host);
            }

            _speedtestLock.Release();
        }
    }

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

    private static async Task WaitForCoreReadyAsync(
        ICoreProcessHost host,
        int socksPort,
        ProxyServer server,
        CancellationToken cancellationToken)
    {
        var waitMs = GetCoreReadyWaitMs(server);
        var iterations = Math.Max(1, waitMs / SocksPollTimeoutMs);
        for (var i = 0; i < iterations; i++)
        {
            if (host.HasExited)
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

    public const string SocksProxyScheme = "socks5";

    private async Task<int?> ProbeThroughSocksAsync(
        int socksPort,
        CancellationToken cancellationToken,
        int timeoutMs,
        bool warmThenMeasure = false)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMs);

        var connectMs = Math.Min(HttpConnectTimeoutMs, timeoutMs);
        var handler = new SocketsHttpHandler
        {
            Proxy = new WebProxy($"{SocksProxyScheme}://127.0.0.1:{socksPort}"),
            UseProxy = true,
            ConnectTimeout = TimeSpan.FromMilliseconds(connectMs)
        };

        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };

        try
        {
            return await RacePingUrlsAsync(client, timeout.Token, warmThenMeasure).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LastProbeError ??= "Proxy-path HTTPS probe timed out.";
            return -1;
        }
    }

    private async Task<int?> ProbeThroughHttpProxyAsync(
        int httpPort,
        CancellationToken cancellationToken,
        int timeoutMs,
        bool warmThenMeasure = false)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMs);

        var connectMs = Math.Min(HttpConnectTimeoutMs, timeoutMs);
        var handler = new SocketsHttpHandler
        {
            Proxy = new WebProxy($"http://127.0.0.1:{httpPort}"),
            UseProxy = true,
            ConnectTimeout = TimeSpan.FromMilliseconds(connectMs)
        };

        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };

        try
        {
            return await RacePingUrlsAsync(client, timeout.Token, warmThenMeasure).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LastProbeError ??= "HTTP proxy-path HTTPS probe timed out.";
            return -1;
        }
    }

    /// <summary>Race PingUrls; first successful GET wins (warmup+timed when Connect gate).</summary>
    private async Task<int?> RacePingUrlsAsync(
        HttpClient client,
        CancellationToken cancellationToken,
        bool warmThenMeasure)
    {
        using var raceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var errorBox = new Exception?[1];
        var tasks = PingUrls.Select(async url =>
        {
            try
            {
                if (warmThenMeasure)
                {
                    _ = await ProbeOneAsync(client, url, raceCts.Token).ConfigureAwait(false);
                    raceCts.Token.ThrowIfCancellationRequested();

                    int? best = null;
                    for (var i = 0; i < ConnectHealthTimedProbeCount; i++)
                    {
                        raceCts.Token.ThrowIfCancellationRequested();
                        var timed = await ProbeOneAsync(client, url, raceCts.Token).ConfigureAwait(false);
                        if (timed.Ok && timed.Ms >= 0)
                            best = best is null ? timed.Ms : Math.Min(best.Value, timed.Ms);
                        else if (timed.Error is not null)
                            Interlocked.CompareExchange(ref errorBox[0], timed.Error, null);
                    }

                    if (best is >= 0)
                        return best;
                    return (int?)-1;
                }

                var one = await ProbeOneAsync(client, url, raceCts.Token).ConfigureAwait(false);
                if (one.Ok && one.Ms >= 0)
                    return (int?)one.Ms;
                if (one.Error is not null)
                    Interlocked.CompareExchange(ref errorBox[0], one.Error, null);
                return (int?)-1;
            }
            catch (OperationCanceledException)
            {
                return (int?)-1;
            }
        }).ToList();

        while (tasks.Count > 0)
        {
            var finished = await Task.WhenAny(tasks).ConfigureAwait(false);
            tasks.Remove(finished);
            int? ms;
            try
            {
                ms = await finished.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                continue;
            }

            if (ms is >= 0)
            {
                try { raceCts.Cancel(); }
                catch (ObjectDisposedException) { /* ignore */ }
                return ms;
            }
        }

        if (errorBox[0] is not null)
            LastProbeError = StatusSanitizer.Scrub(errorBox[0]!.Message);

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
        }
        catch (Exception ex)
        {
            return (false, -1, ex);
        }

        return (false, -1, null);
    }

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
