using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using v2rayF.Models;
using v2rayF.Services;

namespace v2rayF.ViewModels;

public partial class AppNetworkViewModel : ViewModelBase
{
    public enum FilterKind
    {
        All,
        Vpn,
        Direct,
        Block
    }

    private readonly Func<AppSettings> _getSettings;
    private readonly Func<AppSettings, Task> _saveSettingsAsync;
    private readonly Func<Task> _reconnectIfConnectedAsync;
    private readonly Action<string> _setStatus;
    private readonly bool _isMobile;

    private CancellationTokenSource? _trafficCts;
    private bool _dirty;
    private List<AppNetworkItemViewModel> _all = [];

    public AppNetworkViewModel(
        Func<AppSettings> getSettings,
        Func<AppSettings, Task> saveSettingsAsync,
        Func<Task> reconnectIfConnectedAsync,
        Action<string> setStatus,
        bool isMobile)
    {
        _getSettings = getSettings;
        _saveSettingsAsync = saveSettingsAsync;
        _reconnectIfConnectedAsync = reconnectIfConnectedAsync;
        _setStatus = setStatus;
        _isMobile = isMobile;
    }

    public ObservableCollection<AppNetworkItemViewModel> VisibleApps { get; } = [];

    public IReadOnlyList<FilterKind> Filters { get; } =
        [FilterKind.All, FilterKind.Vpn, FilterKind.Direct, FilterKind.Block];

    [ObservableProperty]
    private FilterKind _selectedFilter = FilterKind.All;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _subtitle = "";

    [ObservableProperty]
    private string _advancedDirectText = "";

    [ObservableProperty]
    private string _advancedBlockText = "";

    [ObservableProperty]
    private bool _showAdvanced;

    public string PlatformHint => _isMobile
        ? "Direct = clearnet (outside VPN). Block = no internet while VPN is on."
        : "Requires TUN. Direct = core direct egress (not OS bypass). Block = blackhole.";

    partial void OnSearchTextChanged(string value) => RebuildVisible();

    partial void OnSelectedFilterChanged(FilterKind value) => RebuildVisible();

    public async Task OnOpenedAsync()
    {
        var settings = _getSettings();
        AdvancedDirectText = _isMobile ? settings.AndroidBypassPackages : settings.DesktopDirectProcesses;
        AdvancedBlockText = _isMobile ? settings.AndroidBlockPackages : settings.DesktopBlockProcesses;
        Subtitle = _isMobile ? "Installed apps" : "Running processes";
        await RefreshInternalAsync(force: false).ConfigureAwait(true);
        StartTrafficPolling();
    }

    public async Task OnClosedAsync()
    {
        StopTrafficPolling();
        if (_dirty)
            await ApplyAsync(reconnect: true).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await RefreshInternalAsync(force: true).ConfigureAwait(true);
    }

    private async Task RefreshInternalAsync(bool force)
    {
        IsLoading = true;
        try
        {
            var platform = AppServices.Platform;
            if (platform is null)
            {
                _all = [];
                RebuildVisible();
                return;
            }

            var apps = await platform.GetNetworkAppsAsync(force, CancellationToken.None).ConfigureAwait(false);
            await ResumeOnUiAsync().ConfigureAwait(true);
            var settings = _getSettings();
            _all = apps.Select(a =>
            {
                var mode = AppNetworkPolicy.GetMode(settings, a.Id, _isMobile);
                var item = new AppNetworkItemViewModel(a, mode);
                item.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(AppNetworkItemViewModel.Mode) && !a.IsSelf)
                    {
                        _dirty = true;
                        var live = _getSettings();
                        AppNetworkPolicy.SetMode(live, a.Id, item.Mode, _isMobile);
                        SyncAdvancedFromSettings(live);
                    }
                };
                return item;
            }).ToList();
            RebuildVisible();
        }
        catch (Exception ex)
        {
            _setStatus($"App Network: {StatusSanitizer.Scrub(ex.Message)}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        await ApplyAsync(reconnect: true).ConfigureAwait(true);
    }

    private async Task ApplyAsync(bool reconnect)
    {
        var settings = _getSettings();
        if (ShowAdvanced)
        {
            if (_isMobile)
            {
                settings.AndroidBypassPackages = AdvancedDirectText;
                settings.AndroidBlockPackages = AdvancedBlockText;
            }
            else
            {
                settings.DesktopDirectProcesses = AdvancedDirectText;
                settings.DesktopBlockProcesses = AdvancedBlockText;
            }
        }
        else
        {
            SyncSettingsFromItems(settings);
        }

        await _saveSettingsAsync(settings).ConfigureAwait(true);
        _dirty = false;
        SyncAdvancedFromSettings(settings);

        if (reconnect)
        {
            _setStatus("Reconnecting to apply App Network…");
            await _reconnectIfConnectedAsync().ConfigureAwait(true);
        }
        else
        {
            _setStatus("App Network saved.");
        }
    }

    [RelayCommand]
    private void SetVisibleMode(string modeName)
    {
        if (!Enum.TryParse<AppNetworkMode>(modeName, ignoreCase: true, out var mode))
            return;

        var settings = _getSettings();
        foreach (var item in VisibleApps)
        {
            if (item.IsSelf)
                continue;
            item.Mode = mode;
            AppNetworkPolicy.SetMode(settings, item.Id, mode, _isMobile);
        }

        _dirty = true;
        SyncAdvancedFromSettings(settings);
    }

    private void SyncSettingsFromItems(AppSettings settings)
    {
        foreach (var item in _all)
        {
            if (item.IsSelf)
                continue;
            AppNetworkPolicy.SetMode(settings, item.Id, item.Mode, _isMobile);
        }
    }

    private void SyncAdvancedFromSettings(AppSettings settings)
    {
        AdvancedDirectText = _isMobile ? settings.AndroidBypassPackages : settings.DesktopDirectProcesses;
        AdvancedBlockText = _isMobile ? settings.AndroidBlockPackages : settings.DesktopBlockProcesses;
    }

    private void RebuildVisible()
    {
        VisibleApps.Clear();
        var q = SearchText.Trim();
        foreach (var item in _all)
        {
            if (SelectedFilter == FilterKind.Vpn && item.Mode != AppNetworkMode.Vpn)
                continue;
            if (SelectedFilter == FilterKind.Direct && item.Mode != AppNetworkMode.Direct)
                continue;
            if (SelectedFilter == FilterKind.Block && item.Mode != AppNetworkMode.Block)
                continue;

            if (q.Length > 0 &&
                item.DisplayName.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 &&
                item.Id.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            VisibleApps.Add(item);
        }
    }

    private void StartTrafficPolling()
    {
        StopTrafficPolling();
        if (!_isMobile)
            return;

        _trafficCts = new CancellationTokenSource();
        var token = _trafficCts.Token;
        _ = PollTrafficLoopAsync(token);
    }

    private void StopTrafficPolling()
    {
        try
        {
            _trafficCts?.Cancel();
            _trafficCts?.Dispose();
        }
        catch
        {
            // ignore
        }

        _trafficCts = null;
    }

    private async Task PollTrafficLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(4000, token).ConfigureAwait(false);
                var ids = _all.Select(a => a.Id).Take(80).ToList();
                if (ids.Count == 0)
                    continue;

                var snaps = await AppServices.Platform.GetAppTrafficAsync(ids, token).ConfigureAwait(false);
                await SetOnUiAsync(() =>
                {
                    foreach (var item in _all)
                    {
                        if (!snaps.TryGetValue(item.Id, out var snap))
                        {
                            item.TrafficText = "";
                            continue;
                        }

                        item.TrafficText =
                            $"↓ {FormatRate(snap.DownloadBytesPerSec)}  ↑ {FormatRate(snap.UploadBytesPerSec)}";
                    }
                }).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Best-effort sampling.
            }
        }
    }

    private static string FormatRate(double bytesPerSec)
    {
        if (bytesPerSec < 1024)
            return $"{bytesPerSec:0} B/s";
        if (bytesPerSec < 1024 * 1024)
            return $"{bytesPerSec / 1024:0.0} KB/s";
        return $"{bytesPerSec / (1024 * 1024):0.00} MB/s";
    }
}
