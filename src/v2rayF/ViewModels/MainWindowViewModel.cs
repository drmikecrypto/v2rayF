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
    private readonly ServerRankingCoordinator _startupRank;
    private readonly AdaptiveSurviveService _adaptiveSurvive = new();
    private readonly ProfileVault _vault = new();
    private readonly UpdateCheckService _updateCheck = new();
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private AppSettings _settings = new();
    private UpdateOffer? _pendingUpdate;
    private CancellationTokenSource? _connectCts;
    private IReadOnlyList<SmartConnectService.RankedServer> _lastRanking = [];
    private DateTimeOffset _lastUpdateCheckUtc = DateTimeOffset.MinValue;
    private bool _suppressSelectionPersist;
    private bool _suppressSelectionSwitch;
    private bool _selectionSwitchInFlight;

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
    private bool _smartConnectEnabled = true;

    [ObservableProperty]
    private bool _startupRankServersEnabled = true;

    [ObservableProperty]
    private bool _allowDesktopNotificationRouting = true;

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
    private bool _adaptiveSurviveEnabled;

    [ObservableProperty]
    private bool _autoReconnectEnabled = true;

    [ObservableProperty]
    private bool _subscriptionViaProxy = true;

    [ObservableProperty]
    private bool _settingsOpen;

    [ObservableProperty]
    private bool _appNetworkOpen;

    [ObservableProperty]
    private string _androidBypassPackages = "";

    [ObservableProperty]
    private string _androidBlockPackages = "";

    [ObservableProperty]
    private string _desktopDirectProcesses = "";

    [ObservableProperty]
    private string _desktopBlockProcesses = "";

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

    [ObservableProperty]
    private string _uploadTrafficText = "↑ 0 B/s";

    [ObservableProperty]
    private string _downloadTrafficText = "↓ 0 B/s";

    [ObservableProperty]
    private string _connectedPingText = "";

    [ObservableProperty]
    private bool _showTrafficStats;

    public string AppVersionLabel => AppVersion.Current;

    /// <summary>Shown only when a newer GitHub release is available (or an update is applying).</summary>
    public bool ShowUpdateChrome => UpdateAvailable || IsUpdating;

    public string UpdateButtonText
    {
        get
        {
            if (IsUpdating)
                return "Updating…";
            if (UpdateAvailable && !string.IsNullOrWhiteSpace(UpdateLabel))
                return $"Update {UpdateLabel}";
            return "Update";
        }
    }

    partial void OnUpdateLabelChanged(string value)
    {
        OnPropertyChanged(nameof(UpdateButtonText));
        OnPropertyChanged(nameof(ShowUpdateChrome));
    }

    partial void OnUpdateAvailableChanged(bool value)
    {
        OnPropertyChanged(nameof(UpdateButtonText));
        OnPropertyChanged(nameof(ShowUpdateChrome));
    }

    partial void OnIsUpdatingChanged(bool value)
    {
        OnPropertyChanged(nameof(UpdateButtonText));
        OnPropertyChanged(nameof(ShowUpdateChrome));
    }

    public bool ShowCustomRules => SelectedRoutingMode?.Mode == RoutingMode.CustomDirect;

    public bool ShowAndroidBypass => IsMobile;

    /// <summary>App Network on Android always; on Desktop when TUN is available/used.</summary>
    public bool ShowAppNetwork => IsMobile || ShowDesktopProxySettings;

    public AppNetworkViewModel AppNetwork { get; }

    public bool HasSelectedServer => SelectedServer is not null;

    public bool HasSavedSubscription => !string.IsNullOrWhiteSpace(SubscriptionUrl);

    public bool CanToggleConnection =>
        !IsBusy && ConnectionState is ConnectionState.Idle or ConnectionState.Connected or ConnectionState.Failed;

    public bool NeedsDisconnect =>
        IsConnected ||
        ConnectionState == ConnectionState.Connected ||
        AppServices.KillSwitch.IsArmed;

    partial void OnSelectedRoutingModeChanged(RoutingModeOption? value) => OnPropertyChanged(nameof(ShowCustomRules));

    partial void OnSelectedServerChanged(ProxyServer? value)
    {
        OnPropertyChanged(nameof(HasSelectedServer));
        if (_suppressSelectionPersist)
            return;

        _settings.SelectedServerId = value?.Id.ToString() ?? "";
        _ = PersistSelectedServerAsync();

        if (_suppressSelectionSwitch || _selectionSwitchInFlight)
            return;
        if (ConnectionState is not ConnectionState.Connected)
            return;
        if (value is null)
            return;
        if (_proxyCore.ActiveServer?.Id == value.Id)
            return;

        _ = ConnectToServerAsync(value);
    }

    partial void OnSubscriptionUrlChanged(string value) => OnPropertyChanged(nameof(HasSavedSubscription));

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanToggleConnection));

    partial void OnConnectionStateChanged(ConnectionState value)
    {
        OnPropertyChanged(nameof(CanToggleConnection));
        if (value == ConnectionState.Connected)
            StartTrafficPolling();
        else
            StopTrafficPolling();
    }

    private async Task PersistSelectedServerAsync()
    {
        try
        {
            await _settingsStore.SaveAsync(CollectSettings()).ConfigureAwait(false);
        }
        catch
        {
            // Best effort — selection still works in-session.
        }
    }

    public MainWindowViewModel()
    {
        _smartConnect = new SmartConnectService(_latencyService);
        _startupRank = new ServerRankingCoordinator(_latencyService);
        AppNetwork = new AppNetworkViewModel(
            getSettings: () =>
            {
                CollectSettings();
                return _settings;
            },
            saveSettingsAsync: async settings =>
            {
                AndroidBypassPackages = settings.AndroidBypassPackages;
                AndroidBlockPackages = settings.AndroidBlockPackages;
                DesktopDirectProcesses = settings.DesktopDirectProcesses;
                DesktopBlockProcesses = settings.DesktopBlockProcesses;
                await _settingsStore.SaveAsync(CollectSettings()).ConfigureAwait(false);
            },
            reconnectIfConnectedAsync: ReconnectToApplyAppNetworkAsync,
            setStatus: text => StatusText = text,
            isMobile: IsMobile);

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
                else if (ConnectionState is ConnectionState.Connected or ConnectionState.Failed)
                {
                    // Unexpected teardown or Failed — don't flash Idle/"Disconnected".
                    // HandleUnexpectedCoreStopAsync / Disconnect owns the final StatusText.
                    IsConnected = false;
                }
                else
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

        _proxyCore.PathHealthOk += (_, _) =>
        {
            _autoReconnectAttempts = 0;
        };

        if (IsMobile)
            AppServices.EmergencyDisconnectAsync = EmergencyDisconnectAsync;

        AppServices.RefreshUpdateCheck = () => _ = CheckForUpdatesQuietlyAsync(userInitiated: false);
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
        RestoreSelectedServer();

        try
        {
            await AppServices.CoreEnvironment.EnsureCoreAsync().ConfigureAwait(true);
        }
        catch
        {
            // Shown via UpdateCoreStatus on next line.
        }

        UpdateCoreStatus();
        _ = CheckForUpdatesQuietlyAsync(userInitiated: false);
        _ = RunStartupRankingIfNeededAsync();
    }

    private async Task RunStartupRankingIfNeededAsync()
    {
        if (Servers.Count == 0 ||
            IsConnected ||
            ConnectionState is ConnectionState.Connecting or ConnectionState.Connected or ConnectionState.Disconnecting)
            return;

        if (!ServerRankingCoordinator.ShouldRunStartupRank(_settings, DateTimeOffset.UtcNow))
            return;

        try
        {
            await SetOnUiAsync(() => StatusText = "Ranking servers…").ConfigureAwait(true);

            var snapshot = Servers.ToList();
            await _startupRank
                .RankAllAsync(snapshot, _settings.EnablePacketFragment)
                .ConfigureAwait(true);

            var fastest = ServerRankingCoordinator.PickFastest(snapshot);
            await SetOnUiAsync(() =>
            {
                ReorderServersByLatency();
                if (fastest is not null)
                {
                    _suppressSelectionPersist = true;
                    try
                    {
                        SelectedServer = Servers.FirstOrDefault(s => s.Id == fastest.Id) ?? fastest;
                        _settings.SelectedServerId = fastest.Id.ToString();
                    }
                    finally
                    {
                        _suppressSelectionPersist = false;
                    }

                    StatusText = $"Fastest: {StatusSanitizer.Scrub(fastest.Name)}";
                }
                else
                {
                    StatusText = "Ranking complete — no reachable servers.";
                }
            }).ConfigureAwait(true);

            _settings.LastStartupRankUtc = DateTimeOffset.UtcNow.ToString("O");
            await _serverStore.SaveAsync(Servers).ConfigureAwait(true);
            await _settingsStore.SaveAsync(CollectSettings()).ConfigureAwait(true);
        }
        catch
        {
            // Startup rank is best-effort; app stays usable.
        }
    }

    private void RestoreSelectedServer()
    {
        _suppressSelectionPersist = true;
        try
        {
            ProxyServer? match = null;
            if (!string.IsNullOrWhiteSpace(_settings.SelectedServerId))
                match = Servers.FirstOrDefault(s => s.Id.ToString() == _settings.SelectedServerId);

            SelectedServer = match ?? Servers.FirstOrDefault();
            if (SelectedServer is not null)
                _settings.SelectedServerId = SelectedServer.Id.ToString();
        }
        finally
        {
            _suppressSelectionPersist = false;
        }
    }

    /// <summary>Called when the main window is activated (desktop) — recheck for updates.</summary>
    public void OnMainWindowActivated()
    {
        if (IsMobile || IsUpdating)
            return;
        if (DateTimeOffset.UtcNow - _lastUpdateCheckUtc < TimeSpan.FromMinutes(30))
            return;
        _ = CheckForUpdatesQuietlyAsync(userInitiated: false);
    }

    private async Task CheckForUpdatesQuietlyAsync(bool userInitiated = false)
    {
        if (AppServices.Updater is null)
        {
            if (userInitiated)
            {
                await SetOnUiAsync(() => StatusText = "Updates are not available in this build.")
                    .ConfigureAwait(true);
            }
            return;
        }

        try
        {
            if (userInitiated)
            {
                await SetOnUiAsync(() => StatusText = "Checking for updates…").ConfigureAwait(true);
            }

            var result = await _updateCheck
                .CheckDetailedAsync(AppServices.Updater.ReleaseAssetFileName)
                .ConfigureAwait(true);
            _lastUpdateCheckUtc = DateTimeOffset.UtcNow;

            switch (result.Status)
            {
                case UpdateCheckStatus.Offer when result.Offer is not null:
                    await SetOnUiAsync(() =>
                    {
                        _pendingUpdate = result.Offer;
                        UpdateAvailable = true;
                        UpdateLabel = result.Offer.Version;
                        OnPropertyChanged(nameof(ShowUpdateChrome));
                        if (!IsBusy && !IsUpdating && ConnectionState != ConnectionState.Connected)
                            StatusText = $"v{result.Offer.Version} is available — tap Update.";
                    }).ConfigureAwait(true);
                    break;

                case UpdateCheckStatus.UpToDate:
                    await SetOnUiAsync(() =>
                    {
                        _pendingUpdate = null;
                        UpdateAvailable = false;
                        UpdateLabel = "";
                        OnPropertyChanged(nameof(ShowUpdateChrome));
                        if (userInitiated)
                            StatusText = $"You are on v{AppVersion.Current} (latest).";
                    }).ConfigureAwait(true);
                    break;

                case UpdateCheckStatus.TransientError:
                    // Keep prior offer visible — do not clear chrome on flaky GitHub.
                    await SetOnUiAsync(() =>
                    {
                        OnPropertyChanged(nameof(ShowUpdateChrome));
                        if (userInitiated)
                            StatusText =
                                $"Update check failed: {StatusSanitizer.Scrub(result.ErrorMessage ?? "network error")}";
                    }).ConfigureAwait(true);
                    break;

                case UpdateCheckStatus.NoAsset:
                    await SetOnUiAsync(() =>
                    {
                        if (_pendingUpdate is null)
                        {
                            UpdateAvailable = false;
                            UpdateLabel = "";
                        }

                        OnPropertyChanged(nameof(ShowUpdateChrome));
                        if (userInitiated)
                            StatusText =
                                $"Update package unavailable: {StatusSanitizer.Scrub(result.ErrorMessage ?? "no asset")}";
                    }).ConfigureAwait(true);
                    break;
            }
        }
        catch (Exception ex)
        {
            _lastUpdateCheckUtc = DateTimeOffset.UtcNow;
            await SetOnUiAsync(() =>
            {
                OnPropertyChanged(nameof(ShowUpdateChrome));
                if (userInitiated)
                    StatusText = $"Update check failed: {StatusSanitizer.Scrub(ex.Message)}";
            }).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task UpdateChromeAsync()
    {
        if (IsUpdating)
            return;

        // Button is only visible when an update is available — always apply.
        if (UpdateAvailable && _pendingUpdate is not null)
        {
            await ApplyUpdateAsync().ConfigureAwait(true);
            return;
        }

        await CheckForUpdatesQuietlyAsync(userInitiated: true).ConfigureAwait(true);
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
                StatusText = IsMobile
                    ? "Confirm the system Install prompt, then return here."
                    : "Installing update and restarting…").ConfigureAwait(true);
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
        StartupRankServersEnabled = settings.StartupRankServersEnabled;
        AllowDesktopNotificationRouting = settings.AllowDesktopNotificationRouting;
        SmartMultipathEnabled = settings.SmartMultipathEnabled;
        KillSwitchEnabled = settings.KillSwitchEnabled;
        BlockIpv6 = settings.BlockIpv6;
        DnsThroughProxy = settings.DnsThroughProxy;
        SecureShareEnabled = settings.SecureShareEnabled;
        EnablePacketFragment = settings.EnablePacketFragment;
        AdaptiveSurviveEnabled = settings.AdaptiveSurviveEnabled;
        AutoReconnectEnabled = settings.AutoReconnectEnabled;
        ShareListenAllInterfaces = settings.ShareListenAllInterfaces;
        SubscriptionViaProxy = settings.SubscriptionViaProxy;
        AndroidBypassPackages = settings.AndroidBypassPackages;
        AndroidBlockPackages = settings.AndroidBlockPackages;
        DesktopDirectProcesses = settings.DesktopDirectProcesses;
        DesktopBlockProcesses = settings.DesktopBlockProcesses;
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
        _settings.StartupRankServersEnabled = StartupRankServersEnabled;
        _settings.AllowDesktopNotificationRouting = AllowDesktopNotificationRouting;
        _settings.SmartMultipathEnabled = SmartMultipathEnabled;
        _settings.SelectedServerId = SelectedServer?.Id.ToString() ?? _settings.SelectedServerId;
        _settings.KillSwitchEnabled = KillSwitchEnabled;
        _settings.BlockIpv6 = BlockIpv6;
        _settings.DnsThroughProxy = DnsThroughProxy;
        _settings.SecureShareEnabled = SecureShareEnabled;
        _settings.ShareListenAllInterfaces = ShareListenAllInterfaces;
        _settings.EnablePacketFragment = EnablePacketFragment;
        _settings.AdaptiveSurviveEnabled = AdaptiveSurviveEnabled;
        _settings.AutoReconnectEnabled = AutoReconnectEnabled;
        _settings.SubscriptionViaProxy = SubscriptionViaProxy;
        _settings.AndroidBypassPackages = AndroidBypassPackages;
        _settings.AndroidBlockPackages = AndroidBlockPackages;
        _settings.DesktopDirectProcesses = DesktopDirectProcesses;
        _settings.DesktopBlockProcesses = DesktopBlockProcesses;
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
        OnPropertyChanged(nameof(KillSwitchUsable));
        if (value && EnableSystemProxy)
            EnableSystemProxy = false;
    }

    /// <summary>Kill switch only applies with TUN (system-proxy mode must not blackhole apps).</summary>
    public bool KillSwitchUsable => IsMobile || EnableTunMode;

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

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        SettingsOpen = true;
    }

    [RelayCommand]
    private async Task CloseSettingsAsync()
    {
        SettingsOpen = false;
        await SaveSettingsAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task OpenAppNetworkAsync()
    {
        AppNetworkOpen = true;
        await AppNetwork.OnOpenedAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task CloseAppNetworkAsync()
    {
        await AppNetwork.OnClosedAsync().ConfigureAwait(true);
        AppNetworkOpen = false;
        AndroidBypassPackages = _settings.AndroidBypassPackages;
        AndroidBlockPackages = _settings.AndroidBlockPackages;
        DesktopDirectProcesses = _settings.DesktopDirectProcesses;
        DesktopBlockProcesses = _settings.DesktopBlockProcesses;
        await SaveSettingsAsync().ConfigureAwait(true);
    }

    private async Task ReconnectToApplyAppNetworkAsync()
    {
        if (!IsConnected && ConnectionState != ConnectionState.Connected)
        {
            StatusText = "App Network saved — connect to apply.";
            return;
        }

        var server = SelectedServer ?? _proxyCore.ActiveServer;
        await DisconnectAsync().ConfigureAwait(true);
        if (server is null)
        {
            StatusText = "App Network saved.";
            return;
        }

        StatusText = "Reconnecting to apply App Network…";
        await ConnectWithOrchestrationAsync(server).ConfigureAwait(true);
    }

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
        RestoreSelectedServer();
    }

    [RelayCommand]
    private async Task TestLatencyAsync()
    {
        if (SelectedServer is null)
        {
            StatusText = "Select a server to test proxy delay.";
            return;
        }

        StatusText = "Testing delay…";
        await MeasureLatencyAsync(SelectedServer);
        if (SelectedServer.LatencyMs is > 0)
            StatusText = $"Delay: {SelectedServer.LatencyMs} ms";
        else if (string.IsNullOrWhiteSpace(StatusText) || StatusText.StartsWith("Testing", StringComparison.Ordinal))
            StatusText = "Delay: timeout (proxy probe failed)";
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
        StatusText = "Testing delay for all servers…";
        try
        {
            var snapshot = Servers.ToList();
            await SetOnUiAsync(() =>
            {
                foreach (var server in snapshot)
                    server.SetLatency(null);
            }).ConfigureAwait(true);

            await SetOnUiAsync(() => StatusText = "Verifying proxy path…").ConfigureAwait(true);

            await _startupRank
                .RankAllAsync(snapshot, _settings.EnablePacketFragment)
                .ConfigureAwait(true);

            foreach (var server in snapshot)
            {
                var ranked = server;
                RunOnUiThread(() =>
                {
                    var target = Servers.FirstOrDefault(s => s.Id == ranked.Id);
                    target?.SetLatency(ranked.LatencyMs);
                });
            }

            await SetOnUiAsync(ReorderServersByLatency).ConfigureAwait(true);
            await _serverStore.SaveAsync(Servers).ConfigureAwait(true);
            var ok = Servers.Count(s => s.LatencyMs is > 0);
            StatusText = $"Delay test complete — {ok}/{Servers.Count} reachable.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task MeasureLatencyAsync(ProxyServer server)
    {
        RunOnUiThread(() => server.SetLatency(null));

        var result = await _latencyService.MeasureAsync(
            server,
            enableFragment: _settings.EnablePacketFragment);

        RunOnUiThread(() =>
        {
            server.SetLatency(result);
            if (result is null or < 0 && !string.IsNullOrWhiteSpace(_latencyService.LastProbeError))
                StatusText = $"Delay: timeout ({_latencyService.LastProbeError})";
        });
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
            await RunOnUiThreadAsync(() => ConnectWithOrchestrationAsync()).ConfigureAwait(true);
        else
            await ConnectWithOrchestrationAsync().ConfigureAwait(true);
    }

    private async Task ConnectWithOrchestrationAsync(ProxyServer? forceServer = null)
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
            var forceSurviveWaves = false;
            if (forceServer is not null)
            {
                candidates = [forceServer];
                if (settings.SmartMultipathEnabled && Servers.Count > 1)
                {
                    var serversSnapshot = Servers.ToList();
                    _lastRanking = await _smartConnect.RankAsync(
                        serversSnapshot,
                        token,
                        settings.EnablePacketFragment,
                        preferred: forceServer).ConfigureAwait(false);
                    await ResumeOnUiAsync().ConfigureAwait(true);
                    await SetOnUiAsync(() =>
                    {
                        foreach (var ranked in _lastRanking)
                            ranked.Server.SetLatency(ranked.UiLatencyMs);
                        ReorderServersByLatency();
                    }).ConfigureAwait(true);
                }
            }
            else if (settings.SmartConnectEnabled && Servers.Count > 0)
            {
                await SetOnUiAsync(() => StatusText = "Connecting to fastest…").ConfigureAwait(true);
                var serversSnapshot = Servers.ToList();
                ProxyServer? lastGood = null;
                if (!string.IsNullOrWhiteSpace(settings.LastGoodServerId) &&
                    Guid.TryParse(settings.LastGoodServerId, out var lastGoodId))
                {
                    lastGood = serversSnapshot.FirstOrDefault(s => s.Id == lastGoodId);
                }

                using var rankCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                var rankTask = _smartConnect.RankAsync(
                    serversSnapshot,
                    rankCts.Token,
                    settings.EnablePacketFragment,
                    preferred: lastGood);

                // Race last-good Connect against ranking — cancel rank if last-good wins.
                if (lastGood is not null)
                {
                    try
                    {
                        await SetOnUiAsync(() =>
                        {
                            _suppressSelectionSwitch = true;
                            try { SelectedServer = lastGood; }
                            finally { _suppressSelectionSwitch = false; }
                            StatusText = $"Connecting to {StatusSanitizer.Scrub(lastGood.Name)}…";
                        }).ConfigureAwait(true);

                        if (IsMobile)
                            await ConnectAndroidAsync(lastGood, settings, null, token).ConfigureAwait(false);
                        else
                            await ConnectDesktopAsync(lastGood, settings, null, token).ConfigureAwait(false);

                        try { rankCts.Cancel(); }
                        catch (ObjectDisposedException) { /* ignore */ }

                        await ResumeOnUiAsync().ConfigureAwait(true);
                        settings.LastGoodServerId = lastGood.Id.ToString();
                        await _settingsStore.SaveAsync(settings).ConfigureAwait(false);
                        await _serverStore.SaveAsync(Servers).ConfigureAwait(false);
                        await SetOnUiAsync(() =>
                        {
                            ConnectionState = ConnectionState.Connected;
                            _autoReconnectAttempts = 0;
                            UpdateSecureShareEndpoint();
                        }).ConfigureAwait(true);
                        return;
                    }
                    catch (OperationCanceledException)
                    {
                        try { rankCts.Cancel(); }
                        catch (ObjectDisposedException) { /* ignore */ }
                        throw;
                    }
                    catch
                    {
                        await SafeTeardownAsync(releaseKillSwitch: false).ConfigureAwait(false);
                        await ResumeOnUiAsync().ConfigureAwait(true);
                        await SetOnUiAsync(() => StatusText = "Connecting to fastest…").ConfigureAwait(true);
                    }
                }

                try
                {
                    _lastRanking = await rankTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    _lastRanking = [];
                }

                await ResumeOnUiAsync().ConfigureAwait(true);
                await SetOnUiAsync(() =>
                {
                    foreach (var ranked in _lastRanking)
                        ranked.Server.SetLatency(ranked.UiLatencyMs);
                    ReorderServersByLatency();
                }).ConfigureAwait(true);

                candidates = _smartConnect.SelectConnectOrder(
                    _lastRanking,
                    preferred: null,
                    lastGoodServerId: settings.LastGoodServerId);

                // Skip last-good if we already failed it above.
                if (lastGood is not null)
                    candidates = candidates.Where(s => s.Id != lastGood.Id).ToList();

                if (candidates.Count == 0)
                {
                    if (!settings.AdaptiveSurviveEnabled)
                    {
                        await SetOnUiAsync(() =>
                        {
                            StatusText =
                                "Smart Connect: no usable proxy path. Turn on Adaptive Survive only if DPI blocks Connect (slower).";
                            ConnectionState = ConnectionState.Failed;
                        }).ConfigureAwait(true);
                        return;
                    }

                    candidates = _smartConnect.SelectSurviveConnectOrder(
                        _lastRanking,
                        preferred: null,
                        lastGoodServerId: settings.LastGoodServerId);

                    if (lastGood is not null)
                        candidates = candidates.Where(s => s.Id != lastGood.Id).ToList();

                    if (candidates.Count == 0)
                    {
                        await SetOnUiAsync(() =>
                        {
                            StatusText = "Smart Connect: no candidates to try.";
                            ConnectionState = ConnectionState.Failed;
                        }).ConfigureAwait(true);
                        return;
                    }

                    forceSurviveWaves = true;
                    await SetOnUiAsync(() =>
                        StatusText = "Smart Connect: no proxy-path OK — trying Survive tactics…")
                        .ConfigureAwait(true);
                }
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
                    _lastRanking = await _smartConnect.RankAsync(
                        serversSnapshot,
                        token,
                        settings.EnablePacketFragment,
                        SelectedServer).ConfigureAwait(false);
                    await ResumeOnUiAsync().ConfigureAwait(true);
                }
            }

            Exception? lastError = null;
            var connectSettings = settings;
            var attempts = new List<(AppSettings Settings, string? Tactic, string? Reason)>
            {
                (settings, null, null)
            };
            if ((settings.AdaptiveSurviveEnabled || forceSurviveWaves) && settings.SmartConnectEnabled)
            {
                foreach (var survive in _adaptiveSurvive.BuildRetryAttempts(settings, force: forceSurviveWaves))
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
                        _suppressSelectionSwitch = true;
                        try
                        {
                            SelectedServer = server;
                        }
                        finally
                        {
                            _suppressSelectionSwitch = false;
                        }

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
                                _lastRanking = await _smartConnect.RankAsync(
                                    serversSnapshot,
                                    token,
                                    enableFragment: false,
                                    preferred: SelectedServer).ConfigureAwait(false);
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
                            _autoReconnectAttempts = 0;
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

        // Start core first; arm kill switch only with TUN (system-proxy mode must not blackhole apps).
        await _proxyCore.StartAsync(server, settings, tunFd, multipath, cancellationToken).ConfigureAwait(false);
        await ResumeOnUiAsync().ConfigureAwait(true);

        if (settings.KillSwitchEnabled && settings.EnableTunMode)
        {
            await AppServices.KillSwitch.EnableAsync(
                    _proxyCore.ResolveCorePath(),
                    allowTunInterface: true,
                    cancellationToken)
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
        if (settings.KillSwitchEnabled && settings.EnableTunMode &&
            !string.IsNullOrWhiteSpace(AppServices.KillSwitch.LastError) &&
            !AppServices.KillSwitch.IsArmed)
        {
            status += $" · kill switch not armed ({StatusSanitizer.Scrub(AppServices.KillSwitch.LastError)})";
        }

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
        var bypass = AppNetworkPolicy.GetDirectIds(settings, mobile: true);
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
        await AppServices.Platform.NotifyVpnReadyAsync(cancellationToken).ConfigureAwait(false);

        await AppServices.KillSwitch.EnableAsync(
                _proxyCore.ResolveCorePath(),
                allowTunInterface: true,
                cancellationToken)
            .ConfigureAwait(false);
        await AppServices.Platform.EnableProxyAsync(cancellationToken).ConfigureAwait(false);
        await ResumeOnUiAsync().ConfigureAwait(true);

        var multi = multipath is { Count: > 1 } ? $" · multipath×{multipath.Count}" : "";
        await SetOnUiAsync(() =>
        {
            StatusText =
                $"Connected — {StatusSanitizer.Scrub(server.Name)} (VPN{multi}). Tip: force-stop Instagram once for Direct.";
            IsConnected = true;
        }).ConfigureAwait(true);
    }

    private int _autoReconnectAttempts;
    public const int MaxAutoReconnectAttempts = 2;

    private async Task HandleUnexpectedCoreStopAsync()
    {
        var settings = CollectSettings();
        var canAutoReconnect = settings.AutoReconnectEnabled && SelectedServer is not null;
        var remaining = MaxAutoReconnectAttempts - _autoReconnectAttempts;
        var shouldReconnect = canAutoReconnect && remaining > 0;

        try
        {
            // Tear down proxy/VPN but KEEP kill switch armed (fail closed).
            await SafeTeardownAsync(releaseKillSwitch: false).ConfigureAwait(false);
        }
        finally
        {
            if (!shouldReconnect)
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

        if (!shouldReconnect)
            return;

        Exception? lastError = null;
        for (var i = 0; i < remaining; i++)
        {
            _autoReconnectAttempts++;
            var attempt = _autoReconnectAttempts;
            try
            {
                await SetOnUiAsync(() =>
                {
                    ConnectionState = ConnectionState.Connecting;
                    StatusText = attempt > 1
                        ? $"Connection dropped — reconnecting (try {attempt}/{MaxAutoReconnectAttempts})…"
                        : "Connection dropped — reconnecting…";
                    OnPropertyChanged(nameof(ConnectButtonText));
                }).ConfigureAwait(true);

                if (attempt > 1)
                    await Task.Delay(attempt * 1500, CancellationToken.None).ConfigureAwait(false);

                // Sticky retry with user settings only (never force Survive/fragment).
                var server = SelectedServer!;
                var connectSettings = CollectSettings();
                connectSettings.EnablePacketFragment = EnablePacketFragment;
                connectSettings.AdaptiveSurviveEnabled = false;

                if (IsMobile)
                    await ConnectAndroidAsync(server, connectSettings, null, CancellationToken.None).ConfigureAwait(false);
                else
                    await ConnectDesktopAsync(server, connectSettings, null, CancellationToken.None).ConfigureAwait(false);

                await SetOnUiAsync(() =>
                {
                    ConnectionState = ConnectionState.Connected;
                    StatusText = $"Reconnected — {StatusSanitizer.Scrub(server.Name)}";
                    OnPropertyChanged(nameof(ConnectButtonText));
                    OnPropertyChanged(nameof(TrayToolTip));
                    UpdateSecureShareEndpoint();
                }).ConfigureAwait(true);
                _autoReconnectAttempts = 0;
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        await SetOnUiAsync(() =>
        {
            ConnectionState = ConnectionState.Failed;
            var detail = lastError is null ? "" : $" ({StatusSanitizer.Scrub(lastError.Message)})";
            StatusText = AppServices.KillSwitch.IsArmed
                ? $"Reconnect failed — kill switch still blocking clearnet. Disconnect to restore.{detail}"
                : $"Reconnect failed —{detail.TrimStart()}";
            OnPropertyChanged(nameof(ConnectButtonText));
            OnPropertyChanged(nameof(TrayToolTip));
            UpdateSecureShareEndpoint();
        }).ConfigureAwait(true);
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
                _autoReconnectAttempts = 0;
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

        if (ConnectionState is ConnectionState.Connecting or ConnectionState.Disconnecting)
            return;

        if (_selectionSwitchInFlight)
            return;

        _selectionSwitchInFlight = true;
        try
        {
            _suppressSelectionSwitch = true;
            try
            {
                SelectedServer = server;
            }
            finally
            {
                _suppressSelectionSwitch = false;
            }

            if (IsConnected && _proxyCore.ActiveServer?.Id == server.Id)
                return;

            if (IsConnected)
                await DisconnectAsync();

            if (IsMobile)
                await RunOnUiThreadAsync(() => ConnectWithOrchestrationAsync(forceServer: server)).ConfigureAwait(true);
            else
                await ConnectWithOrchestrationAsync(forceServer: server).ConfigureAwait(true);
        }
        finally
        {
            _selectionSwitchInFlight = false;
        }
    }

    public async Task ShutdownAsync()
    {
        await DisconnectAsync();
        await _proxyCore.DisposeAsync();
    }

    private async Task ImportParsedAsync(string text)
    {
        if (Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https" &&
            !text.Contains('\n') &&
            !text.TrimStart().StartsWith('{') &&
            !text.Contains("vmess://", StringComparison.OrdinalIgnoreCase) &&
            !text.Contains("vless://", StringComparison.OrdinalIgnoreCase))
        {
            SubscriptionUrl = text.Trim();
            if (await TryImportSubscriptionAsync())
                ImportText = "";
            return;
        }

        var imported = ConfigImportParser.ParseDetailed(text);
        if (imported.Servers.Count == 0)
        {
            StatusText = string.IsNullOrEmpty(imported.SummaryHint)
                ? "No valid proxy configs found (share links, Xray JSON, Clash Meta, or bulk list)."
                : imported.SummaryHint;
            return;
        }

        await MergeImportedAsync(imported.Servers);
        ImportText = "";
        var hint = imported.SummaryHint;
        StatusText = string.IsNullOrEmpty(hint)
            ? $"Imported {imported.Servers.Count} server(s)."
            : $"Imported {imported.Servers.Count} server(s). {hint}";
    }

    [RelayCommand]
    private async Task ImportConfigFileAsync()
    {
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
                Title = "Import configs",
                AllowMultiple = true,
                FileTypeFilter =
                [
                    new FilePickerFileType("Config files")
                    {
                        Patterns = ["*.txt", "*.json", "*.v2box", "*.npv", "*.conf", "*.yaml", "*.yml", "*.*"]
                    },
                    new FilePickerFileType("All files") { Patterns = ["*.*"] }
                ]
            }).ConfigureAwait(true);

            if (files.Count == 0)
                return;

            IsBusy = true;
            var all = new List<ProxyServer>();
            var hints = new List<string>();
            foreach (var file in files)
            {
                await using var stream = await file.OpenReadAsync().ConfigureAwait(true);
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms).ConfigureAwait(true);
                var detailed = ConfigImportParser.ParseBytesDetailed(ms.ToArray(), file.Name);
                all.AddRange(detailed.Servers);
                if (!string.IsNullOrEmpty(detailed.SummaryHint) && !hints.Contains(detailed.SummaryHint))
                    hints.Add(detailed.SummaryHint);
            }

            if (all.Count == 0)
            {
                StatusText = hints.Count > 0
                    ? string.Join(" ", hints)
                    : "No valid proxy configs found in the selected file(s).";
                return;
            }

            await MergeImportedAsync(all).ConfigureAwait(true);
            StatusText = hints.Count > 0
                ? $"Imported {all.Count} server(s) from file(s). {string.Join(" ", hints)}"
                : $"Imported {all.Count} server(s) from file(s).";
        }
        catch (Exception ex)
        {
            StatusText = $"Import failed: {StatusSanitizer.Scrub(ex.Message)}";
        }
        finally
        {
            IsBusy = false;
        }
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
            var hint = ConfigImportParser.LastSkippedSingBoxHint;
            var baseMsg = viaProxy
                ? $"Imported {imported.Count} server(s) via proxy."
                : $"Imported {imported.Count} server(s) from subscription.";
            StatusText = string.IsNullOrEmpty(hint) ? baseMsg : $"{baseMsg} {hint}";
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
        var added = 0;
        foreach (var server in imported)
        {
            if (Servers.Any(existing =>
                    (!string.IsNullOrEmpty(existing.RawLink) && existing.RawLink == server.RawLink) ||
                    (existing.Address == server.Address &&
                     existing.Port == server.Port &&
                     existing.Protocol == server.Protocol &&
                     existing.UserId == server.UserId)))
                continue;

            Servers.Add(server);
            added++;
        }

        if (SelectedServer is null)
            RestoreSelectedServer();

        await _serverStore.SaveAsync(Servers);
        if (added != imported.Count)
            StatusText = $"Imported {added} new server(s) ({imported.Count - added} duplicate(s) skipped).";
    }

    private static IClipboard? GetClipboard()
    {
        return GetTopLevel()?.Clipboard;
    }

    private void StartTrafficPolling()
    {
        StopTrafficPolling();
        TrafficStatsHub.Shared.Reset();
        RunOnUiThread(() =>
        {
            ShowTrafficStats = true;
            UploadTrafficText = TrafficStatsService.FormatUploadRate(0);
            DownloadTrafficText = TrafficStatsService.FormatDownloadRate(0);
            // Keep Test All / Smart Connect TCP ms; append connect-path HTTPS when known.
            var tcp = SelectedServer?.LatencyMs is int ms and > 0 ? ms : (int?)null;
            var path = _proxyCore.LastConnectProbeMs;
            ConnectedPingText = tcp is > 0
                ? path is > 0 ? $"{tcp} · path {path}" : $"{tcp}"
                : path is > 0 ? $"path {path}" : "";
            TrafficStatsHub.Shared.ConnectedPingMs = tcp;
        });

        TrafficStatsHub.Shared.Updated += OnHubTrafficUpdated;
        TrafficStatsHub.Shared.Subscribe();
    }

    private void StopTrafficPolling()
    {
        TrafficStatsHub.Shared.Updated -= OnHubTrafficUpdated;
        TrafficStatsHub.Shared.Unsubscribe();
        TrafficStatsHub.Shared.Reset();

        _lastRanking = [];
        RunOnUiThread(() =>
        {
            ShowTrafficStats = false;
            UploadTrafficText = TrafficStatsService.FormatUploadRate(0);
            DownloadTrafficText = TrafficStatsService.FormatDownloadRate(0);
            ConnectedPingText = "";
        });
    }

    private void OnHubTrafficUpdated(TrafficStatsHub.LiveTraffic traffic)
    {
        var up = TrafficStatsService.FormatUploadRate(traffic.UplinkBps);
        var down = TrafficStatsService.FormatDownloadRate(traffic.DownlinkBps);
        var ping = TrafficStatsHub.Shared.ConnectedPingMs is int p and > 0 ? $"{p}" : ConnectedPingText;

        RunOnUiThread(() =>
        {
            if (UploadTrafficText != up)
                UploadTrafficText = up;
            if (DownloadTrafficText != down)
                DownloadTrafficText = down;
            if (!ShowTrafficStats)
                ShowTrafficStats = true;
            if (TrafficStatsHub.Shared.ConnectedPingMs is int and > 0 && ConnectedPingText != ping)
                ConnectedPingText = ping;
        });

        AppServices.OnLiveTraffic?.Invoke(
            traffic.UplinkBps,
            traffic.DownlinkBps,
            TrafficStatsHub.Shared.ConnectedPingMs);
    }

    private void ReorderServersByLatency()
    {
        var selectedId = SelectedServer?.Id;
        var ordered = ServerLatencySort.Order(Servers);
        Servers = new ObservableCollection<ProxyServer>(ordered);
        if (selectedId is Guid id)
            SelectedServer = Servers.FirstOrDefault(s => s.Id == id);
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
