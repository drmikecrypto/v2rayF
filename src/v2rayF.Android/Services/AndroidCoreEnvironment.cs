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
    private const string SingBoxLibraryName = "libsingbox.so";
    private readonly SemaphoreSlim _ensureLock = new(1, 1);
    private volatile bool _ensureComplete;

    public async Task EnsureCoreAsync(CancellationToken cancellationToken = default)
    {
        if (_ensureComplete)
            return;

        await _ensureLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_ensureComplete)
                return;

            var coresDir = GetCoresDirectory();
            Directory.CreateDirectory(coresDir);

            await Task.WhenAll(
                ExtractAssetIfMissingAsync("geoip.dat", Path.Combine(coresDir, "geoip.dat"), cancellationToken),
                ExtractAssetIfMissingAsync("geosite.dat", Path.Combine(coresDir, "geosite.dat"), cancellationToken))
                .ConfigureAwait(false);

            RemoveLegacyCoreExtract(coresDir);
            _ensureComplete = true;
        }
        finally
        {
            _ensureLock.Release();
        }
    }

    public string GetCorePath() => ResolveNativeLibrary(CoreLibraryName);

    /// <summary>
    /// sing-box is packaged as libsingbox.so (same SELinux exec rules as libxray.so).
    /// files/cores/sing-box is not executable on Android 10+.
    /// </summary>
    public string GetSingBoxPath() => ResolveNativeLibrary(SingBoxLibraryName);

    public string GetCoresDirectory() =>
        Path.Combine(Application.Context!.FilesDir!.AbsolutePath, "cores");

    public string GetDataDirectory()
    {
        var dir = Path.Combine(Application.Context!.FilesDir!.AbsolutePath, "v2rayF");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string ResolveNativeLibrary(string fileName)
    {
        var nativeLibDir = Application.Context!.ApplicationInfo!.NativeLibraryDir;
        if (string.IsNullOrEmpty(nativeLibDir))
            throw new InvalidOperationException("Native library directory is unavailable.");

        return Path.Combine(nativeLibDir, fileName);
    }

    private static void RemoveLegacyCoreExtract(string coresDir)
    {
        foreach (var name in new[] { "xray", "sing-box" })
        {
            var legacyPath = Path.Combine(coresDir, name);
            if (!File.Exists(legacyPath))
                continue;

            try { File.Delete(legacyPath); }
            catch { /* best effort */ }
        }
    }

    private static async Task ExtractAssetIfMissingAsync(string assetName, string destPath, CancellationToken cancellationToken)
    {
        if (File.Exists(destPath) && new FileInfo(destPath).Length > 0)
            return;

        if (File.Exists(destPath))
            File.Delete(destPath);

        await using var input = Application.Context!.Assets!.Open(assetName);
        await using var output = File.Create(destPath);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }

    public ICoreProcessHost CreateProcessHost() => new AndroidJavaCoreProcessHost();
}
