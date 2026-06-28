using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using v2rayF.Desktop.Services;
using v2rayF.Services;

namespace v2rayF.Desktop;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Trace.WriteLine($"Unhandled: {e.ExceptionObject}");

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Trace.WriteLine($"Unobserved task: {e.Exception}");
            e.SetObserved();
        };

        AppServices.CoreEnvironment = new DesktopCoreEnvironment();
        AppServices.Platform = new DesktopPlatformIntegration();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<v2rayF.App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
