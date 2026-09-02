using Android.App;
using Android.Content;
using Android.Net;
using Android.OS;
using v2rayF.Models;

namespace v2rayF.Android.Services;

internal static class BatteryOptimizationHelper
{
    public static bool TryPromptIfNeeded(Activity activity, AppSettings settings)
    {
        if (settings.BatteryOptimizationPromptShown)
            return false;

        if (Build.VERSION.SdkInt < BuildVersionCodes.M)
            return false;

        var pm = (PowerManager?)activity.GetSystemService(Context.PowerService);
        if (pm?.IsIgnoringBatteryOptimizations(activity.PackageName) == true)
            return false;

        settings.BatteryOptimizationPromptShown = true;

        try
        {
            var intent = new Intent(Settings.ActionRequestIgnoreBatteryOptimizations);
            intent.SetData(Uri.Parse("package:" + activity.PackageName));
            activity.StartActivity(intent);
            return true;
        }
        catch
        {
            try
            {
                activity.StartActivity(new Intent(Settings.ActionIgnoreBatteryOptimizationSettings));
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
