using System;
using Android.App;
using Android.Content;
using Android.Net;
using Android.OS;
using Android.Provider;
using v2rayF.Models;

namespace v2rayF.Android.Services;

internal static class BatteryOptimizationHelper
{
    public const int RePromptDays = 7;

    /// <summary>
    /// Prompt for battery exemption when still optimizing.
    /// Marks BatteryOptimizationPromptShown only after grant; otherwise re-prompts after 7 days.
    /// </summary>
    public static bool TryPromptIfNeeded(Activity? activity, AppSettings settings)
    {
        if (activity is null)
            return false;

        if (Build.VERSION.SdkInt < BuildVersionCodes.M)
            return false;

        var pm = (PowerManager?)activity.GetSystemService(Context.PowerService);
        if (pm?.IsIgnoringBatteryOptimizations(activity.PackageName) == true)
        {
            settings.BatteryOptimizationPromptShown = true;
            return false;
        }

        // Already granted flag but OS says still optimizing — allow re-prompt on schedule.
        if (settings.BatteryOptimizationPromptShown)
            return false;

        if (!ShouldReprompt(settings))
            return false;

        settings.LastBatteryPromptUtc = DateTimeOffset.UtcNow.ToString("O");

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

    private static bool ShouldReprompt(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.LastBatteryPromptUtc))
            return true;

        if (!DateTimeOffset.TryParse(settings.LastBatteryPromptUtc, out var last))
            return true;

        return DateTimeOffset.UtcNow - last >= TimeSpan.FromDays(RePromptDays);
    }
}
