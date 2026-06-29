using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using v2rayF.Models;
using v2rayF.Services;

namespace v2rayF.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ServerStore _serverStore = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly SubscriptionService _subscriptionService = new();
    private readonly ProxyCoreService _proxyCore = new(AppServices.CoreEnvironment);
    private readonly LatencyService _latencyService = new(AppServices.CoreEnvironment);
    private AppSettings _settings = new();

    public bool IsMobile => AppServices.Platform?.IsMobile ?? false;

    public bool ShowDesktopProxySettings => !IsMobile;

    public IReadOnlyList<RoutingModeOption> RoutingModes { get; } =
    [
        new(RoutingMode.Global, "Global — proxy everything"),
        new(RoutingMode.BypassLan, "Bypass LAN — direct for private IPs"),
        new(RoutingMode.BypassChina, "Bypass China — direct for CN sites/IPs"),
        new(RoutingMode.CustomDirect, "Custom — direct list below")
    ];

    [ObservableProperty]
    private ObservableCollection<ProxyServer> _servers = [];

    [ObservableProperty]
    private ProxyServer? _selectedServer;

    [ObservableProperty]
    private RoutingModeOption? _selectedRoutingMode;

    [ObservableProperty]
    private string _customDirectRules = "";

    [ObservableProperty]
    private bool _enableTunMode;

    [ObservableProperty]
    private bool _enableSystemProxy = true;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _statusText = "Disconnected";

    [ObservableProperty]
    private string _subscriptionUrl = "";

    [ObservableProperty]
    private string _importText = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _coreStatus = "";

    [ObservableProperty]
    private string _tunStatus = "";

    public bool ShowCustomRules => SelectedRoutingMode?.Mode == RoutingMode.CustomDirect;

    public bool HasSelectedServer => SelectedServer is not null;

    public bool HasSavedSubscription => !string.IsNullOrWhiteSpace(SubscriptionUrl);

    partial void OnSelectedRoutingModeChanged(RoutingModeOption? value) => OnPropertyChanged(nameof(ShowCustomRules));

    partial void OnSelectedServerChanged(ProxyServer? value) => OnPropertyChanged(nameof(HasSelectedServer));

    partial void OnSubscriptionUrlChanged(string value) => OnPropertyChanged(nameof(HasSavedSubscription));

    public MainWindowViewModel()
    {
        _proxyCore.RunningStateChanged += (_, running) =>
        {
            RunOnUiThread(() =>
            {
                IsConnected = running;
                StatusText = running
                    ? $"Connected — {_proxyCore.ActiveServer?.Name ?? "server"}"
                    : "Disconnected";
                OnPropertyChanged(nameof(ConnectButtonText));
                OnPropertyChanged(nameof(TrayToolTip));
            });
        };

        if (IsMobile)
            AppServices.EmergencyDisconnectAsync = EmergencyDisconnectAsync;

        UpdateCoreStatus();
        _ = InitializeAsync();
    }

    public string ConnectButtonText => IsConnected ? "Disconnect" : "Connect";

    public string TrayToolTip => IsConnected
        ? $"v2rayF — Connected ({_proxyCore.ActiveServer?.Name})"
        : "v2rayF — Disconnected";

    private async Task InitializeAsync()
    {
        var settingsTask = _settingsStore.LoadAsync();
        var serversTask = _serverStore.LoadAsync();
        await Task.WhenAll(settingsTask, serversTask).ConfigureAwait(true);

        _settings = await settingsTask.ConfigureAwait(true);
        ApplySettingsToView(_settings);

        var servers = await serversTask.ConfigureAwait(true);
        Servers = new ObservableCollection<ProxyServer>(servers);
        SelectedServer ??= Servers.FirstOrDefault();

        try
        {
            await AppServices.CoreEnvironment.EnsureCoreAsync().ConfigureAwait(true);
        }
        catch
        {
            // Shown via UpdateCoreStatus on next line.
        }

        UpdateCoreStatus();
    }

    private void ApplySettingsToView(AppSettings settings)
    {
        SelectedRoutingMode = RoutingModes.FirstOrDefault(m => m.Mode == settings.RoutingMode) ?? RoutingModes[1];
        CustomDirectRules = settings.CustomDirectRules;
        EnableTunMode = IsMobile || settings.EnableTunMode;
        EnableSystemProxy = IsMobile ? false : settings.EnableSystemProxy;
        SubscriptionUrl = settings.SubscriptionUrl;
        UpdateTunStatus();
    }

    private AppSettings CollectSettings()
    {
        _settings.RoutingMode = SelectedRoutingMode?.Mode ?? RoutingMode.BypassLan;
        _settings.CustomDirectRules = CustomDirectRules;
        _settings.EnableTunMode = EnableTunMode;
        _settings.EnableSystemProxy = EnableSystemProxy;
        _settings.SubscriptionUrl = SubscriptionUrl.Trim();
        return _settings;
    }

    partial void OnEnableTunModeChanged(bool value)
    {
        UpdateTunStatus();
        if (value && EnableSystemProxy)
            EnableSystemProxy = false;
    }

    private void UpdateCoreStatus()
    {
        if (!_proxyCore.IsCoreAvailable())
        {
            CoreStatus = "Xray core missing — place xray in the cores folder";
            return;
        }

        var geo = _proxyCore.HasGeoFiles() ? "geo files OK" : "geo files missing (needed for Bypass China)";
        CoreStatus = $"Xray core ready · {geo}";
    }

    private void UpdateTunStatus()
    {
        if (!EnableTunMode)
        {
            TunStatus = "";
            return;
        }

        TunStatus = AppServices.Platform.CanUseTunMode
            ? IsMobile ? "VPN mode — routes all device traffic" : "TUN ready — full-device capture via virtual adapter"
            : AppServices.Platform.TunRequirementMessage;
    }

    private static string GetAndroidVpnFailureMessage()
    {
        var detail = AppServices.Platform?.LastEstablishError;
        if (!string.IsNullOrWhiteSpace(detail))
            return $"VPN setup failed: {detail}";

        return "VPN permission is required.";
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        await _settingsStore.SaveAsync(CollectSettings());
        StatusText = "Settings saved.";
    }

    [RelayCommand]
    private async Task LoadServersAsync()
    {
        var servers = await _serverStore.LoadAsync();
        Servers = new ObservableCollection<ProxyServer>(servers);
        SelectedServer ??= Servers.FirstOrDefault();
    }

    [RelayCommand]
    private async Task TestLatencyAsync()
    {
        if (SelectedServer is null)
        {
            StatusText = "Select a server to test latency.";
            return;
        }

        await MeasureLatencyAsync(SelectedServer);
    }

    [RelayCommand]
    private async Task TestAllLatencyAsync()
    {
        if (Servers.Count == 0)
        {
            StatusText = "No servers to test.";
            return;
        }

        IsBusy = true;
        StatusText = "Testing latency for all servers…";
        foreach (var server in Servers)
            await MeasureLatencyAsync(server);

        await _serverStore.SaveAsync(Servers);
        IsBusy = false;
        StatusText = "Latency test complete.";
    }

    private async Task MeasureLatencyAsync(ProxyServer server)
    {
        RunOnUiThread(() => server.SetLatency(null));

        int? result;
        if (_proxyCore.IsRunning && _proxyCore.ActiveServer?.Id == server.Id)
            result = await _latencyService.MeasureViaSocksAsync(XrayConfigBuilder.SocksPort);
        else
            result = await _latencyService.MeasureAsync(server);

        RunOnUiThread(() => server.SetLatency(result));
    }

    [RelayCommand]
    private async Task ImportFromClipboardAsync()
    {
        var clipboard = GetClipboard();
        if (clipboard is null)
        {
            StatusText = "Clipboard unavailable.";
            return;
        }

        var text = await clipboard.TryGetTextAsync();
        if (string.IsNullOrWhiteSpace(text))
        {
            StatusText = "Clipboard is empty.";
            return;
        }

        ImportText = text;
        await ImportParsedAsync(text);
    }

    [RelayCommand]
    private async Task ImportFromTextAsync()
    {
        if (string.IsNullOrWhiteSpace(ImportText))
        {
            StatusText = "Paste a share link or subscription payload first.";
            return;
        }

        await ImportParsedAsync(ImportText);
    }

    [RelayCommand]
    private async Task RefreshSubscriptionAsync()
    {
        if (string.IsNullOrWhiteSpace(SubscriptionUrl))
        {
            StatusText = "Enter a subscription URL first.";
            return;
        }

        if (await TryImportSubscriptionAsync())
            await _settingsStore.SaveAsync(CollectSettings());
    }

    [RelayCommand]
    private async Task ImportFromSubscriptionAsync()
    {
        await TryImportSubscriptionAsync();
    }

    [RelayCommand]
    private async Task ToggleConnectionAsync()
    {
        if (IsConnected)
        {
            await RunOnUiThreadAsync(DisconnectAsync).ConfigureAwait(true);
            return;
        }

        if (IsMobile)
        {
            await RunOnUiThreadAsync(() => ConnectMobileAsync()).ConfigureAwait(true);
            return;
        }

        await ConnectDesktopFlowAsync().ConfigureAwait(true);
    }

    private async Task ConnectMobileAsync()
    {
        if (SelectedServer is null)
        {
            RunOnUiThread(() => StatusText = "Select a server first.");
            return;
        }

        if (!_proxyCore.IsCoreAvailable() || !_proxyCore.HasGeoFiles())
        {
            await AppServices.CoreEnvironment.EnsureCoreAsync().ConfigureAwait(false);
            RunOnUiThread(UpdateCoreStatus);
        }

        if (!_proxyCore.IsCoreAvailable())
        {
            RunOnUiThread(() => StatusText = "Xray core not found.");
            return;
        }

        var settings = CollectSettings();
        settings.EnableTunMode = true;
        settings.EnableSystemProxy = false;
        if (settings.RoutingMode == RoutingMode.BypassChina && !_proxyCore.HasGeoFiles())
            settings.RoutingMode = RoutingMode.BypassLan;

        await _settingsStore.SaveAsync(settings).ConfigureAwait(false);

        var vpnEngaged = false;
        try
        {
            RunOnUiThread(() =>
            {
                IsBusy = true;
                StatusText = $"Connecting to {SelectedServer.Name}…";
            });

            await ConnectAndroidAsync(SelectedServer, settings, engaged => vpnEngaged = engaged)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await SafeTeardownAsync(vpnEngaged).ConfigureAwait(false);
            RunOnUiThread(() => StatusText = $"Connection failed: {ex.Message}");
        }
        finally
        {
            if (!IsConnected && vpnEngaged)
                await SafeTeardownAsync(vpnEngaged: true).ConfigureAwait(false);

            RunOnUiThread(() =>
            {
                IsBusy = false;
                OnPropertyChanged(nameof(ConnectButtonText));
                OnPropertyChanged(nameof(TrayToolTip));
            });
        }
    }

    private async Task ConnectDesktopFlowAsync()
    {
        if (SelectedServer is null)
        {
            StatusText = "Select a server first.";
            return;
        }

        if (!_proxyCore.IsCoreAvailable())
        {
            await AppServices.CoreEnvironment.EnsureCoreAsync().ConfigureAwait(true);
            UpdateCoreStatus();
        }

        if (!_proxyCore.IsCoreAvailable())
        {
            StatusText = "Xray core not found.";
            return;
        }

        var settings = CollectSettings();
        await _settingsStore.SaveAsync(settings).ConfigureAwait(true);

        try
        {
            IsBusy = true;
            StatusText = $"Connecting to {SelectedServer.Name}…";
            await ConnectDesktopAsync(SelectedServer, settings).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await SafeTeardownAsync(vpnEngaged: false).ConfigureAwait(true);
            StatusText = $"Connection failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(ConnectButtonText));
            OnPropertyChanged(nameof(TrayToolTip));
        }
    }

    private async Task ConnectDesktopAsync(ProxyServer server, AppSettings settings)
    {
        int? tunFd = null;
        if (settings.EnableTunMode)
            tunFd = await AppServices.Platform.EstablishVpnAsync().ConfigureAwait(true);

        await _proxyCore.StartAsync(server, settings, tunFd).ConfigureAwait(true);

        if (settings.EnableSystemProxy && !settings.EnableTunMode)
            await AppServices.Platform.EnableProxyAsync().ConfigureAwait(true);

        StatusText = settings.EnableTunMode
            ? $"Connected — {server.Name} (TUN mode)"
            : settings.EnableSystemProxy
                ? $"Connected — {server.Name} (proxy: {AppServices.Platform.LastProxyMethod})"
                : $"Connected — {server.Name} (manual proxy 127.0.0.1:10809)";

        IsConnected = true;
    }

    private async Task ConnectAndroidAsync(
        ProxyServer server,
        AppSettings settings,
        Action<bool> markVpnEngaged)
    {
        RunOnUiThread(() => StatusText = "Starting VPN…");
        var tunFd = await AppServices.Platform.EstablishVpnAsync().ConfigureAwait(false);
        if (tunFd is null)
        {
            RunOnUiThread(() => StatusText = GetAndroidVpnFailureMessage());
            return;
        }

        markVpnEngaged(true);

        RunOnUiThread(() => StatusText = $"Starting proxy for {server.Name}…");
        try
        {
            await _proxyCore.StartAsync(server, settings, tunFd).ConfigureAwait(false);
            await AppServices.Platform.EnableProxyAsync().ConfigureAwait(false);

            RunOnUiThread(() =>
            {
                StatusText = $"Connected — {server.Name} (VPN)";
                IsConnected = true;
            });
        }
        catch
        {
            await SafeTeardownAsync(vpnEngaged: true).ConfigureAwait(false);
            throw;
        }
    }

    private async Task SafeTeardownAsync(bool vpnEngaged)
    {
        try
        {
            await _proxyCore.StopAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best effort.
        }

        if (vpnEngaged || IsMobile)
        {
            try
            {
                await AppServices.Platform.DisableProxyAsync().ConfigureAwait(false);
            }
            catch
            {
                // Best effort.
            }
        }

        RunOnUiThread(() => IsConnected = false);
    }

    private Task EmergencyDisconnectAsync() => SafeTeardownAsync(vpnEngaged: true);

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        await SafeTeardownAsync(vpnEngaged: true).ConfigureAwait(true);
        StatusText = "Disconnected";
        OnPropertyChanged(nameof(ConnectButtonText));
        OnPropertyChanged(nameof(TrayToolTip));
    }

    [RelayCommand]
    private async Task RemoveSelectedAsync()
    {
        if (SelectedServer is null)
            return;

        if (IsConnected && _proxyCore.ActiveServer?.Id == SelectedServer.Id)
            await DisconnectAsync();

        Servers.Remove(SelectedServer);
        SelectedServer = Servers.FirstOrDefault();
        await _serverStore.SaveAsync(Servers);
        StatusText = "Server removed.";
    }

    [RelayCommand]
    private async Task ConnectToServerAsync(ProxyServer? server)
    {
        if (server is null)
            return;

        SelectedServer = server;

        if (IsConnected && _proxyCore.ActiveServer?.Id == server.Id)
            return;

        if (IsConnected)
            await DisconnectAsync();

        await ToggleConnectionAsync();
    }

    public async Task ShutdownAsync()
    {
        await DisconnectAsync();
        await _proxyCore.DisposeAsync();
    }

    private async Task ImportParsedAsync(string text)
    {
        if (Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https")
        {
            SubscriptionUrl = text.Trim();
            if (await TryImportSubscriptionAsync())
                ImportText = "";
            return;
        }

        var imported = ShareLinkParser.ParseBulk(text);
        if (imported.Count == 0)
        {
            StatusText = "No valid proxy links found.";
            return;
        }

        await MergeImportedAsync(imported);
        ImportText = "";
        StatusText = $"Imported {imported.Count} server(s).";
    }

    private async Task<bool> TryImportSubscriptionAsync()
    {
        if (string.IsNullOrWhiteSpace(SubscriptionUrl))
        {
            StatusText = "Enter a subscription URL.";
            return false;
        }

        try
        {
            IsBusy = true;
            StatusText = "Fetching subscription…";
            var imported = await _subscriptionService.FetchAsync(SubscriptionUrl);
            await MergeImportedAsync(imported);
            await _settingsStore.SaveAsync(CollectSettings());
            StatusText = $"Imported {imported.Count} server(s) from subscription.";
            return true;
        }
        catch (Exception ex)
        {
            StatusText = $"Subscription failed: {ex.Message}";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task MergeImportedAsync(IReadOnlyList<ProxyServer> imported)
    {
        foreach (var server in imported)
        {
            if (Servers.Any(existing =>
                    existing.RawLink == server.RawLink &&
                    existing.Address == server.Address &&
                    existing.Port == server.Port))
                continue;

            Servers.Add(server);
        }

        SelectedServer ??= Servers.FirstOrDefault();
        await _serverStore.SaveAsync(Servers);
    }

    private static IClipboard? GetClipboard()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.MainWindow is null)
                return null;

            return TopLevel.GetTopLevel(desktop.MainWindow)?.Clipboard;
        }

        if (Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime singleView &&
            singleView.MainView is Control view)
        {
            return TopLevel.GetTopLevel(view)?.Clipboard;
        }

        return null;
    }
}
