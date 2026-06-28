using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using v2rayF.Services;

namespace v2rayF.Android.Services;

public sealed class AndroidCoreEnvironment : ICoreEnvironment
{
    private const string CoreLibraryName = "libxray.so";

    public async Task EnsureCoreAsync(CancellationToken cancellationToken = default)
    {
        var coresDir = GetCoresDirectory();
        Directory.CreateDirectory(coresDir);

        await ExtractAssetIfMissingAsync("geoip.dat", Path.Combine(coresDir, "geoip.dat"), cancellationToken).ConfigureAwait(false);
        await ExtractAssetIfMissingAsync("geosite.dat", Path.Combine(coresDir, "geosite.dat"), cancellationToken).ConfigureAwait(false);

        RemoveLegacyCoreExtract(coresDir);
    }

    public string GetCorePath()
    {
        var nativeLibDir = Application.Context!.ApplicationInfo!.NativeLibraryDir;
        if (string.IsNullOrEmpty(nativeLibDir))
            throw new InvalidOperationException("Native library directory is unavailable.");

        return Path.Combine(nativeLibDir, CoreLibraryName);
    }

    public string GetCoresDirectory() =>
        Path.Combine(Application.Context!.FilesDir!.AbsolutePath, "cores");

    public string GetDataDirectory()
    {
        var dir = Path.Combine(Application.Context!.FilesDir!.AbsolutePath, "v2rayF");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void RemoveLegacyCoreExtract(string coresDir)
    {
        var legacyPath = Path.Combine(coresDir, "xray");
        if (!File.Exists(legacyPath))
            return;

        try { File.Delete(legacyPath); }
        catch { /* best effort */ }
    }

    private static async Task ExtractAssetIfMissingAsync(string assetName, string destPath, CancellationToken cancellationToken)
    {
        if (File.Exists(destPath))
            return;

        await using var input = Application.Context!.Assets!.Open(assetName);
        await using var output = File.Create(destPath);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }
}
