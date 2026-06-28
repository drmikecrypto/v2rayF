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
        base.OnCreate();
        _ = WarmupAsync();
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
