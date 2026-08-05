using System;
using System.Threading.Tasks;
using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using v2rayF.Android.Services;
using v2rayF.Services;

namespace v2rayF.Android;

[Application]
public class V2rayApplication : AvaloniaAndroidApplication<v2rayF.App>
{
    public V2rayApplication(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    public override void OnCreate()
    {
        AppServices.CoreEnvironment = new AndroidCoreEnvironment();
        AppServices.Platform = new AndroidPlatformIntegration();
        AppServices.CoreProcessHost = new AndroidJavaCoreProcessHost();
        AppServices.KillSwitch = new AndroidKillSwitch();
        AppServices.Updater = new AndroidAppUpdater();
        TryOverrideAppVersion();
        AndroidEnvironment.UnhandledExceptionRaiser += (_, e) =>
            global::Android.Util.Log.Error("v2rayF", e.Exception?.ToString() ?? "Unhandled exception");
        base.OnCreate();
        _ = WarmupAsync();
    }

    private void TryOverrideAppVersion()
    {
        try
        {
            var info = PackageManager?.GetPackageInfo(PackageName!, 0);
            if (!string.IsNullOrWhiteSpace(info?.VersionName))
                AppVersion.OverrideCurrent(info.VersionName);
        }
        catch
        {
            // Fall back to assembly version.
        }
    }

    private static async Task WarmupAsync()
    {
        try
        {
            await AppServices.CoreEnvironment.EnsureCoreAsync().ConfigureAwait(false);
        }
        catch
        {
            // Warmup is best-effort; connect will retry extraction.
        }
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder).WithInterFont();
}
