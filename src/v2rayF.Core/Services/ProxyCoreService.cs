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
    public const int PathHealthIntervalMs = 45000;
    /// <summary>Path probe cadence when traffic is active (non-flat).</summary>
    public const int ActivePathHealthIntervalMs = 90000;
    public const int PathHealthFailThreshold = 3;
    public const int PathHealthProbeMs = 8000;
    public const int PathHealthProbeVisionMs = 12000;
    /// <summary>Resume/wake live-path verify budget (non-Vision).</summary>
    public const int ResumePathProbeMs = 4000;
    /// <summary>Resume/wake live-path verify budget (Vision / REALITY).</summary>
    public const int ResumePathProbeVisionMs = 6000;
    public const int NatKeepaliveIntervalMs = 25000;
    public const int NatKeepaliveProbeMs = 2000;
    /// <summary>Consecutive tun-only probe failures before soft recovery fires.</summary>
    public const int TunOnlyFailThreshold = 2;

    private readonly ICoreEnvironment _environment;
    private readonly LatencyService _latency;
    private string? _configPath;
    private CancellationTokenSource? _healthCts;
    private int _unexpectedHandled;
    private int _softRecoveryInFlight;
    private bool _activeUseSingBox;
    private bool _activeEnableTunMode;
    private int? _activeTunFd;
    private volatile int _consecutivePathFails;
    private volatile int _consecutiveSocksFails;
    private volatile int _consecutiveTunOnlyFails;
    private DateTimeOffset _lastPathProbeUtc = DateTimeOffset.UtcNow;
    private DateTimeOffset _lastNatKeepaliveUtc = DateTimeOffset.UtcNow;
    private DateTimeOffset _lastVpnKeepaliveUtc = DateTimeOffset.UtcNow;

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

    public int? ActiveTunFd => _activeTunFd;

    public bool ActiveEnableTunMode => _activeEnableTunMode;

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

    /// <summary>Raised when TUN app-path probe fails (trigger soft recovery before hard stop).</summary>
    public event EventHandler? TunPathFailed;

    /// <summary>True while soft recovery is in flight (pause path-fail escalation).</summary>
    public bool IsSoftRecoveryInFlight => Volatile.Read(ref _softRecoveryInFlight) != 0;

    /// <summary>Begin soft recovery — path health will not escalate to UnexpectedStop until EndSoftRecovery.</summary>
    public void BeginSoftRecovery() => Interlocked.Exchange(ref _softRecoveryInFlight, 1);

    /// <summary>End soft recovery; on success reset fail counters.</summary>
    public void EndSoftRecovery(bool success)
    {
        Interlocked.Exchange(ref _softRecoveryInFlight, 0);
        if (success)
            ResetPathHealthState();
    }

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

    public static int GetResumePathProbeMs(ProxyServer? server)
    {
        if (server is not null &&
            (ShareLinkParser.IsVisionFlow(server) ||
             string.Equals(server.Security, "reality", StringComparison.OrdinalIgnoreCase)))
            return ResumePathProbeVisionMs;
        return ResumePathProbeMs;
    }

    /// <summary>Clear health fail counters after a successful live-path verify.</summary>
    public void ResetPathHealthState()
    {
        _consecutivePathFails = 0;
        _consecutiveSocksFails = 0;
        _consecutiveTunOnlyFails = 0;
        PathHealthOk?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Lightweight post-connect path check for wake/resume (SOCKS+HTTP on Android TUN).</summary>
    public async Task<bool> VerifyLivePathAsync(CancellationToken cancellationToken = default)
    {
        if (ActiveServer is null || !IsRunning)
            return false;

        var budget = GetResumePathProbeMs(ActiveServer);
        var pathMs = await ProbeConnectPathAsync(
                ActiveServer,
                _activeUseSingBox,
                _activeTunFd,
                budget,
                enableTunMode: null,
                cancellationToken)
            .ConfigureAwait(false);

        if (pathMs is >= 0)
        {
            ResetPathHealthState();
            return true;
        }

        return false;
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
        var probeMs = await ProbeConnectPathAsync(
                server, useSingBox, tunFd, healthBudget: null, enableTunMode: settings.EnableTunMode, cancellationToken)
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

            probeMs = await ProbeConnectPathAsync(
                    server, useSingBox, tunFd, healthBudget: null, enableTunMode: dohSettings.EnableTunMode, cancellationToken)
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
        _activeUseSingBox = useSingBox;
        _activeEnableTunMode = settings.EnableTunMode;
        _activeTunFd = tunFd;
        _consecutivePathFails = 0;
        _consecutiveSocksFails = 0;
        _consecutiveTunOnlyFails = 0;
        _lastPathProbeUtc = DateTimeOffset.UtcNow;
        _lastNatKeepaliveUtc = DateTimeOffset.UtcNow;
        _lastVpnKeepaliveUtc = DateTimeOffset.UtcNow;
        StartHealthMonitor();
        RunningStateChanged?.Invoke(this, true);
    }

    private readonly record struct PathProbeResult(int? LocalhostMs, int? TunMs, bool TunRequired)
    {
        public bool LocalhostOk => LocalhostMs is >= 0;
        public bool TunOk => !TunRequired || TunMs is >= 0;

        public int? CombinedMs
        {
            get
            {
                if (!LocalhostOk)
                    return LocalhostMs;
                if (!TunOk)
                    return TunMs;
                if (TunMs is int tun && LocalhostMs is int local)
                    return Math.Max(local, tun);
                return LocalhostMs;
            }
        }
    }

    private async Task<int?> ProbeConnectPathAsync(
        ProxyServer server,
        bool useSingBox,
        int? tunFd,
        int? healthBudget,
        bool? enableTunMode,
        CancellationToken cancellationToken)
    {
        var result = await ProbePathComponentsAsync(
                server, useSingBox, tunFd, healthBudget, enableTunMode, cancellationToken)
            .ConfigureAwait(false);
        return result.CombinedMs;
    }

    private async Task<PathProbeResult> ProbePathComponentsAsync(
        ProxyServer server,
        bool useSingBox,
        int? tunFd,
        int? healthBudget,
        bool? enableTunMode,
        CancellationToken cancellationToken)
    {
        var budget = healthBudget ?? LatencyService.GetConnectHealthProbeMs(server);
        using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        probeCts.CancelAfter(budget);

        var tunRequired = RequiresTunAppPath(enableTunMode);
        var tunProbeBudget = Math.Min(budget, LatencyService.TunAppPathProbeMs);
        var tunTask = tunRequired
            ? AppServices.Platform.ProbeTunAppPathAsync(probeCts.Token, tunProbeBudget)
            : Task.FromResult<int?>(null);

        try
        {
            if (!RequiresAndroidTunHttpProbe(useSingBox, tunFd))
            {
                var socksMs = await _latency
                    .MeasureConnectHealthViaSocksAsync(XrayConfigBuilder.SocksPort, probeCts.Token, budget)
                    .ConfigureAwait(false);
                var tunMs = await tunTask.ConfigureAwait(false);
                return new PathProbeResult(socksMs, tunMs, tunRequired);
            }

            var socksTask = _latency.MeasureConnectHealthViaSocksAsync(
                XrayConfigBuilder.SocksPort, probeCts.Token, budget);
            var httpTask = _latency.MeasureConnectHealthViaHttpAsync(
                XrayConfigBuilder.HttpPort, probeCts.Token, budget, warmThenMeasure: false);
            await Task.WhenAll(socksTask, httpTask, tunTask).ConfigureAwait(false);

            var socksMs2 = await socksTask.ConfigureAwait(false);
            if (socksMs2 is null or < 0)
                return new PathProbeResult(socksMs2, await tunTask.ConfigureAwait(false), tunRequired);

            var httpMs = await httpTask.ConfigureAwait(false);
            if (httpMs is null or < 0)
                return new PathProbeResult(httpMs, await tunTask.ConfigureAwait(false), tunRequired);

            var tunAppMs = await tunTask.ConfigureAwait(false);
            var localhostMs = Math.Max(socksMs2.Value, httpMs.Value);
            return new PathProbeResult(localhostMs, tunAppMs, tunRequired);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new PathProbeResult(-1, -1, tunRequired);
        }
    }

    private bool RequiresTunAppPath(bool? enableTunMode = null) =>
        enableTunMode ?? _activeEnableTunMode;

    private static bool RequiresAndroidTunHttpProbe(bool useSingBox, int? tunFd) =>
        useSingBox && tunFd is int fd && fd >= 0;

    /// <summary>Restart sing-box/Xray without tearing down VPN interface (clears FakeIP).</summary>
    public async Task RefreshRuntimeAsync(
        ProxyServer server,
        AppSettings settings,
        int? tunFd,
        IReadOnlyList<ProxyServer>? multipathServers = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);

        Interlocked.Exchange(ref _unexpectedHandled, 1);
        StopHealthMonitor();
        await ProcessHost.StopAsync(cancellationToken).ConfigureAwait(false);

        var useSingBox = CoreRuntime.UseSingBox(server);
        if (useSingBox && multipathServers is { Count: > 0 })
            multipathServers = null;

        if (settings.SecureShareEnabled)
            XrayConfigBuilder.EnsureShareCredentials(settings);

        var configJson = useSingBox
            ? SingBoxConfigBuilder.Build(server, settings, tunFd: tunFd)
            : XrayConfigBuilder.Build(server, settings, tunFd, multipathServers);
        var configDir = Path.Combine(_environment.GetDataDirectory(), "runtime");
        Directory.CreateDirectory(configDir);
        _configPath ??= Path.Combine(configDir, useSingBox ? "singbox-config.json" : "config.json");
        await File.WriteAllTextAsync(_configPath, configJson, cancellationToken).ConfigureAwait(false);

        await ProcessHost.StartAsync(
            ResolveCorePathFor(server),
            _configPath,
            ResolveCoresDirectory(),
            tunFd,
            cancellationToken).ConfigureAwait(false);

        using var readyTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readyTimeout.CancelAfter(ConnectTimeoutMs);
        await WaitForCoreReadyAsync(server, useSingBox, tunFd, readyTimeout.Token).ConfigureAwait(false);

        var probeMs = await ProbeConnectPathAsync(
                server, useSingBox, tunFd, healthBudget: null, enableTunMode: settings.EnableTunMode, cancellationToken)
            .ConfigureAwait(false);
        if (probeMs is null or < 0 && !settings.DnsThroughProxy)
        {
            var dohSettings = CloneSettingsWithDoH(settings);
            await ProcessHost.StopAsync(cancellationToken).ConfigureAwait(false);

            configJson = useSingBox
                ? SingBoxConfigBuilder.Build(server, dohSettings, tunFd: tunFd)
                : XrayConfigBuilder.Build(server, dohSettings, tunFd, multipathServers);
            await File.WriteAllTextAsync(_configPath, configJson, cancellationToken).ConfigureAwait(false);

            await ProcessHost.StartAsync(
                ResolveCorePathFor(server),
                _configPath,
                ResolveCoresDirectory(),
                tunFd,
                cancellationToken).ConfigureAwait(false);

            using var readyTimeout2 = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readyTimeout2.CancelAfter(ConnectTimeoutMs);
            await WaitForCoreReadyAsync(server, useSingBox, tunFd, readyTimeout2.Token).ConfigureAwait(false);

            probeMs = await ProbeConnectPathAsync(
                    server, useSingBox, tunFd, healthBudget: null, enableTunMode: dohSettings.EnableTunMode, cancellationToken)
                .ConfigureAwait(false);
        }

        if (probeMs is null or < 0)
        {
            await ProcessHost.StopAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("Proxy path failed after runtime refresh.");
        }

        Interlocked.Exchange(ref _unexpectedHandled, 0);
        ActiveServer = server;
        _activeUseSingBox = useSingBox;
        _activeEnableTunMode = settings.EnableTunMode;
        _activeTunFd = tunFd;
        _consecutivePathFails = 0;
        _consecutiveSocksFails = 0;
        _consecutiveTunOnlyFails = 0;
        _lastPathProbeUtc = DateTimeOffset.UtcNow;
        _lastNatKeepaliveUtc = DateTimeOffset.UtcNow;
        _lastVpnKeepaliveUtc = DateTimeOffset.UtcNow;
        StartHealthMonitor();
        RunningStateChanged?.Invoke(this, true);
    }

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
            AutoReconnectEnabled = settings.AutoReconnectEnabled,
            BatteryOptimizationPromptShown = settings.BatteryOptimizationPromptShown,
            LastBatteryPromptUtc = settings.LastBatteryPromptUtc
        };
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        // Prevent unexpected-exit handlers from racing a deliberate stop.
        Interlocked.Exchange(ref _unexpectedHandled, 1);
        StopHealthMonitor();
        LastConnectProbeMs = null;
        Interlocked.Exchange(ref _softRecoveryInFlight, 0);
        await ProcessHost.StopAsync(cancellationToken).ConfigureAwait(false);
        ActiveServer = null;
        _activeTunFd = null;
        _activeEnableTunMode = false;
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
                    _consecutiveSocksFails = 0;
                    var now = DateTimeOffset.UtcNow;
                    var flat = IsTrafficFlat(TrafficStatsHub.Shared.Latest);

                    if (flat && (now - _lastNatKeepaliveUtc).TotalMilliseconds >= NatKeepaliveIntervalMs)
                    {
                        _lastNatKeepaliveUtc = now;
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

                    var pathInterval = flat ? PathHealthIntervalMs : ActivePathHealthIntervalMs;
                    if (ActiveServer is not null &&
                        (now - _lastPathProbeUtc).TotalMilliseconds >= pathInterval)
                    {
                        _lastPathProbeUtc = now;
                        PathProbeResult probeResult;
                        try
                        {
                            probeResult = await ProbePathComponentsAsync(
                                    ActiveServer!,
                                    _activeUseSingBox,
                                    _activeTunFd,
                                    healthBudget: GetPathHealthProbeMs(ActiveServer),
                                    enableTunMode: null,
                                    cancellationToken)
                                .ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                        {
                            probeResult = new PathProbeResult(-1, -1, RequiresTunAppPath());
                        }

                        if (probeResult.CombinedMs is null or < 0)
                        {
                            if (IsSoftRecoveryInFlight)
                                continue;

                            if (probeResult.LocalhostOk && !probeResult.TunOk)
                            {
                                _consecutiveTunOnlyFails++;
                                if (_consecutiveTunOnlyFails >= TunOnlyFailThreshold)
                                {
                                    BeginSoftRecovery();
                                    TunPathFailed?.Invoke(this, EventArgs.Empty);
                                }
                            }
                            else
                                _consecutiveTunOnlyFails = 0;

                            _consecutivePathFails++;
                            if (ShouldRaiseOnPathFails(_consecutivePathFails))
                            {
                                RaiseUnexpectedStop();
                                return;
                            }
                        }
                        else
                        {
                            _consecutivePathFails = 0;
                            _consecutiveTunOnlyFails = 0;
                            PathHealthOk?.Invoke(this, EventArgs.Empty);
                            if ((now - _lastVpnKeepaliveUtc).TotalMilliseconds >= ActivePathHealthIntervalMs)
                            {
                                _lastVpnKeepaliveUtc = now;
                                try
                                {
                                    AppServices.OnVpnKeepalive?.Invoke();
                                }
                                catch
                                {
                                    // Best-effort captive-portal / VPN validation.
                                }
                            }
                        }
                    }

                    continue;
                }

                _consecutiveSocksFails++;
                if (!ShouldRaiseOnSocksFails(_consecutiveSocksFails))
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

                _consecutiveSocksFails = 0;
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
