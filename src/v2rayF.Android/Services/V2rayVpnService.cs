using System;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
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

    private static ParcelFileDescriptor? _interface;
    private static TaskCompletionSource<int?>? _establishTcs;

    public static Task<int?> EstablishAsync(Context context, CancellationToken cancellationToken = default)
    {
        Disconnect(context);
        _establishTcs = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var intent = new Intent(context, typeof(V2rayVpnService));
        intent.SetAction(ActionEstablish);

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
            StartForeground(NotificationId, notification);

            TearDownInterface();

            var builder = new Builder(this);
            builder.SetSession("v2rayF");
            builder.SetMtu(1500);
            builder.AddAddress("172.19.0.1", 30);
            builder.AddRoute("0.0.0.0", 0);
            builder.AddDnsServer("8.8.8.8");
            builder.AddDnsServer("1.1.1.1");

            _interface = builder.Establish();
            _establishTcs?.TrySetResult(_interface?.DetachFd());

            var active = BuildNotification("Proxy connection active");
            StartForeground(NotificationId, active);
        }
        catch (Exception ex)
        {
            _establishTcs?.TrySetException(ex);
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

    public override IBinder? OnBind(Intent? intent) => null;

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
