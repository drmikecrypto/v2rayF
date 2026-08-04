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
    public const int HealthCheckIntervalMs = 4000;

    private readonly ICoreEnvironment _environment;
    private string? _configPath;
    private CancellationTokenSource? _healthCts;
    private int _unexpectedHandled;

    private static ICoreProcessHost ProcessHost => AppServices.CoreProcessHost;

    public ProxyCoreService(ICoreEnvironment environment)
    {
        _environment = environment;
        ProcessHost.UnexpectedExited += OnUnexpectedExited;
    }

    public bool IsRunning => ProcessHost.IsRunning;

    public ProxyServer? ActiveServer { get; private set; }

    public event EventHandler<bool>? RunningStateChanged;

    /// <summary>Raised when the core dies unexpectedly while we thought it was connected.</summary>
    public event EventHandler? UnexpectedStop;

    public string ResolveCorePath() => _environment.GetCorePath();

    public string ResolveCoresDirectory() => _environment.GetCoresDirectory();

    public bool IsCoreAvailable() => File.Exists(ResolveCorePath());

    public bool HasGeoFiles()
    {
        var cores = ResolveCoresDirectory();
        return File.Exists(Path.Combine(cores, "geoip.dat")) &&
               File.Exists(Path.Combine(cores, "geosite.dat"));
    }

    public async Task StartAsync(
        ProxyServer server,
        AppSettings settings,
        int? tunFd = null,
        IReadOnlyList<ProxyServer>? multipathServers = null,
        CancellationToken cancellationToken = default)
    {
        await _environment.EnsureCoreAsync(cancellationToken).ConfigureAwait(false);

        if (!IsCoreAvailable())
            throw new FileNotFoundException(
                "Xray core not found.",
                ResolveCorePath());

        if (settings.EnableTunMode && !AppServices.Platform.CanUseTunMode)
            throw new InvalidOperationException(AppServices.Platform.TunRequirementMessage);

        if (settings.RoutingMode == RoutingMode.BypassChina && !HasGeoFiles())
            throw new InvalidOperationException(
                "Bypass China routing requires geoip.dat and geosite.dat in the cores folder.");

        await StopAsync(cancellationToken).ConfigureAwait(false);
        Interlocked.Exchange(ref _unexpectedHandled, 0);

        if (settings.SecureShareEnabled)
            XrayConfigBuilder.EnsureShareCredentials(settings);

        var configJson = XrayConfigBuilder.Build(server, settings, tunFd, multipathServers);
        var configDir = Path.Combine(_environment.GetDataDirectory(), "runtime");
        Directory.CreateDirectory(configDir);
        _configPath = Path.Combine(configDir, "config.json");
        await File.WriteAllTextAsync(_configPath, configJson, cancellationToken).ConfigureAwait(false);

        await ProcessHost.StartAsync(
            ResolveCorePath(),
            _configPath,
            ResolveCoresDirectory(),
            tunFd,
            cancellationToken).ConfigureAwait(false);

        using var readyTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readyTimeout.CancelAfter(ConnectTimeoutMs);

        await WaitForCoreReadyAsync(readyTimeout.Token).ConfigureAwait(false);

        if (ProcessHost.HasExited)
        {
            var error = ProcessHost.GetRecentError();
            await StopAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error)
                    ? "Xray core exited immediately after start."
                    : FormatStartupError(error));
        }

        ActiveServer = server;
        StartHealthMonitor();
        RunningStateChanged?.Invoke(this, true);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        // Prevent unexpected-exit handlers from racing a deliberate stop.
        Interlocked.Exchange(ref _unexpectedHandled, 1);
        StopHealthMonitor();
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

                if (!await IsPortOpenAsync("127.0.0.1", XrayConfigBuilder.SocksPort, cancellationToken)
                        .ConfigureAwait(false))
                {
                    // Brief grace: port may flap during reconnect.
                    await Task.Delay(750, cancellationToken).ConfigureAwait(false);
                    if (ProcessHost.HasExited ||
                        !await IsPortOpenAsync("127.0.0.1", XrayConfigBuilder.SocksPort, cancellationToken)
                            .ConfigureAwait(false))
                    {
                        RaiseUnexpectedStop();
                        return;
                    }
                }
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

    private void OnUnexpectedExited(object? sender, EventArgs e) => RaiseUnexpectedStop();

    private void RaiseUnexpectedStop()
    {
        if (Interlocked.Exchange(ref _unexpectedHandled, 1) != 0)
            return;

        StopHealthMonitor();
        ActiveServer = null;
        RunningStateChanged?.Invoke(this, false);
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
            timeout.CancelAfter(200);
            await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string FormatStartupError(string stderr)
    {
        if (stderr.Contains("10808", StringComparison.Ordinal) ||
            stderr.Contains("10809", StringComparison.Ordinal) ||
            stderr.Contains("bind:", StringComparison.OrdinalIgnoreCase) ||
            stderr.Contains("Only one usage of each socket address", StringComparison.OrdinalIgnoreCase))
        {
            return "Local proxy ports 10808/10809 are already in use. Close v2rayN (or another proxy client) and try again.";
        }

        if (string.IsNullOrWhiteSpace(stderr))
            return "Xray core exited immediately after start.";

        var lastLine = stderr;
        var newline = stderr.LastIndexOf('\n');
        if (newline >= 0 && newline < stderr.Length - 1)
            lastLine = stderr[(newline + 1)..].Trim();

        return StatusSanitizer.Scrub(lastLine);
    }
}
