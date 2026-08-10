using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Avalonia.Android;
using System;
using System.IO;
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
    public const int CameraPermissionRequestCode = 9003;
    public const int QrCaptureRequestCode = 9004;
    public const string PackageInstalledAction = "com.drmikecrypto.v2rayf.PACKAGE_INSTALLED";
    public const string PackageInstalledSessionExtra = "session_id";

    public static MainActivity? Instance { get; private set; }
    public static TaskCompletionSource<bool>? VpnPermissionTcs { get; set; }

    private TaskCompletionSource<string?>? _qrCaptureTcs;
    private Java.IO.File? _qrCaptureFile;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        Instance = this;
        base.OnCreate(savedInstanceState);
        RequestNotificationPermissionIfNeeded();
        AppServices.CaptureQrTextAsync = CaptureQrTextAsync;
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

    private async Task<string?> CaptureQrTextAsync()
    {
        if (ContextCompat.CheckSelfPermission(this, Manifest.Permission.Camera) != Permission.Granted)
        {
            var permTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Handler(object? s, PermissionEventArgs e)
            {
                if (e.RequestCode != CameraPermissionRequestCode)
                    return;
                PermissionResult -= Handler;
                permTcs.TrySetResult(e.GrantResults is { Length: > 0 } && e.GrantResults[0] == Permission.Granted);
            }

            PermissionResult += Handler;
            ActivityCompat.RequestPermissions(this, [Manifest.Permission.Camera], CameraPermissionRequestCode);
            if (!await permTcs.Task.ConfigureAwait(true))
                return null;
        }

        _qrCaptureTcs?.TrySetResult(null);
        _qrCaptureTcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var cache = CacheDir ?? FilesDir;
        _qrCaptureFile = new Java.IO.File(cache, $"qr_{DateTime.UtcNow.Ticks}.jpg");
        var uri = FileProvider.GetUriForFile(this, $"{PackageName}.fileprovider", _qrCaptureFile);

        var takePicture = new Intent(MediaStore.ActionImageCapture);
        takePicture.PutExtra(MediaStore.ExtraOutput, uri);
        takePicture.AddFlags(ActivityFlags.GrantWriteUriPermission | ActivityFlags.GrantReadUriPermission);

        try
        {
            StartActivityForResult(takePicture, QrCaptureRequestCode);
        }
        catch (Exception)
        {
            _qrCaptureTcs.TrySetResult(null);
            return null;
        }

        return await _qrCaptureTcs.Task.ConfigureAwait(true);
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
            AppServices.CaptureQrTextAsync = null;
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
        {
            VpnPermissionTcs?.TrySetResult(resultCode == Result.Ok);
            return;
        }

        if (requestCode != QrCaptureRequestCode)
            return;

        try
        {
            if (resultCode != Result.Ok || _qrCaptureFile is null || !_qrCaptureFile.Exists())
            {
                _qrCaptureTcs?.TrySetResult(null);
                return;
            }

            var bytes = File.ReadAllBytes(_qrCaptureFile.AbsolutePath);
            var text = QrCodeDecoder.DecodeFromImageBytes(bytes);
            _qrCaptureTcs?.TrySetResult(text);
        }
        catch
        {
            _qrCaptureTcs?.TrySetResult(null);
        }
        finally
        {
            try
            {
                _qrCaptureFile?.Delete();
            }
            catch
            {
                // Best effort.
            }

            _qrCaptureFile = null;
        }
    }

    public sealed class PermissionEventArgs(int requestCode, Permission[] grantResults) : EventArgs
    {
        public int RequestCode { get; } = requestCode;
        public Permission[] GrantResults { get; } = grantResults;
    }
}
