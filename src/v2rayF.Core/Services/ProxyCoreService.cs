using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using v2rayF.Models;

namespace v2rayF.Services;

public sealed class ProxyCoreService : IAsyncDisposable
{
    public const int ConnectTimeoutMs = 15000;
    /// <summary>Extra SOCKS bind budget for sing-box + Android VPN TUN (system stack).</summary>
    public const int SingBoxTunReadyBonusMs = 7000;
    public const int CoreReadyPollMs = 50;
    public const int HealthCheckIntervalMs = 5000;
    public const int HealthPortTimeoutMs = 500;
    public const int HealthSocksFailThreshold = 3;
    public const int HealthSocksFailGapMs = 400;
    public const int PathHealthIntervalMs = 60000;
    public const int PathHealthFailThreshold = 3;
    public const int PathHealthProbeMs = 8000;
    public const int PathHealthProbeVisionMs = 12000;
    public const int NatKeepaliveIntervalMs = 25000;
    public const int NatKeepaliveProbeMs = 2000;

    private readonly ICoreEnvironment _environment;
    private readonly LatencyService _latency;
    private string? _configPath;
    private CancellationTokenSource? _healthCts;
    private int _unexpectedHandled;

    private static ICoreProcessHost ProcessHost => AppServices.CoreProcessHost;

    public ProxyCoreService(ICoreEnvironment environment)
    {
        _environment = environment;
        _latency = new LatencyService(environment);
        ProcessHost.UnexpectedExited += OnUnexpectedExited;
    }

    /// <summary>Last successful post-connect proxy-path RTT (ms), if health probe ran.</summary>
    public int? LastConnectProbeMs { get; private set; }

    public bool IsRunning => ProcessHost.IsRunning;

    public ProxyServer? ActiveServer { get; private set; }

    public event EventHandler<bool>? RunningStateChanged;

    /// <summary>Raised when the core dies unexpectedly while we thought it was connected.</summary>
    public event EventHandler? UnexpectedStop;

    public string ResolveCorePath() => _environment.GetCorePath();

    public string ResolveCorePathFor(ProxyServer server) =>
        CoreRuntime.UseSingBox(server) ? _environment.GetSingBoxPath() : _environment.GetCorePath();

    public string ResolveCoresDirectory() => _environment.GetCoresDirectory();

    public bool IsCoreAvailable() => File.Exists(ResolveCorePath());

    public bool IsCoreAvailableFor(ProxyServer server) => File.Exists(ResolveCorePathFor(server));

    public bool HasGeoFiles()
    {
        var cores = ResolveCoresDirectory();
        return File.Exists(Path.Combine(cores, "geoip.dat")) &&
               File.Exists(Path.Combine(cores, "geosite.dat"));
    }

    /// <summary>Raised when a soft path probe succeeds (reset AutoReconnect budget).</summary>
    public event EventHandler? PathHealthOk;

    /// <summary>True when consecutive SOCKS probe misses should declare the tunnel dead.</summary>
    public static bool ShouldRaiseOnSocksFails(int consecutiveFails) =>
        consecutiveFails >= HealthSocksFailThreshold;

    /// <summary>True when consecutive flat-traffic path probes should declare a zombie tunnel.</summary>
    public static bool ShouldRaiseOnPathFails(int consecutiveFails) =>
        consecutiveFails >= PathHealthFailThreshold;

    public static int GetPathHealthProbeMs(ProxyServer? server)
    {
        if (server is not null &&
            (ShareLinkParser.IsVisionFlow(server) ||
             string.Equals(server.Security, "reality", StringComparison.OrdinalIgnoreCase)))
            return PathHealthProbeVisionMs;
        return PathHealthProbeMs;
    }

    public static bool IsTrafficFlat(TrafficStatsHub.LiveTraffic traffic) =>
        traffic.UplinkBps + traffic.DownlinkBps <= 0;

    public async Task StartAsync(
        ProxyServer server,
        AppSettings settings,
        int? tunFd = null,
        IReadOnlyList<ProxyServer>? multipathServers = null,
        CancellationToken cancellationToken = default)
    {
        await _environment.EnsureCoreAsync(cancellationToken).ConfigureAwait(false);

        var useSingBox = CoreRuntime.UseSingBox(server);
        if (!IsCoreAvailableFor(server))
            throw new FileNotFoundException(
                useSingBox
                    ? "sing-box core not found. Use in-app Update to get a build that includes sing-box, or place the binary next to Xray in cores/ (desktop)."
                    : "Xray core not found.",
                ResolveCorePathFor(server));

        if (settings.EnableTunMode && !AppServices.Platform.CanUseTunMode)
            throw new InvalidOperationException(AppServices.Platform.TunRequirementMessage);

        if (settings.RoutingMode == RoutingMode.BypassChina && !HasGeoFiles() &&
            !useSingBox)
            throw new InvalidOperationException(
                "Bypass China routing requires geoip.dat and geosite.dat in the cores folder.");

        await StopAsync(cancellationToken).ConfigureAwait(false);
        Interlocked.Exchange(ref _unexpectedHandled, 0);

        if (settings.SecureShareEnabled)
            XrayConfigBuilder.EnsureShareCredentials(settings);

        if (useSingBox && multipathServers is { Count: > 0 })
            multipathServers = null; // sing-box multipath not in this build

        var configJson = useSingBox
            ? SingBoxConfigBuilder.Build(server, settings, tunFd: tunFd)
            : XrayConfigBuilder.Build(server, settings, tunFd, multipathServers);
        var configDir = Path.Combine(_environment.GetDataDirectory(), "runtime");
        Directory.CreateDirectory(configDir);
        _configPath = Path.Combine(configDir, useSingBox ? "singbox-config.json" : "config.json");
        await File.WriteAllTextAsync(_configPath, configJson, cancellationToken).ConfigureAwait(false);

        // Pass TUN fd for both cores — sing-box reads SING_BOX_TUN_FD (patched libsingbox.so); Xray uses xray.tun.fd env.
        await ProcessHost.StartAsync(
            ResolveCorePathFor(server),
            _configPath,
            ResolveCoresDirectory(),
            tunFd,
            cancellationToken).ConfigureAwait(false);

        using var readyTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readyTimeout.CancelAfter(ConnectTimeoutMs);

        await WaitForCoreReadyAsync(server, useSingBox, tunFd, readyTimeout.Token).ConfigureAwait(false);

        // Gate Connected on a real proxy-path probe — SOCKS listen alone is not enough.
        LastConnectProbeMs = null;
        var probeMs = await ProbeConnectPathAsync(server, useSingBox, tunFd, healthBudget: null, cancellationToken)
            .ConfigureAwait(false);

        // If DoH was off and the gate failed, one automatic Secure-DNS retry (no fragment).
        if (probeMs is null or < 0 && !settings.DnsThroughProxy)
        {
            var dohSettings = CloneSettingsWithDoH(settings);
            await StopAsync(cancellationToken).ConfigureAwait(false);
            Interlocked.Exchange(ref _unexpectedHandled, 0);

            configJson = useSingBox
                ? SingBoxConfigBuilder.Build(server, dohSettings, tunFd: tunFd)
                : XrayConfigBuilder.Build(server, dohSettings, tunFd, multipathServers);
            await File.WriteAllTextAsync(_configPath!, configJson, cancellationToken).ConfigureAwait(false);

            await ProcessHost.StartAsync(
                ResolveCorePathFor(server),
                _configPath!,
                ResolveCoresDirectory(),
                tunFd,
                cancellationToken).ConfigureAwait(false);

            using var readyTimeout2 = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readyTimeout2.CancelAfter(ConnectTimeoutMs);
            await WaitForCoreReadyAsync(server, useSingBox, tunFd, readyTimeout2.Token).ConfigureAwait(false);

            probeMs = await ProbeConnectPathAsync(server, useSingBox, tunFd, healthBudget: null, cancellationToken)
                .ConfigureAwait(false);
        }

        if (probeMs is null or < 0)
        {
            await StopAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(
                RequiresAndroidTunHttpProbe(useSingBox, tunFd)
                    ? "Proxy path failed after connect (HTTP proxy 10809 probe timed out). Play Store needs the VPN HTTP proxy."
                    : "Proxy path failed after connect (HTTPS probe timed out). The tunnel is not usable.");
        }

        LastConnectProbeMs = probeMs;

        ActiveServer = server;
        StartHealthMonitor();
        RunningStateChanged?.Invoke(this, true);
    }

    private async Task<int?> ProbeConnectPathAsync(
        ProxyServer server,
        bool useSingBox,
        int? tunFd,
        int? healthBudget,
        CancellationToken cancellationToken)
    {
        var budget = healthBudget ?? LatencyService.GetConnectHealthProbeMs(server);
        using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        probeCts.CancelAfter(budget);
        try
        {
            if (!RequiresAndroidTunHttpProbe(useSingBox, tunFd))
            {
                return await _latency
                    .MeasureConnectHealthViaSocksAsync(XrayConfigBuilder.SocksPort, probeCts.Token, budget)
                    .ConfigureAwait(false);
            }

            // Parallel SOCKS + HTTP: SOCKS warms TLS; HTTP skips second warmup (faster Connect).
            var socksTask = _latency.MeasureConnectHealthViaSocksAsync(
                XrayConfigBuilder.SocksPort, probeCts.Token, budget);
            var httpTask = _latency.MeasureConnectHealthViaHttpAsync(
                XrayConfigBuilder.HttpPort, probeCts.Token, budget, warmThenMeasure: false);
            await Task.WhenAll(socksTask, httpTask).ConfigureAwait(false);

            var socksMs = await socksTask.ConfigureAwait(false);
            if (socksMs is null or < 0)
                return socksMs;

            var httpMs = await httpTask.ConfigureAwait(false);
            return httpMs is null or < 0 ? httpMs : Math.Max(socksMs.Value, httpMs.Value);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return -1;
        }
    }

    private static bool RequiresAndroidTunHttpProbe(bool useSingBox, int? tunFd) =>
        useSingBox && tunFd is int fd && fd >= 0;

    private static AppSettings CloneSettingsWithDoH(AppSettings settings)
    {
        // Shallow copy via JSON is heavy; mutate a clone of key fields for the retry only.
        return new AppSettings
        {
            RoutingMode = settings.RoutingMode,
            CustomDirectRules = settings.CustomDirectRules,
            CustomProxyRules = settings.CustomProxyRules,
            CustomBlockRules = settings.CustomBlockRules,
            EnableTunMode = settings.EnableTunMode,
            EnableSystemProxy = settings.EnableSystemProxy,
            SmartConnectEnabled = settings.SmartConnectEnabled,
            StartupRankServersEnabled = settings.StartupRankServersEnabled,
            LastStartupRankUtc = settings.LastStartupRankUtc,
            AllowDesktopNotificationRouting = settings.AllowDesktopNotificationRouting,
            SmartMultipathEnabled = settings.SmartMultipathEnabled,
            KillSwitchEnabled = settings.KillSwitchEnabled,
            BlockIpv6 = settings.BlockIpv6,
            DnsThroughProxy = true,
            SecureShareEnabled = settings.SecureShareEnabled,
            ShareBindPort = settings.ShareBindPort,
            ShareAuthUser = settings.ShareAuthUser,
            ShareAuthPass = settings.ShareAuthPass,
            ShareListenAllInterfaces = settings.ShareListenAllInterfaces,
            EnablePacketFragment = settings.EnablePacketFragment,
            SubscriptionViaProxy = settings.SubscriptionViaProxy,
            AndroidBypassPackages = settings.AndroidBypassPackages,
            AndroidBlockPackages = settings.AndroidBlockPackages,
            DesktopDirectProcesses = settings.DesktopDirectProcesses,
            DesktopBlockProcesses = settings.DesktopBlockProcesses,
            AdaptiveSurviveEnabled = settings.AdaptiveSurviveEnabled,
            AutoReconnectEnabled = settings.AutoReconnectEnabled
        };
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        // Prevent unexpected-exit handlers from racing a deliberate stop.
        Interlocked.Exchange(ref _unexpectedHandled, 1);
        StopHealthMonitor();
        LastConnectProbeMs = null;
        await ProcessHost.StopAsync(cancellationToken).ConfigureAwait(false);
        ActiveServer = null;
        RunningStateChanged?.Invoke(this, false);
    }

    public async ValueTask DisposeAsync()
    {
        ProcessHost.UnexpectedExited -= OnUnexpectedExited;
        await StopAsync().ConfigureAwait(false);
    }

    private void StartHealthMonitor()
    {
        StopHealthMonitor();
        _healthCts = new CancellationTokenSource();
        var token = _healthCts.Token;
        _ = Task.Run(() => HealthLoopAsync(token), CancellationToken.None);
    }

    private void StopHealthMonitor()
    {
        try
        {
            _healthCts?.Cancel();
            _healthCts?.Dispose();
        }
        catch
        {
            // ignore
        }

        _healthCts = null;
    }

    private async Task HealthLoopAsync(CancellationToken cancellationToken)
    {
        var consecutiveSocksFails = 0;
        var consecutivePathFails = 0;
        var lastPathProbeUtc = DateTimeOffset.UtcNow;
        var lastNatKeepaliveUtc = DateTimeOffset.UtcNow;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(HealthCheckIntervalMs, cancellationToken).ConfigureAwait(false);
                if (ProcessHost.HasExited)
                {
                    RaiseUnexpectedStop();
                    return;
                }

                if (await IsPortOpenAsync("127.0.0.1", XrayConfigBuilder.SocksPort, cancellationToken)
                        .ConfigureAwait(false))
                {
                    consecutiveSocksFails = 0;
                    var now = DateTimeOffset.UtcNow;
                    if (IsTrafficFlat(TrafficStatsHub.Shared.Latest))
                    {
                        if ((now - lastNatKeepaliveUtc).TotalMilliseconds >= NatKeepaliveIntervalMs)
                        {
                            lastNatKeepaliveUtc = now;
                            try
                            {
                                using var keepaliveCts =
                                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                                keepaliveCts.CancelAfter(NatKeepaliveProbeMs);
                                _ = await _latency
                                    .MeasureViaSocksAsync(
                                        XrayConfigBuilder.SocksPort,
                                        keepaliveCts.Token,
                                        NatKeepaliveProbeMs)
                                    .ConfigureAwait(false);
                            }
                            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                            {
                                // NAT keepalive is best-effort; path health decides drop.
                            }
                            catch
                            {
                                // ignore
                            }
                        }

                        if ((now - lastPathProbeUtc).TotalMilliseconds >= PathHealthIntervalMs)
                        {
                            lastPathProbeUtc = now;
                            var budget = GetPathHealthProbeMs(ActiveServer);
                            int? pathMs;
                            try
                            {
                                using var probeCts =
                                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                                probeCts.CancelAfter(budget);
                                pathMs = await _latency
                                    .MeasureViaSocksAsync(XrayConfigBuilder.SocksPort, probeCts.Token, budget)
                                    .ConfigureAwait(false);
                            }
                            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                            {
                                pathMs = -1;
                            }

                            if (pathMs is null or < 0)
                            {
                                consecutivePathFails++;
                                if (ShouldRaiseOnPathFails(consecutivePathFails))
                                {
                                    RaiseUnexpectedStop();
                                    return;
                                }
                            }
                            else
                            {
                                consecutivePathFails = 0;
                                PathHealthOk?.Invoke(this, EventArgs.Empty);
                            }
                        }
                    }

                    continue;
                }

                consecutiveSocksFails++;
                if (!ShouldRaiseOnSocksFails(consecutiveSocksFails))
                {
                    await Task.Delay(HealthSocksFailGapMs, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (ProcessHost.HasExited ||
                    !await IsPortOpenAsync("127.0.0.1", XrayConfigBuilder.SocksPort, cancellationToken)
                        .ConfigureAwait(false))
                {
                    RaiseUnexpectedStop();
                    return;
                }

                consecutiveSocksFails = 0;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // Keep monitoring.
            }
        }
    }

    private void OnUnexpectedExited(object? sender, EventArgs e)
    {
        // Startup failures are handled by StartAsync; avoid racing VPN teardown on Connect.
        if (ActiveServer is null)
            return;

        RaiseUnexpectedStop();
    }

    private void RaiseUnexpectedStop()
    {
        if (Interlocked.Exchange(ref _unexpectedHandled, 1) != 0)
            return;

        StopHealthMonitor();
        ActiveServer = null;
        // Do not fire RunningStateChanged(false) here — that flashed Idle/"Disconnected"
        // before teardown. UnexpectedStop handler owns UI after SafeTeardown.
        UnexpectedStop?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Transport-aware SOCKS bind budget for live Connect (uses full ConnectTimeoutMs cap).</summary>
    public static int GetConnectReadyWaitMs(ProxyServer server, bool useSingBox, int? tunFd)
    {
        var waitMs = LatencyService.GetCoreReadyWaitMs(server);
        if (useSingBox && tunFd is int fd && fd >= 0)
            waitMs += SingBoxTunReadyBonusMs;
        return Math.Min(ConnectTimeoutMs, waitMs);
    }

    private async Task WaitForCoreReadyAsync(
        ProxyServer server,
        bool useSingBox,
        int? tunFd,
        CancellationToken cancellationToken)
    {
        var waitMs = GetConnectReadyWaitMs(server, useSingBox, tunFd);
        var deadline = Environment.TickCount64 + waitMs;
        while (Environment.TickCount64 < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ProcessHost.HasExited)
            {
                await Task.Delay(150, CancellationToken.None).ConfigureAwait(false);
                var error = ProcessHost.GetRecentError();
                await StopAsync(cancellationToken).ConfigureAwait(false);
                throw new InvalidOperationException(CoreStartupErrorFormatter.Format(error));
            }

            if (await IsCorePortsReadyAsync(useSingBox, tunFd, cancellationToken).ConfigureAwait(false))
                return;

            await Task.Delay(CoreReadyPollMs, cancellationToken).ConfigureAwait(false);
        }

        if (await IsCorePortsReadyAsync(useSingBox, tunFd, cancellationToken).ConfigureAwait(false))
            return;

        var coreLabel = CoreRuntime.CoreLabel(server);
        var recent = ProcessHost.GetRecentError();
        if (ProcessHost.HasExited)
        {
            await StopAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(CoreStartupErrorFormatter.Format(recent));
        }

        await StopAsync(cancellationToken).ConfigureAwait(false);
        var detail = CoreStartupErrorFormatter.ExtractActionableLine(recent, 200);
        var message = string.IsNullOrWhiteSpace(detail)
            ? $"{coreLabel} core did not become ready in time."
            : $"{coreLabel} core did not become ready in time: {detail}";
        throw new TimeoutException(message);
    }

    private static async Task<bool> IsCorePortsReadyAsync(
        bool useSingBox,
        int? tunFd,
        CancellationToken cancellationToken)
    {
        if (!await IsPortOpenAsync("127.0.0.1", XrayConfigBuilder.SocksPort, cancellationToken)
                .ConfigureAwait(false))
            return false;

        if (RequiresAndroidTunHttpProbe(useSingBox, tunFd) &&
            !await IsPortOpenAsync("127.0.0.1", XrayConfigBuilder.HttpPort, cancellationToken)
                .ConfigureAwait(false))
            return false;

        return true;
    }

    private static async Task<bool> IsPortOpenAsync(string host, int port, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(HealthPortTimeoutMs);
            await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

}
