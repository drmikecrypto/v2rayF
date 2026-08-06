using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using v2rayF.Services;

namespace v2rayF.Desktop.Services;

public sealed class DesktopAppUpdater : IAppUpdater
{
    public string ReleaseAssetFileName => $"v2rayF-{GetRuntimeIdentifier()}.zip";

    public async Task ApplyUpdateAsync(UpdateOffer offer, IProgress<string>? progress, CancellationToken cancellationToken = default)
    {
        var dataDir = AppServices.CoreEnvironment.GetDataDirectory();
        var workDir = Path.Combine(dataDir, "updates", offer.Version);
        var zipPath = Path.Combine(workDir, offer.AssetFileName);
        var extractDir = Path.Combine(workDir, "files");

        await UpdateDownloadHelper.DownloadAsync(offer.DownloadUrl, zipPath, progress, cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(offer.Sha256))
        {
            progress?.Report("Verifying SHA256…");
            UpdateDownloadHelper.VerifySha256(zipPath, offer.Sha256);
        }
        else
        {
            progress?.Report("Warning: release has no SHA256 — install refused.");
            throw new InvalidOperationException(
                "Update package has no SHA256 checksum (digest or SHA256SUMS). Refusing to install.");
        }

        UpdateDownloadHelper.ExtractZip(zipPath, extractDir, progress);

        var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var exePath = ResolveExecutablePath(appDir);
        var scriptPath = WriteUpdaterScript(appDir, extractDir, exePath);

        progress?.Report("Restarting with new version…");
        LaunchDetached(scriptPath);
        Environment.Exit(0);
    }

    private static string GetRuntimeIdentifier()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
        throw new PlatformNotSupportedException("Unsupported desktop OS for in-app update.");
    }

    private static string ResolveExecutablePath(string appDir)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Path.Combine(appDir, "v2rayF.exe");

        var direct = Path.Combine(appDir, "v2rayF");
        if (File.Exists(direct))
            return direct;

        var desktop = Path.Combine(appDir, "v2rayF.Desktop");
        if (File.Exists(desktop))
            return desktop;

        return direct;
    }

    private static string WriteUpdaterScript(string appDir, string stageDir, string exePath)
    {
        var scriptDir = Path.Combine(Path.GetTempPath(), "v2rayF-updater");
        Directory.CreateDirectory(scriptDir);
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var processName = Path.GetFileNameWithoutExtension(exePath);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var ps1 = Path.Combine(scriptDir, $"apply-{stamp}.ps1");
            var content = $@"
$ErrorActionPreference = 'Stop'
Start-Sleep -Seconds 2
while (Get-Process -Name '{processName}' -ErrorAction SilentlyContinue) {{ Start-Sleep -Milliseconds 400 }}
Copy-Item -LiteralPath '{stageDir}\*' -Destination '{appDir}' -Recurse -Force
Start-Process -FilePath '{exePath}'
Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue
";
            File.WriteAllText(ps1, content);
            return ps1;
        }

        var sh = Path.Combine(scriptDir, $"apply-{stamp}.sh");
        var shell = $@"#!/usr/bin/env bash
set -euo pipefail
sleep 2
while pgrep -x '{processName}' >/dev/null 2>&1; do sleep 0.4; done
cp -R ""{stageDir}/""* ""{appDir}/""
chmod +x ""{exePath}"" ""{appDir}/cores/xray"" 2>/dev/null || true
nohup ""{exePath}"" >/dev/null 2>&1 &
rm -f ""$0""
";
        File.WriteAllText(sh, shell);
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try { File.SetUnixFileMode(sh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
            catch { /* Windows dev build */ }
        }
        return sh;
    }

    private static void LaunchDetached(string scriptPath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = scriptPath,
            CreateNoWindow = true,
            UseShellExecute = false
        });
    }
}
