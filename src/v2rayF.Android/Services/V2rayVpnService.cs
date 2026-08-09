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

    private CancellationTokenSource? _trafficCts;
    private readonly TrafficStatsService _trafficStats = new(AppServices.CoreEnvironment);

    public static Task<int?> EstablishAsync(
        Context context,
        IReadOnlyList<string>? bypassPackages = null,
        bool blockIpv6 = true,
        CancellationToken cancellationToken = default)
    {
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
                StopForeground(true);
                StopSelf();
                return StartCommandResult.NotSticky;
            }

            TearDownInterface();

            var blockIpv6 = intent?.GetBooleanExtra(ExtraBlockIpv6, true) ?? true;
            var bypass = intent?.GetStringArrayListExtra(ExtraBypassPackages);

            var builder = new Builder(this);
            builder.SetSession("v2rayF");
            builder.SetMtu(1280);
            builder.AddAddress("172.19.0.1", 30);
            builder.AddRoute("0.0.0.0", 0);
            builder.AddDnsServer("172.19.0.1");

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
                StopForeground(true);
                StopSelf();
                return StartCommandResult.NotSticky;
            }

            var fd = _interface.DetachFd();
            _interface = null;
            if (fd < 0)
            {
                AndroidPlatformIntegration.ReportEstablishError("VPN file descriptor is invalid.");
                _establishTcs?.TrySetResult(null);
                StopForeground(true);
                StopSelf();
                return StartCommandResult.NotSticky;
            }

            _tunFd = fd;
            _establishTcs?.TrySetResult(fd);

            StartVpnForeground(BuildNotification(TrafficStatsService.FormatNotificationLine(0, 0)));
            StartTrafficNotificationUpdates();
        }
        catch (Exception ex)
        {
            StopTrafficNotificationUpdates();
            AndroidPlatformIntegration.ReportEstablishError(ex.Message);
            _establishTcs?.TrySetResult(null);
            StopForeground(true);
            StopSelf();
        }

        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        StopTrafficNotificationUpdates();
        TearDownInterface();
        StopForeground(true);
        base.OnDestroy();
    }

    public override IBinder? OnBind(Intent? intent) => base.OnBind(intent);

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

    private static void TearDownInterface()
    {
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
        _trafficCts = new CancellationTokenSource();
        var token = _trafficCts.Token;
        _ = Task.Run(() => TrafficNotificationLoopAsync(token), CancellationToken.None);
    }

    private void StopTrafficNotificationUpdates()
    {
        try
        {
            _trafficCts?.Cancel();
            _trafficCts?.Dispose();
        }
        catch
        {
            // Best effort.
        }

        _trafficCts = null;
    }

    private async Task TrafficNotificationLoopAsync(CancellationToken cancellationToken)
    {
        var manager = NotificationManagerCompat.From(this);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                var snap = await _trafficStats.QueryAsync(cancellationToken).ConfigureAwait(false);
                var text = snap is { } traffic
                    ? TrafficStatsService.FormatNotificationLine(traffic.UplinkBytes, traffic.DownlinkBytes)
                    : TrafficStatsService.FormatNotificationLine(0, 0);

                manager.Notify(NotificationId, BuildNotification(text));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // Keep updating when possible.
            }
        }
    }
}
