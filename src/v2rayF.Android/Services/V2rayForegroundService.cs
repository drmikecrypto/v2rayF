using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;

namespace v2rayF.Android.Services;

/// <summary>
/// Legacy FGS stub — VPN notification is owned by <see cref="V2rayVpnService"/> (id 1001).
/// Kept so old start intents do not crash; does not post a duplicate tile.
/// </summary>
[Service(Exported = false, ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeSpecialUse)]
public class V2rayForegroundService : Service
{
    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        StopSelf();
        return StartCommandResult.NotSticky;
    }

    public override IBinder? OnBind(Intent? intent) => null;
}
