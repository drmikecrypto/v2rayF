using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using v2rayF.Models;

namespace v2rayF.Services;

public sealed class ProxyCoreService : IAsyncDisposable
{
    private readonly ICoreEnvironment _environment;
    private string? _configPath;

    private static ICoreProcessHost ProcessHost => AppServices.CoreProcessHost;

    public ProxyCoreService(ICoreEnvironment environment)
    {
        _environment = environment;
    }

    public bool IsRunning => ProcessHost.IsRunning;

    public ProxyServer? ActiveServer { get; private set; }

    public event EventHandler<bool>? RunningStateChanged;

    public string ResolveCorePath() => _environment.GetCorePath();

    public string ResolveCoresDirectory() => _environment.GetCoresDirectory();

    public bool IsCoreAvailable() => File.Exists(ResolveCorePath());

    public bool HasGeoFiles()
    {
        var cores = ResolveCoresDirectory();
        return File.Exists(Path.Combine(cores, "geoip.dat")) &&
               File.Exists(Path.Combine(cores, "geosite.dat"));
    }

    public async Task StartAsync(ProxyServer server, AppSettings settings, int? tunFd = null, CancellationToken cancellationToken = default)
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

        var configJson = XrayConfigBuilder.Build(server, settings, tunFd);
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

        await WaitForCoreReadyAsync(cancellationToken).ConfigureAwait(false);

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
        RunningStateChanged?.Invoke(this, true);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await ProcessHost.StopAsync(cancellationToken).ConfigureAwait(false);
        ActiveServer = null;
        RunningStateChanged?.Invoke(this, false);
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task WaitForCoreReadyAsync(CancellationToken cancellationToken)
    {
        for (var i = 0; i < 40; i++)
        {
            if (ProcessHost.HasExited)
                return;

            if (await IsPortOpenAsync("127.0.0.1", XrayConfigBuilder.SocksPort, cancellationToken).ConfigureAwait(false))
                return;

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        await Task.Delay(150, cancellationToken).ConfigureAwait(false);
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

        return lastLine;
    }
}
