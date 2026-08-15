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
    public const int HealthCheckIntervalMs = 5000;
    public const int HealthPortTimeoutMs = 500;
    public const int HealthSocksFailThreshold = 3;
    public const int HealthSocksFailGapMs = 400;

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
        CoreRuntime.RequiresSingBox(server) ? _environment.GetSingBoxPath() : _environment.GetCorePath();

    public string ResolveCoresDirectory() => _environment.GetCoresDirectory();

    public bool IsCoreAvailable() => File.Exists(ResolveCorePath());

    public bool IsCoreAvailableFor(ProxyServer server) => File.Exists(ResolveCorePathFor(server));

    public bool HasGeoFiles()
    {
        var cores = ResolveCoresDirectory();
        return File.Exists(Path.Combine(cores, "geoip.dat")) &&
               File.Exists(Path.Combine(cores, "geosite.dat"));
    }

    /// <summary>True when consecutive SOCKS probe misses should declare the tunnel dead.</summary>
    public static bool ShouldRaiseOnSocksFails(int consecutiveFails) =>
        consecutiveFails >= HealthSocksFailThreshold;

    public async Task StartAsync(
        ProxyServer server,
        AppSettings settings,
        int? tunFd = null,
        IReadOnlyList<ProxyServer>? multipathServers = null,
        CancellationToken cancellationToken = default)
    {
        await _environment.EnsureCoreAsync(cancellationToken).ConfigureAwait(false);

        if (!IsCoreAvailableFor(server))
            throw new FileNotFoundException(
                CoreRuntime.RequiresSingBox(server)
                    ? "sing-box core not found. Place sing-box in the cores folder."
                    : "Xray core not found.",
                ResolveCorePathFor(server));

        if (settings.EnableTunMode && !AppServices.Platform.CanUseTunMode)
            throw new InvalidOperationException(AppServices.Platform.TunRequirementMessage);

        if (settings.RoutingMode == RoutingMode.BypassChina && !HasGeoFiles() &&
            !CoreRuntime.RequiresSingBox(server))
            throw new InvalidOperationException(
                "Bypass China routing requires geoip.dat and geosite.dat in the cores folder.");

        await StopAsync(cancellationToken).ConfigureAwait(false);
        Interlocked.Exchange(ref _unexpectedHandled, 0);

        if (settings.SecureShareEnabled)
            XrayConfigBuilder.EnsureShareCredentials(settings);

        var useSingBox = CoreRuntime.RequiresSingBox(server);
        if (useSingBox && multipathServers is { Count: > 0 })
            multipathServers = null; // sing-box multipath not in this build

        var configJson = useSingBox
            ? SingBoxConfigBuilder.Build(server, settings)
            : XrayConfigBuilder.Build(server, settings, tunFd, multipathServers);
        var configDir = Path.Combine(_environment.GetDataDirectory(), "runtime");
        Directory.CreateDirectory(configDir);
        _configPath = Path.Combine(configDir, useSingBox ? "singbox-config.json" : "config.json");
        await File.WriteAllTextAsync(_configPath, configJson, cancellationToken).ConfigureAwait(false);

        await ProcessHost.StartAsync(
            ResolveCorePathFor(server),
            _configPath,
            ResolveCoresDirectory(),
            useSingBox ? null : tunFd,
            cancellationToken).ConfigureAwait(false);

        using var readyTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readyTimeout.CancelAfter(ConnectTimeoutMs);

        await WaitForCoreReadyAsync(readyTimeout.Token).ConfigureAwait(false);

        if (ProcessHost.HasExited)
        {
            // Let async stdout/stderr drainers finish before reading the error buffer.
            await Task.Delay(150, CancellationToken.None).ConfigureAwait(false);
            var error = ProcessHost.GetRecentError();
            await StopAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(CoreStartupErrorFormatter.Format(error));
        }

        // Gate Connected on a real proxy-path probe — SOCKS listen alone is not enough.
        LastConnectProbeMs = null;
        using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var healthBudget = LatencyService.GetConnectHealthProbeMs(server);
        probeCts.CancelAfter(healthBudget);
        int? probeMs;
        try
        {
            probeMs = await _latency
                .MeasureConnectHealthViaSocksAsync(XrayConfigBuilder.SocksPort, probeCts.Token, healthBudget)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            probeMs = -1;
        }

        if (probeMs is null or < 0)
        {
            await StopAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(
                "Proxy path failed after connect (HTTPS probe timed out). The tunnel is not usable.");
        }

        LastConnectProbeMs = probeMs;

        ActiveServer = server;
        StartHealthMonitor();
        RunningStateChanged?.Invoke(this, true);
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

    private async Task WaitForCoreReadyAsync(CancellationToken cancellationToken)
    {
        for (var i = 0; i < 80; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ProcessHost.HasExited)
                return;

            if (await IsPortOpenAsync("127.0.0.1", XrayConfigBuilder.SocksPort, cancellationToken).ConfigureAwait(false))
                return;

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        if (!await IsPortOpenAsync("127.0.0.1", XrayConfigBuilder.SocksPort, cancellationToken).ConfigureAwait(false))
            throw new TimeoutException("Xray core did not become ready in time.");
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
