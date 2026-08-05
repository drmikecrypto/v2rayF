using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.Content;
using v2rayF.Services;
using Application = Android.App.Application;
using Signature = Android.Content.PM.Signature;

namespace v2rayF.Android.Services;

public sealed class AndroidAppUpdater : IAppUpdater
{
    public string ReleaseAssetFileName => "v2rayF-android-arm64.zip";

    public Task ApplyUpdateAsync(UpdateOffer offer, IProgress<string>? progress, CancellationToken cancellationToken = default) =>
        AndroidUiThread.InvokeAsync(() => ApplyOnUiThreadAsync(offer, progress, cancellationToken));

    private static async Task ApplyOnUiThreadAsync(
        UpdateOffer offer,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var ctx = Application.Context ?? throw new InvalidOperationException("Application context missing.");
        var cacheRoot = Path.Combine(ctx.CacheDir!.AbsolutePath, "updates");
        var workDir = Path.Combine(cacheRoot, offer.Version);
        Directory.CreateDirectory(workDir);

        var zipPath = Path.Combine(workDir, offer.AssetFileName);
        await UpdateDownloadHelper.DownloadAsync(offer.DownloadUrl, zipPath, progress, cancellationToken)
            .ConfigureAwait(true);

        var extractDir = Path.Combine(workDir, "files");
        UpdateDownloadHelper.ExtractZip(zipPath, extractDir, progress);

        var apk = Directory.EnumerateFiles(extractDir, "*.apk", SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new InvalidOperationException("Update package did not contain an APK.");

        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var pm = ctx.PackageManager!;
            if (!pm.CanRequestPackageInstalls())
            {
                progress?.Report("Allow installs, then tap Update again.");
                var settings = new Intent(global::Android.Provider.Settings.ActionManageUnknownAppSources,
                    global::Android.Net.Uri.Parse("package:" + ctx.PackageName));
                settings.AddFlags(ActivityFlags.NewTask);
                ctx.StartActivity(settings);
                throw new InvalidOperationException("Install permission required.");
            }
        }

        EnsureCompatibleSignature(ctx, apk);

        progress?.Report("Installing update…");
        try
        {
            CommitViaPackageInstaller(ctx, apk);
            progress?.Report("Confirm the system Install prompt…");
        }
        catch
        {
            progress?.Report("Opening system installer…");
            OpenSystemInstaller(ctx, apk);
            progress?.Report("Confirm the system Install prompt…");
        }
    }

    private static void EnsureCompatibleSignature(Context ctx, string apkPath)
    {
        var pm = ctx.PackageManager!;
        var installed = GetInstalledSignatureFingerprints(pm, ctx.PackageName!);
        var incoming = GetApkSignatureFingerprints(pm, apkPath);
        if (installed.Count == 0 || incoming.Count == 0)
            return;

        if (!incoming.Overlaps(installed))
        {
            throw new InvalidOperationException(
                "Update signature differs from the installed app (older CI builds used a one-off debug key). Uninstall v2rayF, then install the latest APK from GitHub Releases once — later Updates will work.");
        }
    }

    private static HashSet<string> GetInstalledSignatureFingerprints(PackageManager pm, string packageName)
    {
        try
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.P)
            {
                var info = pm.GetPackageInfo(packageName, PackageInfoFlags.SigningCertificates);
                return ToFingerprints(info?.SigningInfo?.GetApkContentsSigners());
            }

#pragma warning disable CS0618
            var legacy = pm.GetPackageInfo(packageName, PackageInfoFlags.Signatures);
            return ToFingerprints(legacy?.Signatures);
#pragma warning restore CS0618
        }
        catch
        {
            return [];
        }
    }

    private static HashSet<string> GetApkSignatureFingerprints(PackageManager pm, string apkPath)
    {
        try
        {
            PackageInfo? info;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.P)
                info = pm.GetPackageArchiveInfo(apkPath, PackageInfoFlags.SigningCertificates);
            else
#pragma warning disable CS0618
                info = pm.GetPackageArchiveInfo(apkPath, PackageInfoFlags.Signatures);
#pragma warning restore CS0618

            if (info?.ApplicationInfo is not null)
            {
                info.ApplicationInfo.SourceDir = apkPath;
                info.ApplicationInfo.PublicSourceDir = apkPath;
            }

            if (Build.VERSION.SdkInt >= BuildVersionCodes.P)
                return ToFingerprints(info?.SigningInfo?.GetApkContentsSigners());

#pragma warning disable CS0618
            return ToFingerprints(info?.Signatures);
#pragma warning restore CS0618
        }
        catch
        {
            return [];
        }
    }

    private static HashSet<string> ToFingerprints(IEnumerable<Signature>? signatures)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (signatures is null)
            return set;

        foreach (var signature in signatures)
        {
            var bytes = signature.ToByteArray();
            if (bytes is null || bytes.Length == 0)
                continue;
            set.Add(Convert.ToHexString(SHA256.HashData(bytes)));
        }

        return set;
    }

    private static void CommitViaPackageInstaller(Context ctx, string apkPath)
    {
        var activity = MainActivity.Instance
            ?? throw new InvalidOperationException("Main activity unavailable for install confirmation.");

        var installer = ctx.PackageManager!.PackageInstaller;
        var sessionParams = new PackageInstaller.SessionParams(PackageInstallMode.FullInstall);
        var sessionId = installer.CreateSession(sessionParams);

        using (var session = installer.OpenSession(sessionId))
        {
            using (var input = File.OpenRead(apkPath))
            using (var output = session.OpenWrite("base.apk", 0, input.Length))
            {
                input.CopyTo(output);
                session.Fsync(output);
            }

            var intent = new Intent(activity, typeof(MainActivity));
            intent.SetAction(MainActivity.PackageInstalledAction);
            intent.PutExtra(MainActivity.PackageInstalledSessionExtra, sessionId);

            var flags = PendingIntentFlags.UpdateCurrent;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
                flags |= PendingIntentFlags.Mutable;

            var pending = PendingIntent.GetActivity(activity, sessionId, intent, flags)
                ?? throw new InvalidOperationException("Could not create install PendingIntent.");

            session.Commit(pending.IntentSender);
        }
    }

    private static void OpenSystemInstaller(Context ctx, string apkPath)
    {
        var apkFile = new Java.IO.File(apkPath);
        var authority = ctx.PackageName + ".fileprovider";
        var uri = FileProvider.GetUriForFile(ctx, authority, apkFile);

        var install = new Intent(Intent.ActionInstallPackage);
        install.SetData(uri);
        install.AddFlags(ActivityFlags.GrantReadUriPermission);
        if (MainActivity.Instance is null)
            install.AddFlags(ActivityFlags.NewTask);

        GrantUriToResolvers(ctx, install, uri);

        if (install.ResolveActivity(ctx.PackageManager!) is null)
        {
            install = new Intent(Intent.ActionView);
            install.SetDataAndType(uri, "application/vnd.android.package-archive");
            install.AddFlags(ActivityFlags.GrantReadUriPermission);
            if (MainActivity.Instance is null)
                install.AddFlags(ActivityFlags.NewTask);
            GrantUriToResolvers(ctx, install, uri);
        }

        (MainActivity.Instance as Context ?? ctx).StartActivity(install);
    }

    private static void GrantUriToResolvers(Context ctx, Intent intent, global::Android.Net.Uri uri)
    {
        var resolvers = ctx.PackageManager!.QueryIntentActivities(intent, PackageInfoFlags.MatchDefaultOnly);
        foreach (var resolve in resolvers)
        {
            var packageName = resolve.ActivityInfo?.PackageName;
            if (!string.IsNullOrEmpty(packageName))
                ctx.GrantUriPermission(packageName, uri, ActivityFlags.GrantReadUriPermission);
        }
    }
}
