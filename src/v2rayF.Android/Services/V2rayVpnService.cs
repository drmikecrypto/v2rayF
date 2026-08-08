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
    private static TaskCompletionSource<int?>? _establishTcs;

    public static Task<int?> EstablishAsync(
        Context context,
        IReadOnlyList<string>? bypassPackages = null,
        bool blockIpv6 = true,
        CancellationToken cancellationToken = default)
    {
        Disconnect(context);
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
        if (string.Equals(intent?.Action, ActionDisconnect, StringComparison.Ordinal))
        {
            TearDownInterface();
            StopForeground(true);
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        try
        {
            EnsureChannel();
            var notification = BuildNotification("Establishing VPN…");
            StartVpnForeground(notification);

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
                    // Capture IPv6 so it cannot leak around the tunnel.
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
            if (fd < 0)
            {
                AndroidPlatformIntegration.ReportEstablishError("VPN file descriptor is invalid.");
                _establishTcs?.TrySetResult(null);
                StopForeground(true);
                StopSelf();
                return StartCommandResult.NotSticky;
            }

            _establishTcs?.TrySetResult(fd);

            var active = BuildNotification("Proxy connection active");
            StartVpnForeground(active);
        }
        catch (Exception ex)
        {
            AndroidPlatformIntegration.ReportEstablishError(ex.Message);
            _establishTcs?.TrySetResult(null);
            StopForeground(true);
            StopSelf();
        }

        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
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

        var stopIntent = new Intent(context, typeof(V2rayVpnService));
        context.StopService(stopIntent);

        var disconnectIntent = new Intent(context, typeof(V2rayVpnService));
        disconnectIntent.SetAction(ActionDisconnect);
        context.StartService(disconnectIntent);
    }

    private static void TearDownInterface()
    {
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
            .Build();
}
