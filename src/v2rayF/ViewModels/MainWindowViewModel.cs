using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
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
    private readonly SmartConnectService _smartConnect;
    private readonly AdaptiveSurviveService _adaptiveSurvive = new();
    private readonly ProfileVault _vault = new();
    private readonly UpdateCheckService _updateCheck = new();
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private AppSettings _settings = new();
    private UpdateOffer? _pendingUpdate;
    private CancellationTokenSource? _connectCts;
    private IReadOnlyList<SmartConnectService.RankedServer> _lastRanking = [];

    public bool IsMobile => AppServices.Platform?.IsMobile ?? false;

    public bool ShowDesktopProxySettings => !IsMobile;

    public IReadOnlyList<RoutingModeOption> RoutingModes { get; } =
    [
        new(RoutingMode.Global, "Global — proxy everything (Sentinel)"),
        new(RoutingMode.BypassLan, "Bypass LAN — direct for private IPs"),
        new(RoutingMode.BypassChina, "Bypass China — direct for CN sites/IPs"),
        new(RoutingMode.CustomDirect, "Custom — Direct / Proxy / Block lists")
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
    private string _customProxyRules = "";

    [ObservableProperty]
    private string _customBlockRules = "";

    [ObservableProperty]
    private bool _enableTunMode;

    [ObservableProperty]
    private bool _enableSystemProxy = true;

    [ObservableProperty]
    private bool _smartConnectEnabled;

    [ObservableProperty]
    private bool _smartMultipathEnabled;

    [ObservableProperty]
    private bool _killSwitchEnabled = true;

    [ObservableProperty]
    private bool _blockIpv6 = true;

    [ObservableProperty]
    private bool _dnsThroughProxy = true;

    [ObservableProperty]
    private bool _secureShareEnabled;

    [ObservableProperty]
    private bool _enablePacketFragment;

    [ObservableProperty]
    private bool _adaptiveSurviveEnabled = true;

    [ObservableProperty]
    private bool _subscriptionViaProxy = true;

    [ObservableProperty]
    private string _androidBypassPackages = "";

    [ObservableProperty]
    private string _secureShareEndpoint = "";

    [ObservableProperty]
    private bool _revealSharePassword;

    [ObservableProperty]
    private bool _shareListenAllInterfaces;

    [ObservableProperty]
    private bool _vaultUnlocked;

    [ObservableProperty]
    private string _vaultPassphrase = "";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private ConnectionState _connectionState = ConnectionState.Idle;

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

    [ObservableProperty]
    private bool _updateAvailable;

    [ObservableProperty]
    private string _updateLabel = "";

    [ObservableProperty]
    private bool _isUpdating;

    public string AppVersionLabel => AppVersion.Current;

    public string UpdateButtonText => string.IsNullOrWhiteSpace(UpdateLabel)
        ? "Update"
        : $"Update {UpdateLabel}";

    partial void OnUpdateLabelChanged(string value) => OnPropertyChanged(nameof(UpdateButtonText));

    public bool ShowCustomRules => SelectedRoutingMode?.Mode == RoutingMode.CustomDirect;

    public bool ShowAndroidBypass => IsMobile;

    public bool HasSelectedServer => SelectedServer is not null;

    public bool HasSavedSubscription => !string.IsNullOrWhiteSpace(SubscriptionUrl);

    public bool CanToggleConnection =>
        !IsBusy && ConnectionState is ConnectionState.Idle or ConnectionState.Connected or ConnectionState.Failed;

    public bool NeedsDisconnect =>
        IsConnected ||
        ConnectionState == ConnectionState.Connected ||
        AppServices.KillSwitch.IsArmed;

    partial void OnSelectedRoutingModeChanged(RoutingModeOption? value) => OnPropertyChanged(nameof(ShowCustomRules));

    partial void OnSelectedServerChanged(ProxyServer? value) => OnPropertyChanged(nameof(HasSelectedServer));

    partial void OnSubscriptionUrlChanged(string value) => OnPropertyChanged(nameof(HasSavedSubscription));

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanToggleConnection));

    partial void OnConnectionStateChanged(ConnectionState value) => OnPropertyChanged(nameof(CanToggleConnection));

    public MainWindowViewModel()
    {
        _smartConnect = new SmartConnectService(_latencyService);

        _proxyCore.RunningStateChanged += (_, running) =>
        {
            RunOnUiThread(() =>
            {
                // Ignore flicker during connect/disconnect orchestration.
                if (ConnectionState is ConnectionState.Connecting or ConnectionState.Disconnecting)
                {
                    if (running)
                        IsConnected = true;
                    return;
                }

                IsConnected = running;
                if (running)
                {
                    ConnectionState = ConnectionState.Connected;
                    StatusText = $"Connected — {StatusSanitizer.Scrub(_proxyCore.ActiveServer?.Name ?? "server")}";
                }
                else if (ConnectionState != ConnectionState.Failed)
                {
                    ConnectionState = ConnectionState.Idle;
                    StatusText = "Disconnected";
                }

                OnPropertyChanged(nameof(ConnectButtonText));
                OnPropertyChanged(nameof(TrayToolTip));
                UpdateSecureShareEndpoint();
            });
        };

        _proxyCore.UnexpectedStop += (_, _) =>
        {
            _ = HandleUnexpectedCoreStopAsync();
        };

        if (IsMobile)
            AppServices.EmergencyDisconnectAsync = EmergencyDisconnectAsync;

        AppServices.RefreshUpdateCheck = () => _ = CheckForUpdatesQuietlyAsync();
        AppServices.ReportStatus = msg => RunOnUiThread(() => StatusText = msg);

        UpdateCoreStatus();
        _ = InitializeAsync();
    }

    public string ConnectButtonText => NeedsDisconnect
        ? "Disconnect"
        : ConnectionState == ConnectionState.Connecting
            ? "Connecting…"
            : ConnectionState == ConnectionState.Disconnecting
                ? "Disconnecting…"
                : "Connect";

    public string TrayToolTip => NeedsDisconnect
        ? IsConnected
            ? $"v2rayF — Connected ({_proxyCore.ActiveServer?.Name})"
            : "v2rayF — Kill switch armed"
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
        _ = CheckForUpdatesQuietlyAsync();
    }

    private async Task CheckForUpdatesQuietlyAsync()
    {
        if (AppServices.Updater is null)
            return;

        try
        {
            var offer = await _updateCheck.CheckAsync(AppServices.Updater.ReleaseAssetFileName).ConfigureAwait(true);
            if (offer is null)
            {
                await SetOnUiAsync(() =>
                {
                    _pendingUpdate = null;
                    UpdateAvailable = false;
                    UpdateLabel = "";
                }).ConfigureAwait(true);
                return;
            }

            await SetOnUiAsync(() =>
            {
                _pendingUpdate = offer;
                UpdateAvailable = true;
                UpdateLabel = offer.Version;
                if (!IsBusy && !IsUpdating)
                    StatusText = $"v{offer.Version} is available — tap Update.";
            }).ConfigureAwait(true);
        }
        catch
        {
            // Offline or GitHub rate limit — ignore quietly.
        }
    }

    [RelayCommand]
    private async Task ApplyUpdateAsync()
    {
        if (_pendingUpdate is null || AppServices.Updater is null || IsUpdating)
            return;

        IsUpdating = true;
        IsBusy = true;
        try
        {
            if (IsConnected)
                await DisconnectAsync().ConfigureAwait(true);

            var progress = new Progress<string>(msg => RunOnUiThread(() => StatusText = msg));
            await AppServices.Updater.ApplyUpdateAsync(_pendingUpdate, progress).ConfigureAwait(true);
            await SetOnUiAsync(() =>
                StatusText = "Confirm the system Install prompt, then return here.").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await SetOnUiAsync(() =>
                StatusText = $"Update failed: {StatusSanitizer.Scrub(ex.Message)}").ConfigureAwait(true);
        }
        finally
        {
            await SetOnUiAsync(() =>
            {
                IsBusy = false;
                IsUpdating = false;
            }).ConfigureAwait(true);
        }
    }

    private void ApplySettingsToView(AppSettings settings)
    {
        SelectedRoutingMode = RoutingModes.FirstOrDefault(m => m.Mode == settings.RoutingMode) ?? RoutingModes[1];
        CustomDirectRules = settings.CustomDirectRules;
        CustomProxyRules = settings.CustomProxyRules;
        CustomBlockRules = settings.CustomBlockRules;
        EnableTunMode = IsMobile || settings.EnableTunMode;
        EnableSystemProxy = IsMobile ? false : settings.EnableSystemProxy;
        SubscriptionUrl = settings.SubscriptionUrl;
        SmartConnectEnabled = settings.SmartConnectEnabled;
        SmartMultipathEnabled = settings.SmartMultipathEnabled;
        KillSwitchEnabled = settings.KillSwitchEnabled;
        BlockIpv6 = settings.BlockIpv6;
        DnsThroughProxy = settings.DnsThroughProxy;
        SecureShareEnabled = settings.SecureShareEnabled;
        EnablePacketFragment = settings.EnablePacketFragment;
        AdaptiveSurviveEnabled = settings.AdaptiveSurviveEnabled;
        ShareListenAllInterfaces = settings.ShareListenAllInterfaces;
        SubscriptionViaProxy = settings.SubscriptionViaProxy;
        AndroidBypassPackages = settings.AndroidBypassPackages;
        VaultUnlocked = _vault.IsUnlocked;
        UpdateTunStatus();
        UpdateSecureShareEndpoint();
    }

    private AppSettings CollectSettings()
    {
        _settings.RoutingMode = SelectedRoutingMode?.Mode ?? RoutingMode.BypassLan;
        _settings.CustomDirectRules = CustomDirectRules;
        _settings.CustomProxyRules = CustomProxyRules;
        _settings.CustomBlockRules = CustomBlockRules;
        _settings.EnableTunMode = EnableTunMode;
        _settings.EnableSystemProxy = EnableSystemProxy;
        _settings.SubscriptionUrl = SubscriptionUrl.Trim();
        _settings.SmartConnectEnabled = SmartConnectEnabled;
        _settings.SmartMultipathEnabled = SmartMultipathEnabled;
        _settings.KillSwitchEnabled = KillSwitchEnabled;
        _settings.BlockIpv6 = BlockIpv6;
        _settings.DnsThroughProxy = DnsThroughProxy;
        _settings.SecureShareEnabled = SecureShareEnabled;
        _settings.ShareListenAllInterfaces = ShareListenAllInterfaces;
        _settings.EnablePacketFragment = EnablePacketFragment;
        _settings.AdaptiveSurviveEnabled = AdaptiveSurviveEnabled;
        _settings.SubscriptionViaProxy = SubscriptionViaProxy;
        _settings.AndroidBypassPackages = AndroidBypassPackages;
        return _settings;
    }

    [RelayCommand]
    private void ApplySentinelProfile()
    {
        SelectedRoutingMode = RoutingModes.First(m => m.Mode == RoutingMode.Global);
        KillSwitchEnabled = true;
        DnsThroughProxy = true;
        BlockIpv6 = true;
        if (!IsMobile)
            EnableTunMode = AppServices.Platform.CanUseTunMode;
        EnableSystemProxy = !EnableTunMode && !IsMobile;
        StatusText = "Sentinel profile applied — Save settings to persist.";
    }

    partial void OnEnableTunModeChanged(bool value)
    {
        UpdateTunStatus();
        if (value && EnableSystemProxy)
            EnableSystemProxy = false;
    }

    partial void OnSecureShareEnabledChanged(bool value)
    {
        if (value)
            XrayConfigBuilder.EnsureShareCredentials(_settings);
        UpdateSecureShareEndpoint();
    }

    partial void OnRevealSharePasswordChanged(bool value) => UpdateSecureShareEndpoint();

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

    private void UpdateSecureShareEndpoint()
    {
        if (!SecureShareEnabled || !IsConnected)
        {
            SecureShareEndpoint = SecureShareEnabled
                ? "Secure Share will show endpoint after connect."
                : "";
            return;
        }

        var lan = AppServices.Platform.GetLanIPv4Address() ?? "LAN-IP";
        var port = _settings.ShareBindPort > 0 ? _settings.ShareBindPort : XrayConfigBuilder.DefaultSharePort;
        XrayConfigBuilder.EnsureShareCredentials(_settings);
        var pass = RevealSharePassword && VaultUnlocked
            ? _settings.ShareAuthPass
            : "••••••••";
        var bindHint = ShareListenAllInterfaces ? "listen: all interfaces" : $"listen: {lan}";
        SecureShareEndpoint =
            $"socks5://{_settings.ShareAuthUser}:{pass}@{lan}:{port}\n" +
            $"http://{_settings.ShareAuthUser}:{pass}@{lan}:{port + 1}\n" +
            $"{bindHint} · Hotspot tip: OEM Wi‑Fi hotspot may bypass VPN — use these proxies.";
    }

    private static string GetAndroidVpnFailureMessage()
    {
        var detail = AppServices.Platform?.LastEstablishError;
        if (!string.IsNullOrWhiteSpace(detail))
            return $"VPN setup failed: {StatusSanitizer.Scrub(detail)}";

        return "VPN permission is required.";
    }

    private static IReadOnlyList<string> ParsePackageList(string raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        await _settingsStore.SaveAsync(CollectSettings());
        StatusText = "Settings saved.";
    }

    [RelayCommand]
    private async Task CopySecureShareAsync()
    {
        if (!SecureShareEnabled || !IsConnected)
        {
            StatusText = "Connect with Secure Share enabled first.";
            return;
        }

        if (!VaultUnlocked)
        {
            StatusText = "Unlock vault to copy Secure Share credentials.";
            return;
        }

        var clipboard = GetClipboard();
        if (clipboard is null)
        {
            StatusText = "Clipboard unavailable.";
            return;
        }

        var lan = AppServices.Platform.GetLanIPv4Address() ?? "LAN-IP";
        var port = _settings.ShareBindPort > 0 ? _settings.ShareBindPort : XrayConfigBuilder.DefaultSharePort;
        XrayConfigBuilder.EnsureShareCredentials(_settings);
        var line = $"socks5://{_settings.ShareAuthUser}:{_settings.ShareAuthPass}@{lan}:{port}";
        await clipboard.SetTextAsync(line);
        StatusText = "Secure Share SOCKS endpoint copied (once).";
    }

    [RelayCommand]
    private async Task RotateSharePasswordAsync()
    {
        XrayConfigBuilder.RotateSharePassword(_settings);
        RevealSharePassword = false;
        await _settingsStore.SaveAsync(CollectSettings());
        UpdateSecureShareEndpoint();
        StatusText = "Secure Share password rotated. Reconnect to apply.";
    }

    [RelayCommand]
    private void UnlockVault()
    {
        _vault.Unlock();
        VaultUnlocked = true;
        UpdateSecureShareEndpoint();
        StatusText = "Vault unlocked for this session.";
    }

    [RelayCommand]
    private void LockVault()
    {
        _vault.Lock();
        VaultUnlocked = false;
        RevealSharePassword = false;
        VaultPassphrase = "";
        UpdateSecureShareEndpoint();
        StatusText = "Vault locked.";
    }

    [RelayCommand]
    private async Task ExportVaultAsync()
    {
        if (!VaultUnlocked)
        {
            StatusText = "Unlock vault before exporting.";
            return;
        }

        if (string.IsNullOrWhiteSpace(VaultPassphrase) || VaultPassphrase.Length < 8)
        {
            StatusText = "Enter a vault passphrase (8+ chars) before export.";
            return;
        }

        var top = GetTopLevel();
        if (top?.StorageProvider is null)
        {
            StatusText = "File picker unavailable.";
            return;
        }

        try
        {
            var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export encrypted profiles",
                SuggestedFileName = "profiles.v2rayf",
                FileTypeChoices =
                [
                    new FilePickerFileType("v2rayF vault") { Patterns = ["*.v2rayf"] }
                ]
            }).ConfigureAwait(true);

            if (file is null)
                return;

            var bytes = _vault.Export(Servers.ToList(), CollectSettings(), VaultPassphrase);
            await using var stream = await file.OpenWriteAsync().ConfigureAwait(true);
            await stream.WriteAsync(bytes).ConfigureAwait(true);
            StatusText = "Encrypted vault exported.";
            VaultPassphrase = "";
        }
        catch (Exception ex)
        {
            StatusText = $"Vault export failed: {StatusSanitizer.Scrub(ex.Message)}";
        }
    }

    [RelayCommand]
    private async Task ImportVaultAsync()
    {
        if (string.IsNullOrWhiteSpace(VaultPassphrase) || VaultPassphrase.Length < 8)
        {
            StatusText = "Enter the vault passphrase (8+ chars) before import.";
            return;
        }

        var top = GetTopLevel();
        if (top?.StorageProvider is null)
        {
            StatusText = "File picker unavailable.";
            return;
        }

        try
        {
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import encrypted profiles",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("v2rayF vault") { Patterns = ["*.v2rayf"] }
                ]
            }).ConfigureAwait(true);

            if (files.Count == 0)
                return;

            await using var stream = await files[0].OpenReadAsync().ConfigureAwait(true);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms).ConfigureAwait(true);
            var payload = _vault.Import(ms.ToArray(), VaultPassphrase);
            await MergeImportedAsync(payload.Servers).ConfigureAwait(true);
            if (payload.Settings is not null)
            {
                // Merge routing / leak prefs without wiping share credentials on this device.
                _settings.RoutingMode = payload.Settings.RoutingMode;
                _settings.CustomDirectRules = payload.Settings.CustomDirectRules;
                _settings.CustomProxyRules = payload.Settings.CustomProxyRules;
                _settings.CustomBlockRules = payload.Settings.CustomBlockRules;
                _settings.SmartConnectEnabled = payload.Settings.SmartConnectEnabled;
                _settings.SmartMultipathEnabled = payload.Settings.SmartMultipathEnabled;
                _settings.KillSwitchEnabled = payload.Settings.KillSwitchEnabled;
                _settings.BlockIpv6 = payload.Settings.BlockIpv6;
                _settings.DnsThroughProxy = payload.Settings.DnsThroughProxy;
                _settings.EnablePacketFragment = payload.Settings.EnablePacketFragment;
                _settings.AdaptiveSurviveEnabled = payload.Settings.AdaptiveSurviveEnabled;
                if (!string.IsNullOrWhiteSpace(payload.Settings.SubscriptionUrl))
                    _settings.SubscriptionUrl = payload.Settings.SubscriptionUrl;
                ApplySettingsToView(_settings);
                await _settingsStore.SaveAsync(_settings).ConfigureAwait(true);
            }

            _vault.Unlock();
            VaultUnlocked = true;
            VaultPassphrase = "";
            StatusText = $"Imported {payload.Servers.Count} server(s) from vault.";
        }
        catch (Exception ex)
        {
            StatusText = $"Vault import failed: {StatusSanitizer.Scrub(ex.Message)}";
        }
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
        StatusText = "Testing TCP latency for all servers…";
        try
        {
            var snapshot = Servers.ToList();
            using var gate = new SemaphoreSlim(8, 8);
            await Task.WhenAll(snapshot.Select(async server =>
            {
                await gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    RunOnUiThread(() => server.SetLatency(null));
                    var ms = await _latencyService.MeasureTcpOnlyAsync(server).ConfigureAwait(false);
                    RunOnUiThread(() => server.SetLatency(ms));
                }
                finally
                {
                    gate.Release();
                }
            })).ConfigureAwait(true);

            await _serverStore.SaveAsync(Servers).ConfigureAwait(true);
            StatusText = "TCP latency test complete.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task MeasureLatencyAsync(ProxyServer server)
    {
        RunOnUiThread(() => server.SetLatency(null));

        // Single-server Test still prefers proxy-path when core is available.
        var result = await _latencyService.MeasureAsync(server);

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
        if (NeedsDisconnect)
        {
            await RunOnUiThreadAsync(DisconnectAsync).ConfigureAwait(true);
            return;
        }

        if (ConnectionState is ConnectionState.Connecting or ConnectionState.Disconnecting)
            return;

        if (IsMobile)
            await RunOnUiThreadAsync(ConnectWithOrchestrationAsync).ConfigureAwait(true);
        else
            await ConnectWithOrchestrationAsync().ConfigureAwait(true);
    }

    private async Task ConnectWithOrchestrationAsync()
    {
        if (!await _connectionGate.WaitAsync(0).ConfigureAwait(false))
            return;

        await ResumeOnUiAsync().ConfigureAwait(true);

        _connectCts?.Cancel();
        _connectCts?.Dispose();
        _connectCts = new CancellationTokenSource();
        var token = _connectCts.Token;

        await SetOnUiAsync(() =>
        {
            IsBusy = true;
            ConnectionState = ConnectionState.Connecting;
            OnPropertyChanged(nameof(ConnectButtonText));
        }).ConfigureAwait(true);

        try
        {
            if (!_proxyCore.IsCoreAvailable())
            {
                await AppServices.CoreEnvironment.EnsureCoreAsync(token).ConfigureAwait(false);
                await ResumeOnUiAsync().ConfigureAwait(true);
                await SetOnUiAsync(UpdateCoreStatus).ConfigureAwait(true);
            }

            if (!_proxyCore.IsCoreAvailable())
            {
                await SetOnUiAsync(() =>
                {
                    StatusText = "Xray core not found.";
                    ConnectionState = ConnectionState.Failed;
                }).ConfigureAwait(true);
                return;
            }

            await ResumeOnUiAsync().ConfigureAwait(true);
            var settings = CollectSettings();
            if (IsMobile)
            {
                settings.EnableTunMode = true;
                settings.EnableSystemProxy = false;
                if (settings.RoutingMode == RoutingMode.BypassChina && !_proxyCore.HasGeoFiles())
                    settings.RoutingMode = RoutingMode.BypassLan;
            }

            await _settingsStore.SaveAsync(settings).ConfigureAwait(false);
            await ResumeOnUiAsync().ConfigureAwait(true);

            IReadOnlyList<ProxyServer> candidates;
            if (settings.SmartConnectEnabled && Servers.Count > 0)
            {
                await SetOnUiAsync(() => StatusText = "Smart Connect — probing servers…").ConfigureAwait(true);
                var serversSnapshot = Servers.ToList();
                _lastRanking = await _smartConnect.RankAsync(serversSnapshot, token).ConfigureAwait(false);
                await ResumeOnUiAsync().ConfigureAwait(true);
                await SetOnUiAsync(() =>
                {
                    foreach (var ranked in _lastRanking)
                        ranked.Server.SetLatency(ranked.LatencyMs < 0 ? -1 : ranked.LatencyMs);
                }).ConfigureAwait(true);

                candidates = _smartConnect.SelectConnectOrder(
                    _lastRanking,
                    SelectedServer,
                    settings.LastGoodServerId);
            }
            else
            {
                if (SelectedServer is null)
                {
                    await SetOnUiAsync(() =>
                    {
                        StatusText = "Select a server first.";
                        ConnectionState = ConnectionState.Failed;
                    }).ConfigureAwait(true);
                    return;
                }

                candidates = [SelectedServer];
                if (settings.SmartMultipathEnabled && Servers.Count > 1)
                {
                    var serversSnapshot = Servers.ToList();
                    _lastRanking = await _smartConnect.RankAsync(serversSnapshot, token).ConfigureAwait(false);
                    await ResumeOnUiAsync().ConfigureAwait(true);
                }
            }

            Exception? lastError = null;
            var connectSettings = settings;
            var attempts = new List<(AppSettings Settings, string? Tactic, string? Reason)>
            {
                (settings, null, null)
            };
            if (settings.AdaptiveSurviveEnabled && settings.SmartConnectEnabled)
            {
                foreach (var survive in _adaptiveSurvive.BuildRetryAttempts(settings))
                    attempts.Add((survive.Settings, survive.Tactic, survive.StatusReason));
            }

            foreach (var (attemptSettings, tactic, reason) in attempts)
            {
                connectSettings = attemptSettings;
                var waveCandidates = tactic is null
                    ? candidates
                    : candidates.Take(AdaptiveSurviveService.MaxSurviveCandidates).ToList();

                if (!string.IsNullOrWhiteSpace(reason))
                {
                    await SetOnUiAsync(() => StatusText = reason!).ConfigureAwait(true);
                }

                foreach (var server in waveCandidates)
                {
                    token.ThrowIfCancellationRequested();
                    await SetOnUiAsync(() =>
                    {
                        SelectedServer = server;
                        StatusText = string.IsNullOrWhiteSpace(reason)
                            ? $"Connecting to {StatusSanitizer.Scrub(server.Name)}…"
                            : $"{reason} — {StatusSanitizer.Scrub(server.Name)}";
                    }).ConfigureAwait(true);

                    try
                    {
                        IReadOnlyList<ProxyServer>? multipath = null;
                        if (connectSettings.SmartMultipathEnabled && Servers.Count > 1)
                        {
                            if (_lastRanking.Count == 0)
                            {
                                var serversSnapshot = Servers.ToList();
                                _lastRanking = await _smartConnect.RankAsync(serversSnapshot, token).ConfigureAwait(false);
                                await ResumeOnUiAsync().ConfigureAwait(true);
                            }

                            multipath = _smartConnect.PickMultipathPeers(_lastRanking, server);
                        }

                        if (IsMobile)
                            await ConnectAndroidAsync(server, connectSettings, multipath, token).ConfigureAwait(false);
                        else
                            await ConnectDesktopAsync(server, connectSettings, multipath, token).ConfigureAwait(false);

                        await ResumeOnUiAsync().ConfigureAwait(true);
                        settings.LastGoodServerId = server.Id.ToString();
                        if (!string.IsNullOrWhiteSpace(tactic))
                            settings.LastSurviveTactic = tactic;
                        // Persist user prefs + last tactic hint only — not temporary fragment/sentinel overrides.
                        await _settingsStore.SaveAsync(settings).ConfigureAwait(false);
                        await _serverStore.SaveAsync(Servers).ConfigureAwait(false);
                        await SetOnUiAsync(() =>
                        {
                            ConnectionState = ConnectionState.Connected;
                            if (!string.IsNullOrWhiteSpace(reason))
                                StatusText = $"{StatusText} · {reason}";
                            UpdateSecureShareEndpoint();
                        }).ConfigureAwait(true);
                        return;
                    }
                    catch (OperationCanceledException)
                    {
                        await SafeTeardownAsync(releaseKillSwitch: true).ConfigureAwait(false);
                        await SetOnUiAsync(() =>
                        {
                            StatusText = "Connect cancelled.";
                            ConnectionState = ConnectionState.Idle;
                        }).ConfigureAwait(true);
                        return;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        // Keep kill switch armed across failover attempts (no clearnet window).
                        await SafeTeardownAsync(releaseKillSwitch: false).ConfigureAwait(false);
                        await ResumeOnUiAsync().ConfigureAwait(true);
                    }
                }
            }

            await SafeTeardownAsync(releaseKillSwitch: true).ConfigureAwait(false);
            await SetOnUiAsync(() =>
            {
                ConnectionState = ConnectionState.Failed;
                StatusText = $"Connection failed: {StatusSanitizer.Scrub(lastError?.Message ?? "no candidates")}";
            }).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            await SafeTeardownAsync(releaseKillSwitch: true).ConfigureAwait(false);
            await SetOnUiAsync(() =>
            {
                StatusText = "Connect cancelled.";
                ConnectionState = ConnectionState.Idle;
            }).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await SafeTeardownAsync(releaseKillSwitch: true).ConfigureAwait(false);
            await SetOnUiAsync(() =>
            {
                ConnectionState = ConnectionState.Failed;
                StatusText = $"Connection failed: {StatusSanitizer.Scrub(ex.Message)}";
            }).ConfigureAwait(true);
        }
        finally
        {
            await SetOnUiAsync(() =>
            {
                IsBusy = false;
                OnPropertyChanged(nameof(ConnectButtonText));
                OnPropertyChanged(nameof(TrayToolTip));
            }).ConfigureAwait(true);
            _connectionGate.Release();
        }
    }

    private async Task ConnectDesktopAsync(
        ProxyServer server,
        AppSettings settings,
        IReadOnlyList<ProxyServer>? multipath,
        CancellationToken cancellationToken)
    {
        int? tunFd = null;
        if (settings.EnableTunMode)
            tunFd = await AppServices.Platform.EstablishVpnAsync(
                bypassPackages: null,
                blockIpv6: settings.BlockIpv6,
                cancellationToken).ConfigureAwait(false);

        await ResumeOnUiAsync().ConfigureAwait(true);

        // Start core first; arm kill switch only after SOCKS is up (fail-closed without blocking dial).
        await _proxyCore.StartAsync(server, settings, tunFd, multipath, cancellationToken).ConfigureAwait(false);
        await ResumeOnUiAsync().ConfigureAwait(true);

        if (settings.KillSwitchEnabled)
        {
            await AppServices.KillSwitch.EnableAsync(_proxyCore.ResolveCorePath(), cancellationToken)
                .ConfigureAwait(false);
            await ResumeOnUiAsync().ConfigureAwait(true);
        }

        if (settings.EnableSystemProxy && !settings.EnableTunMode)
        {
            await AppServices.Platform.EnableProxyAsync(cancellationToken).ConfigureAwait(false);
            await ResumeOnUiAsync().ConfigureAwait(true);
        }

        var mode = settings.EnableTunMode
            ? "TUN"
            : settings.EnableSystemProxy
                ? $"proxy: {AppServices.Platform.LastProxyMethod}"
                : "manual 127.0.0.1:10809";

        var multi = multipath is { Count: > 1 } ? $" · multipath×{multipath.Count}" : "";
        var status = $"Connected — {StatusSanitizer.Scrub(server.Name)} ({mode}{multi})";
        if (settings.KillSwitchEnabled && !string.IsNullOrWhiteSpace(AppServices.KillSwitch.LastError) &&
            !AppServices.KillSwitch.IsArmed)
            status += " · kill switch unavailable (need admin)";

        await SetOnUiAsync(() =>
        {
            StatusText = status;
            IsConnected = true;
        }).ConfigureAwait(true);
    }

    private async Task ConnectAndroidAsync(
        ProxyServer server,
        AppSettings settings,
        IReadOnlyList<ProxyServer>? multipath,
        CancellationToken cancellationToken)
    {
        await SetOnUiAsync(() => StatusText = "Starting VPN…").ConfigureAwait(true);
        var bypass = ParsePackageList(settings.AndroidBypassPackages);
        var tunFd = await AppServices.Platform.EstablishVpnAsync(bypass, settings.BlockIpv6, cancellationToken)
            .ConfigureAwait(false);

        // AndroidUiThread resumes on the thread pool — hop back to Avalonia before any UI touch.
        await ResumeOnUiAsync().ConfigureAwait(true);

        if (tunFd is null)
        {
            var message = GetAndroidVpnFailureMessage();
            await SetOnUiAsync(() => StatusText = message).ConfigureAwait(true);
            throw new InvalidOperationException(message);
        }

        await SetOnUiAsync(() =>
            StatusText = $"Starting proxy for {StatusSanitizer.Scrub(server.Name)}…").ConfigureAwait(true);

        await _proxyCore.StartAsync(server, settings, tunFd, multipath, cancellationToken).ConfigureAwait(false);
        await ResumeOnUiAsync().ConfigureAwait(true);

        await AppServices.KillSwitch.EnableAsync(_proxyCore.ResolveCorePath(), cancellationToken).ConfigureAwait(false);
        await AppServices.Platform.EnableProxyAsync(cancellationToken).ConfigureAwait(false);
        await ResumeOnUiAsync().ConfigureAwait(true);

        var multi = multipath is { Count: > 1 } ? $" · multipath×{multipath.Count}" : "";
        await SetOnUiAsync(() =>
        {
            StatusText = $"Connected — {StatusSanitizer.Scrub(server.Name)} (VPN{multi})";
            IsConnected = true;
        }).ConfigureAwait(true);
    }

    private async Task HandleUnexpectedCoreStopAsync()
    {
        try
        {
            // Tear down proxy/VPN but KEEP kill switch armed (fail closed).
            await SafeTeardownAsync(releaseKillSwitch: false).ConfigureAwait(false);
        }
        finally
        {
            await SetOnUiAsync(() =>
            {
                ConnectionState = ConnectionState.Failed;
                StatusText = AppServices.KillSwitch.IsArmed
                    ? "Connection dropped — kill switch still blocking clearnet. Disconnect to restore."
                    : "Connection dropped — VPN torn down.";
                OnPropertyChanged(nameof(ConnectButtonText));
                OnPropertyChanged(nameof(TrayToolTip));
                UpdateSecureShareEndpoint();
            }).ConfigureAwait(true);
        }
    }

    private async Task SafeTeardownAsync(bool releaseKillSwitch)
    {
        try
        {
            await _proxyCore.StopAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best effort.
        }

        try
        {
            await AppServices.Platform.DisableProxyAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best effort.
        }

        if (releaseKillSwitch)
        {
            try
            {
                await AppServices.KillSwitch.DisableAsync().ConfigureAwait(false);
            }
            catch
            {
                // Best effort.
            }
        }

        await SetOnUiAsync(() =>
        {
            IsConnected = false;
            UpdateSecureShareEndpoint();
        }).ConfigureAwait(true);
    }

    private Task EmergencyDisconnectAsync() => SafeTeardownAsync(releaseKillSwitch: true);

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (!await _connectionGate.WaitAsync(0).ConfigureAwait(false))
        {
            // Cancel in-flight connect; that path releases kill switch on cancel.
            _connectCts?.Cancel();
            return;
        }

        try
        {
            await SetOnUiAsync(() =>
            {
                ConnectionState = ConnectionState.Disconnecting;
                IsBusy = true;
                OnPropertyChanged(nameof(ConnectButtonText));
            }).ConfigureAwait(true);
            _connectCts?.Cancel();

            await SafeTeardownAsync(releaseKillSwitch: true).ConfigureAwait(false);
            await SetOnUiAsync(() =>
            {
                ConnectionState = ConnectionState.Idle;
                StatusText = "Disconnected";
            }).ConfigureAwait(true);
        }
        finally
        {
            await SetOnUiAsync(() =>
            {
                IsBusy = false;
                OnPropertyChanged(nameof(ConnectButtonText));
                OnPropertyChanged(nameof(TrayToolTip));
            }).ConfigureAwait(true);
            _connectionGate.Release();
        }
    }

    [RelayCommand]
    private Task RemoveSelectedAsync() => RemoveServerAsync(SelectedServer);

    [RelayCommand]
    private async Task RemoveServerAsync(ProxyServer? server)
    {
        if (server is null)
        {
            StatusText = "Select a server to remove.";
            return;
        }

        var target = Servers.FirstOrDefault(s => s.Id == server.Id);
        if (target is null)
            return;

        if (IsConnected && _proxyCore.ActiveServer?.Id == target.Id)
            await DisconnectAsync().ConfigureAwait(true);

        Servers.Remove(target);
        if (SelectedServer?.Id == target.Id)
            SelectedServer = Servers.FirstOrDefault();

        await _serverStore.SaveAsync(Servers).ConfigureAwait(true);
        StatusText = Servers.Count == 0
            ? "Server removed. List is empty."
            : $"Removed \"{StatusSanitizer.Scrub(target.Name)}\".";
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
            var viaProxy = SubscriptionViaProxy && IsConnected;
            var imported = await _subscriptionService.FetchAsync(SubscriptionUrl, viaProxy);
            await MergeImportedAsync(imported);
            await _settingsStore.SaveAsync(CollectSettings());
            StatusText = viaProxy
                ? $"Imported {imported.Count} server(s) via proxy."
                : $"Imported {imported.Count} server(s) from subscription.";
            return true;
        }
        catch (Exception ex)
        {
            StatusText = $"Subscription failed: {StatusSanitizer.Scrub(ex.Message)}";
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
        return GetTopLevel()?.Clipboard;
    }

    private static TopLevel? GetTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.MainWindow is null)
                return null;

            return TopLevel.GetTopLevel(desktop.MainWindow);
        }

        if (Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime singleView &&
            singleView.MainView is Control view)
        {
            return TopLevel.GetTopLevel(view);
        }

        return null;
    }
}
