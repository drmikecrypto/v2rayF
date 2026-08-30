using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Avalonia.Android;
using System;
using System.Threading.Tasks;
using v2rayF.Android.Services;
using v2rayF.Services;

namespace v2rayF.Android;

[Activity(
    Label = "v2rayF",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/Icon",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    public const int VpnRequestCode = 9001;
    public const int NotificationPermissionRequestCode = 9002;
    public const string PackageInstalledAction = "com.drmikecrypto.v2rayf.PACKAGE_INSTALLED";
    public const string PackageInstalledSessionExtra = "session_id";

    public static MainActivity? Instance { get; private set; }
    public static TaskCompletionSource<bool>? VpnPermissionTcs { get; set; }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        Instance = this;
        base.OnCreate(savedInstanceState);
        RequestNotificationPermissionIfNeeded();
        HandlePackageInstallResult(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Intent = intent;
        HandlePackageInstallResult(intent);
    }

    protected override void OnResume()
    {
        base.OnResume();
        AppServices.RefreshUpdateCheck?.Invoke();
        AppServices.OnSessionResumed?.Invoke();
    }

    private static void HandlePackageInstallResult(Intent? intent)
    {
        if (intent?.Action != PackageInstalledAction)
            return;

        var status = intent.GetIntExtra(PackageInstaller.ExtraStatus, int.MinValue);
        var message = intent.GetStringExtra(PackageInstaller.ExtraStatusMessage) ?? "";

        switch ((PackageInstallStatus)status)
        {
            case PackageInstallStatus.PendingUserAction:
            {
                var confirm = intent.GetParcelableExtra(Intent.ExtraIntent) as Intent;
                if (confirm is not null && Instance is not null)
                    Instance.StartActivity(confirm);
                AppServices.ReportStatus?.Invoke("Confirm the system Install prompt…");
                break;
            }
            case PackageInstallStatus.Success:
                AppServices.ReportStatus?.Invoke("Update installed — restart the app if the version label has not changed yet.");
                AppServices.RefreshUpdateCheck?.Invoke();
                break;
            case PackageInstallStatus.FailureAborted:
                AppServices.ReportStatus?.Invoke("Update cancelled.");
                break;
            case PackageInstallStatus.FailureConflict:
            case PackageInstallStatus.FailureIncompatible:
            case PackageInstallStatus.FailureInvalid:
            case PackageInstallStatus.FailureStorage:
            case PackageInstallStatus.Failure:
            default:
                if (status == int.MinValue)
                    break;
                var detail = string.IsNullOrWhiteSpace(message) ? ((PackageInstallStatus)status).ToString() : message;
                AppServices.ReportStatus?.Invoke(
                    $"Update install failed: {detail}. If this repeats, uninstall and install the latest APK from GitHub Releases once.");
                break;
        }
    }

    private void RequestNotificationPermissionIfNeeded()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu)
            return;

        if (ContextCompat.CheckSelfPermission(this, Manifest.Permission.PostNotifications) == Permission.Granted)
            return;

        ActivityCompat.RequestPermissions(this, [Manifest.Permission.PostNotifications], NotificationPermissionRequestCode);
    }

    public event EventHandler<PermissionEventArgs>? PermissionResult;

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        PermissionResult?.Invoke(this, new PermissionEventArgs(requestCode, grantResults));
    }

    protected override void OnDestroy()
    {
        // Closing the activity (back / swipe finish) must tear down VPN + clear FGS notification.
        // Home / background keeps the sticky VPN service running.
        if (IsFinishing)
        {
            var disconnect = AppServices.EmergencyDisconnectAsync;
            if (disconnect is not null)
            {
                try
                {
                    disconnect().GetAwaiter().GetResult();
                }
                catch
                {
                    // Best effort teardown on exit.
                }
            }
            else
            {
                try
                {
                    V2rayVpnService.Disconnect(ApplicationContext);
                }
                catch
                {
                    // Best effort.
                }
            }
        }

        if (Instance == this)
            Instance = null;
        base.OnDestroy();
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == VpnRequestCode)
            VpnPermissionTcs?.TrySetResult(resultCode == Result.Ok);
    }

    public sealed class PermissionEventArgs(int requestCode, Permission[] grantResults) : EventArgs
    {
        public int RequestCode { get; } = requestCode;
        public Permission[] GrantResults { get; } = grantResults;
    }
}
