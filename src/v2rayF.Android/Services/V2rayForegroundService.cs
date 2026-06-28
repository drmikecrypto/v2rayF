using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;

namespace v2rayF.Android.Services;

[Service(Exported = false, ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeSpecialUse)]
public class V2rayForegroundService : Service
{
    private const int NotificationId = 1001;
    private const string ChannelId = "v2rayF";

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        EnsureChannel();
        var notification = new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("v2rayF")
            .SetContentText("Proxy connection active")
            .SetSmallIcon(Resource.Drawable.Icon)
            .SetOngoing(true)
            .Build();

        StartForeground(NotificationId, notification);
        return StartCommandResult.Sticky;
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnDestroy()
    {
        StopForeground(true);
        base.OnDestroy();
    }

    private void EnsureChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
            return;

        var manager = (NotificationManager?)GetSystemService(NotificationService);
        var channel = new NotificationChannel(ChannelId, "v2rayF", NotificationImportance.Low);
        manager?.CreateNotificationChannel(channel);
    }
}
