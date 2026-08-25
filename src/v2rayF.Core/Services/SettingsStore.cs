using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using v2rayF.Models;

namespace v2rayF.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _settingsPath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public SettingsStore()
    {
        var folder = AppServices.CoreEnvironment?.GetDataDirectory()
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "v2rayF");
        Directory.CreateDirectory(folder);
        _settingsPath = Path.Combine(folder, "settings.json");
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_settingsPath))
                return new AppSettings();

            await using var stream = File.OpenRead(_settingsPath);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            settings ??= new AppSettings();

            var hadLegacyPlaintext = HasUnprotectedSecrets(settings);
            UnprotectSensitive(settings);

            if (settings.StorageVersion < 2 || hadLegacyPlaintext)
            {
                settings.StorageVersion = 2;
                await WriteUnlockedAsync(settings, cancellationToken).ConfigureAwait(false);
            }

            return settings;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            settings.StorageVersion = 2;
            await WriteUnlockedAsync(settings, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task WriteUnlockedAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var toWrite = CloneForDisk(settings);
        ProtectSensitive(toWrite);
        await using var stream = File.Create(_settingsPath);
        await JsonSerializer.SerializeAsync(stream, toWrite, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    private static AppSettings CloneForDisk(AppSettings s) => new()
    {
        RoutingMode = s.RoutingMode,
        CustomDirectRules = s.CustomDirectRules,
        CustomProxyRules = s.CustomProxyRules,
        CustomBlockRules = s.CustomBlockRules,
        EnableTunMode = s.EnableTunMode,
        EnableSystemProxy = s.EnableSystemProxy,
        SubscriptionUrl = s.SubscriptionUrl,
        SmartConnectEnabled = s.SmartConnectEnabled,
        StartupRankServersEnabled = s.StartupRankServersEnabled,
        LastStartupRankUtc = s.LastStartupRankUtc,
        AllowDesktopNotificationRouting = s.AllowDesktopNotificationRouting,
        SmartMultipathEnabled = s.SmartMultipathEnabled,
        SelectedServerId = s.SelectedServerId,
        KillSwitchEnabled = s.KillSwitchEnabled,
        BlockIpv6 = s.BlockIpv6,
        DnsThroughProxy = s.DnsThroughProxy,
        SecureShareEnabled = s.SecureShareEnabled,
        ShareBindPort = s.ShareBindPort,
        ShareAuthUser = s.ShareAuthUser,
        ShareAuthPass = s.ShareAuthPass,
        ShareListenAllInterfaces = s.ShareListenAllInterfaces,
        EnablePacketFragment = s.EnablePacketFragment,
        SubscriptionViaProxy = s.SubscriptionViaProxy,
        AndroidBypassPackages = s.AndroidBypassPackages,
        AndroidBlockPackages = s.AndroidBlockPackages,
        DesktopDirectProcesses = s.DesktopDirectProcesses,
        DesktopBlockProcesses = s.DesktopBlockProcesses,
        LastGoodServerId = s.LastGoodServerId,
        AdaptiveSurviveEnabled = s.AdaptiveSurviveEnabled,
        AutoReconnectEnabled = s.AutoReconnectEnabled,
        LastSurviveTactic = s.LastSurviveTactic,
        StorageVersion = s.StorageVersion
    };

    private static void ProtectSensitive(AppSettings settings)
    {
        var p = AppServices.SecretProtector;
        settings.ShareAuthPass = SecretFieldProtector.ProtectField(p, settings.ShareAuthPass);
        settings.SubscriptionUrl = SecretFieldProtector.ProtectField(p, settings.SubscriptionUrl);
    }

    private static void UnprotectSensitive(AppSettings settings)
    {
        var p = AppServices.SecretProtector;
        settings.ShareAuthPass = SecretFieldProtector.UnprotectField(p, settings.ShareAuthPass);
        settings.SubscriptionUrl = SecretFieldProtector.UnprotectField(p, settings.SubscriptionUrl);
    }

    private static bool HasUnprotectedSecrets(AppSettings settings) =>
        IsLegacyPlaintext(settings.ShareAuthPass) || IsLegacyPlaintext(settings.SubscriptionUrl);

    private static bool IsLegacyPlaintext(string? value) =>
        !string.IsNullOrEmpty(value) && !value.StartsWith(AesGcmSecretProtector.Prefix, StringComparison.Ordinal);
}
