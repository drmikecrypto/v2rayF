using System;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using v2rayF.Services;
using Application = Android.App.Application;

namespace v2rayF.Android.Services;

/// <summary>
/// Play-friendly updater: opens the GitHub release page in the browser.
/// Does not download or sideload APKs (no REQUEST_INSTALL_PACKAGES).
/// </summary>
public sealed class AndroidAppUpdater : IAppUpdater
{
    private const string ReleasesBaseUrl = "https://github.com/drmikecrypto/v2rayF/releases/tag/";

    public string ReleaseAssetFileName => "v2rayF-android-arm64.zip";

    public Task ApplyUpdateAsync(UpdateOffer offer, IProgress<string>? progress, CancellationToken cancellationToken = default) =>
        AndroidUiThread.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ctx = Application.Context ?? throw new InvalidOperationException("Application context missing.");
            var tag = string.IsNullOrWhiteSpace(offer.Tag) ? "latest" : offer.Tag.Trim();
            var url = tag.Equals("latest", StringComparison.OrdinalIgnoreCase)
                ? "https://github.com/drmikecrypto/v2rayF/releases/latest"
                : ReleasesBaseUrl + Uri.EscapeDataString(tag);

            progress?.Report("Opening GitHub release in browser…");
            var intent = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(url));
            intent.AddFlags(ActivityFlags.NewTask);
            ctx.StartActivity(intent);
            progress?.Report("Download the APK from GitHub Releases, then install it manually.");
            return Task.CompletedTask;
        });
}
