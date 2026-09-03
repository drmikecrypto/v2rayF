using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.OS;
using AndroidX.Core.App;
using v2rayF.Services;

namespace v2rayF.Android.Services;

[Service(Permission = "android.permission.BIND_VPN_SERVICE", Exported = false)]
[IntentFilter(new[] { "android.net.VpnService" })]
public class V2rayVpnService : VpnService
{
    private const int NotificationId = 1001;
    private const string ChannelId = "v2rayF";
    private const string ActionEstablish = "com.drmikecrypto.v2rayf.action.ESTABLISH";
    private const string ActionDisconnect = "com.drmikecrypto.v2rayf.action.DISCONNECT";
    private const string ExtraBlockIpv6 = "block_ipv6";
    private const string ExtraBypassPackages = "bypass_packages";

    private static ParcelFileDescriptor? _interface;
    private static int _tunFd = -1;
    private static TaskCompletionSource<int?>? _establishTcs;
    private static ConnectivityManager? _connectivityManager;
    private static VpnNetworkCallback? _networkCallback;
    private static UnderlyingNetworkCallback? _underlyingCallback;
    private static string? _establishConfigHash;
    private static DateTimeOffset _lastNetworkRecoveryUtc = DateTimeOffset.MinValue;
    private const int NetworkRecoveryThrottleMs = 5000;

    private bool _subscribedTraffic;

    public static int? GetActiveTunFd() => _tunFd >= 0 ? _tunFd : null;

    public static bool NeedsReestablish(IReadOnlyList<string>? bypassPackages, bool blockIpv6) =>
        _tunFd >= 0 &&
        !string.Equals(_establishConfigHash, ComputeEstablishHash(bypassPackages, blockIpv6), StringComparison.Ordinal);

    private static string ComputeEstablishHash(IReadOnlyList<string>? bypassPackages, bool blockIpv6)
    {
        var parts = bypassPackages?
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
        return $"{blockIpv6}|{string.Join("\0", parts)}";
    }

    public static Task<int?> EstablishAsync(
        Context context,
        IReadOnlyList<string>? bypassPackages = null,
        bool blockIpv6 = true,
        CancellationToken cancellationToken = default)
    {
        var hash = ComputeEstablishHash(bypassPackages, blockIpv6);
        if (_tunFd >= 0 && string.Equals(_establishConfigHash, hash, StringComparison.Ordinal))
            return Task.FromResult<int?>(_tunFd);

        // In-process teardown only — never StartService(DISCONNECT) before ESTABLISH
        // (that races FGS and can force-close with RemoteServiceException).
        _establishTcs?.TrySetResult(null);
        TearDownInterface();

        _establishTcs = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var intent = new Intent(context, typeof(V2rayVpnService));
        intent.SetAction(ActionEstablish);
        intent.PutExtra(ExtraBlockIpv6, blockIpv6);
        if (bypassPackages is { Count: > 0 })
            intent.PutStringArrayListExtra(ExtraBypassPackages, bypassPackages.ToList());

        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            context.StartForegroundService(intent);
        else
            context.StartService(intent);

        return WaitEstablishAsync(cancellationToken);
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var isDisconnect = string.Equals(intent?.Action, ActionDisconnect, StringComparison.Ordinal);

        try
        {
            EnsureChannel();
            // Always promote to foreground first (Android 8+ FGS contract).
            StartVpnForeground(BuildNotification(isDisconnect ? "Stopping VPN…" : "Establishing VPN…"));

            if (isDisconnect)
            {
                StopTrafficNotificationUpdates();
                TearDownInterface();
                ClearNotification();
                StopSelf();
                return StartCommandResult.NotSticky;
            }

            TearDownInterface();

            var blockIpv6 = intent?.GetBooleanExtra(ExtraBlockIpv6, true) ?? true;
            var bypass = intent?.GetStringArrayListExtra(ExtraBypassPackages);

            var builder = new Builder(this);
            builder.SetSession("v2rayF");
            builder.SetMtu(XrayConfigBuilder.AndroidTunMtu);
            builder.AddAddress("172.19.0.1", 30);
            builder.AddRoute("0.0.0.0", 0);
            // Tunnel DNS so Xray UseIPv4 applies (WhatsApp / other raw-socket apps).
            builder.AddDnsServer("172.19.0.1");

            // Chromium (Play Store / Translate / IG feed) needs VPN HTTP proxy.
            // MQTT/realtime hosts bypass CONNECT → TUN (Instagram Direct); see GetMetaHttpProxyExclusions.
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
            {
                try
                {
                    builder.SetHttpProxy(ProxyInfo.BuildDirectProxy(
                        "127.0.0.1",
                        XrayConfigBuilder.HttpPort,
                        SingBoxConfigBuilder.GetMetaHttpProxyExclusions()));
                }
                catch (Exception ex)
                {
                    // OEM reject — Chromium apps may fail until user disconnects.
                    AndroidPlatformIntegration.ReportHttpProxyWarning(
                        $"VPN HTTP proxy unavailable — Chromium apps may fail ({ex.Message})");
                }
            }

            try
            {
                // Keep app + libxray outbound off the tunnel (avoids routing loops).
                builder.AddDisallowedApplication(PackageName!);
            }
            catch
            {
                // Ignore if package lookup fails on exotic OEMs.
            }

            if (blockIpv6)
            {
                try
                {
                    builder.AddAddress("fd00:1:fd00:1:fd00:1:fd00:1", 126);
                    builder.AddRoute("::", 0);
                }
                catch
                {
                    // Some OEMs reject IPv6 VPN addresses; IPv4 catch-all still applies.
                }
            }

            if (bypass is not null)
            {
                foreach (var packageName in bypass)
                {
                    if (string.IsNullOrWhiteSpace(packageName))
                        continue;
                    try
                    {
                        builder.AddDisallowedApplication(packageName.Trim());
                    }
                    catch
                    {
                        // Package may not be installed.
                    }
                }
            }

            if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
                builder.SetBlocking(false);

            _interface = builder.Establish();
            if (_interface is null)
            {
                AndroidPlatformIntegration.ReportEstablishError("VPN interface could not be created.");
                _establishTcs?.TrySetResult(null);
                ClearNotification();
                StopSelf();
                return StartCommandResult.NotSticky;
            }

            var fd = _interface.DetachFd();
            _interface = null;
            if (fd < 0)
            {
                AndroidPlatformIntegration.ReportEstablishError("VPN file descriptor is invalid.");
                _establishTcs?.TrySetResult(null);
                ClearNotification();
                StopSelf();
                return StartCommandResult.NotSticky;
            }

            _tunFd = fd;
            IReadOnlyList<string>? bypassList = bypass is null ? null : bypass.ToList();
            _establishConfigHash = ComputeEstablishHash(bypassList, blockIpv6);
            _establishTcs?.TrySetResult(fd);

            // Session-survival callbacks are best-effort — never tear down a good TUN.
            try
            {
                RegisterNetworkCallback(this);
            }
            catch
            {
                // Missing ACCESS_NETWORK_STATE (or OEM quirks) must not fail Connect.
            }

            StartVpnForeground(BuildNotification(TrafficStatsService.FormatNotificationLine(0, 0, null)));
            StartTrafficNotificationUpdates();
        }
        catch (Exception ex)
        {
            StopTrafficNotificationUpdates();
            AndroidPlatformIntegration.ReportEstablishError(ex.Message);
            _establishTcs?.TrySetResult(null);
            ClearNotification();
            StopSelf();
        }

        return StartCommandResult.Sticky;
    }

    public override void OnRevoke()
    {
        try
        {
            StopTrafficNotificationUpdates();
            TearDownInterface();
            ClearNotification();
        }
        catch
        {
            // Best effort.
        }

        try
        {
            AppServices.OnVpnRevoked?.Invoke();
        }
        catch
        {
            // Best effort.
        }

        try
        {
            StopSelf();
        }
        catch
        {
            // Best effort.
        }

        base.OnRevoke();
    }

    public override void OnDestroy()
    {
        StopTrafficNotificationUpdates();
        TearDownInterface();
        ClearNotification();
        base.OnDestroy();
    }

    public override IBinder? OnBind(Intent? intent) => base.OnBind(intent);

    /// <summary>Re-validate the VPN after Xray is reading the TUN fd (captive portal / Chrome).</summary>
    public static void ReportVpnReady()
    {
        try
        {
            var context = Application.Context;
            if (context is null)
                return;
            if (context.GetSystemService(Context.ConnectivityService) is not ConnectivityManager cm)
                return;
            var networks = cm.GetAllNetworks();
            if (networks is null)
                return;
            foreach (var network in networks)
            {
                var caps = cm.GetNetworkCapabilities(network);
                if (caps is null || !caps.HasTransport(TransportType.Vpn))
                    continue;
                cm.ReportNetworkConnectivity(network, true);
            }
        }
        catch
        {
            // Best effort — unvalidated VPN is the pre-fix behavior.
        }
    }

    public static void Disconnect(Context? context = null)
    {
        _establishTcs?.TrySetResult(null);
        TearDownInterface();

        if (context is null)
            return;

        var disconnectIntent = new Intent(context, typeof(V2rayVpnService));
        disconnectIntent.SetAction(ActionDisconnect);
        // Use FGS start so OnStartCommand can satisfy startForeground before StopSelf.
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            context.StartForegroundService(disconnectIntent);
        else
            context.StartService(disconnectIntent);
    }

    private void ClearNotification()
    {
        try
        {
            StopForeground(StopForegroundFlags.Remove);
        }
        catch
        {
            try
            {
                StopForeground(true);
            }
            catch
            {
                // Best effort.
            }
        }

        try
        {
            NotificationManagerCompat.From(this).Cancel(NotificationId);
        }
        catch
        {
            // Best effort.
        }
    }

    private static void TearDownInterface()
    {
        UnregisterNetworkCallback();

        if (_tunFd >= 0)
        {
            try
            {
                AndroidJavaCoreProcessHost.CloseFd(_tunFd);
            }
            catch
            {
                // Best effort.
            }

            _tunFd = -1;
        }

        _establishConfigHash = null;

        try
        {
            _interface?.Close();
            _interface?.Dispose();
        }
        catch
        {
            // Best effort teardown.
        }
        finally
        {
            _interface = null;
        }
    }

    private static void RegisterNetworkCallback(Context context)
    {
        try
        {
            UnregisterNetworkCallback();
            if (context.GetSystemService(Context.ConnectivityService) is not ConnectivityManager cm)
                return;

            _connectivityManager = cm;
            _networkCallback = new VpnNetworkCallback();
            _underlyingCallback = new UnderlyingNetworkCallback();

            cm.RegisterDefaultNetworkCallback(_networkCallback);

            var internetRequest = new NetworkRequest.Builder()
                .AddCapability(NetCapability.Internet)
                .AddTransportType(TransportType.Wifi)
                .AddTransportType(TransportType.Cellular)
                .AddTransportType(TransportType.Ethernet)
                .Build();
            cm.RegisterNetworkCallback(internetRequest, _underlyingCallback);
        }
        catch
        {
            try
            {
                UnregisterNetworkCallback();
            }
            catch
            {
                // Best effort cleanup.
            }
        }
    }

    private static void UnregisterNetworkCallback()
    {
        if (_connectivityManager is null)
            return;

        if (_networkCallback is not null)
        {
            try
            {
                _connectivityManager.UnregisterNetworkCallback(_networkCallback);
            }
            catch
            {
                // Best effort.
            }
        }

        if (_underlyingCallback is not null)
        {
            try
            {
                _connectivityManager.UnregisterNetworkCallback(_underlyingCallback);
            }
            catch
            {
                // Best effort.
            }
        }

        _connectivityManager = null;
        _networkCallback = null;
        _underlyingCallback = null;
    }

    private static void NotifySessionRecovery()
    {
        var now = DateTimeOffset.UtcNow;
        if ((now - _lastNetworkRecoveryUtc).TotalMilliseconds < NetworkRecoveryThrottleMs)
            return;

        _lastNetworkRecoveryUtc = now;
        ReportVpnReady();
        AppServices.OnSessionResumed?.Invoke();
    }

    private sealed class VpnNetworkCallback : ConnectivityManager.NetworkCallback
    {
        public override void OnCapabilitiesChanged(Network network, NetworkCapabilities networkCapabilities)
        {
            if (networkCapabilities?.HasTransport(TransportType.Vpn) == true)
                NotifySessionRecovery();
        }
    }

    private sealed class UnderlyingNetworkCallback : ConnectivityManager.NetworkCallback
    {
        public override void OnAvailable(Network network) => NotifySessionRecovery();

        public override void OnLost(Network network) => NotifySessionRecovery();
    }

    private static async Task<int?> WaitEstablishAsync(CancellationToken cancellationToken)
    {
        if (_establishTcs is null)
            return null;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            return await _establishTcs.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (System.OperationCanceledException)
        {
            _establishTcs?.TrySetResult(null);
            return null;
        }
        catch (Exception ex)
        {
            AndroidPlatformIntegration.ReportEstablishError(ex.Message);
            return null;
        }
    }

    private void StartVpnForeground(Notification notification)
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.UpsideDownCake)
            StartForeground(NotificationId, notification, ForegroundService.TypeSpecialUse);
        else
            StartForeground(NotificationId, notification);
    }

    private void EnsureChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
            return;

        var manager = (NotificationManager?)GetSystemService(NotificationService);
        var channel = new NotificationChannel(ChannelId, "v2rayF", NotificationImportance.Low);
        manager?.CreateNotificationChannel(channel);
    }

    private Notification BuildNotification(string text) =>
        new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("v2rayF")
            .SetContentText(text)
            .SetSmallIcon(Resource.Drawable.Icon)
            .SetOngoing(true)
            .SetOnlyAlertOnce(true)
            .Build();

    private void StartTrafficNotificationUpdates()
    {
        StopTrafficNotificationUpdates();
        // Prefer shared hub (UI may already be polling) — one statsquery process for all consumers.
        TrafficStatsHub.Shared.Updated += OnHubUpdated;
        TrafficStatsHub.Shared.Subscribe();
        _subscribedTraffic = true;
        AppServices.OnLiveTraffic = OnLiveTrafficFromUi;
    }

    private void StopTrafficNotificationUpdates()
    {
        if (_subscribedTraffic)
        {
            TrafficStatsHub.Shared.Updated -= OnHubUpdated;
            TrafficStatsHub.Shared.Unsubscribe();
            _subscribedTraffic = false;
        }

        if (ReferenceEquals(AppServices.OnLiveTraffic, (Action<long, long, int?>)OnLiveTrafficFromUi))
            AppServices.OnLiveTraffic = null;
    }

    private void OnHubUpdated(TrafficStatsHub.LiveTraffic traffic) =>
        UpdateNotificationText(TrafficStatsService.FormatNotificationLine(
            traffic.UplinkBps,
            traffic.DownlinkBps,
            TrafficStatsHub.Shared.ConnectedPingMs));

    private void OnLiveTrafficFromUi(long upBps, long downBps, int? pingMs) =>
        UpdateNotificationText(TrafficStatsService.FormatNotificationLine(upBps, downBps, pingMs));

    private void UpdateNotificationText(string text)
    {
        try
        {
            NotificationManagerCompat.From(this).Notify(NotificationId, BuildNotification(text));
        }
        catch
        {
            // Best effort.
        }
    }
}
