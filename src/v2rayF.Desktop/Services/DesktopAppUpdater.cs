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
        var logPath = Path.Combine(dataDir, "update-last.log");

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
        EnsureAppDirectoryWritable(appDir);

        var exePath = ResolveExecutablePath(appDir);
        var pid = Environment.ProcessId;
        var scriptPath = WriteUpdaterScript(appDir, extractDir, exePath, pid, logPath);

        progress?.Report("Restarting with new version…");
        LaunchDetached(scriptPath);
        Environment.Exit(0);
    }

    private static void EnsureAppDirectoryWritable(string appDir)
    {
        try
        {
            var probe = Path.Combine(appDir, $".v2rayf-write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Cannot write to the install folder ({appDir}). Move v2rayF to a user-writable folder (e.g. Downloads) and try Update again. {ex.Message}",
                ex);
        }
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
        {
            var renamed = Path.Combine(appDir, "v2rayF.exe");
            if (File.Exists(renamed))
                return renamed;
            var desktop = Path.Combine(appDir, "v2rayF.Desktop.exe");
            if (File.Exists(desktop))
                return desktop;
            return renamed;
        }

        var direct = Path.Combine(appDir, "v2rayF");
        if (File.Exists(direct))
            return direct;

        var desktopUnix = Path.Combine(appDir, "v2rayF.Desktop");
        if (File.Exists(desktopUnix))
            return desktopUnix;

        return direct;
    }

    private static string PsLiteral(string path) =>
        "'" + path.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string WriteUpdaterScript(string appDir, string stageDir, string exePath, int pid, string logPath)
    {
        var scriptDir = Path.Combine(Path.GetTempPath(), "v2rayF-updater");
        Directory.CreateDirectory(scriptDir);
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var ps1 = Path.Combine(scriptDir, $"apply-{stamp}.ps1");
            var content = $@"
$ErrorActionPreference = 'Stop'
$log = {PsLiteral(logPath)}
function Write-Log([string]$m) {{ Add-Content -LiteralPath $log -Value ((Get-Date).ToString('o') + ' ' + $m) -ErrorAction SilentlyContinue }}
try {{
  Write-Log 'updater started'
  Start-Sleep -Seconds 2
  $deadline = (Get-Date).AddMinutes(2)
  while ((Get-Date) -lt $deadline) {{
    $p = Get-Process -Id {pid} -ErrorAction SilentlyContinue
    if (-not $p) {{ break }}
    Start-Sleep -Milliseconds 400
  }}
  $src = {PsLiteral(stageDir)}
  $dst = {PsLiteral(appDir)}
  $ok = $false
  for ($i = 0; $i -lt 30; $i++) {{
    try {{
      Copy-Item -Path (Join-Path $src '*') -Destination $dst -Recurse -Force
      $ok = $true
      break
    }} catch {{
      Write-Log (""copy retry $($i+1): $($_.Exception.Message)"")
      Start-Sleep -Milliseconds 500
    }}
  }}
  if (-not $ok) {{ throw 'Copy failed after retries (files may be locked).' }}
  Write-Log 'copy ok'
  Start-Process -FilePath {PsLiteral(exePath)}
  Write-Log 'restarted'
}} catch {{
  Write-Log (""FAILED: $($_.Exception.Message)"")
  throw
}} finally {{
  Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue
}}
";
            File.WriteAllText(ps1, content);
            return ps1;
        }

        var sh = Path.Combine(scriptDir, $"apply-{stamp}.sh");
        var shell = $@"#!/usr/bin/env bash
set -euo pipefail
LOG={BashQuote(logPath)}
log() {{ echo ""$(date -Iseconds) $*"" >> ""$LOG"" 2>/dev/null || true; }}
log 'updater started'
sleep 2
deadline=$((SECONDS+120))
while kill -0 {pid} 2>/dev/null; do
  if (( SECONDS >= deadline )); then break; fi
  sleep 0.4
done
src={BashQuote(stageDir)}
dst={BashQuote(appDir)}
ok=0
for i in $(seq 1 30); do
  if cp -R ""$src/""* ""$dst/"" 2>>""$LOG""; then ok=1; break; fi
  log ""copy retry $i""
  sleep 0.5
done
if [[ ""$ok"" -ne 1 ]]; then log 'copy failed'; exit 1; fi
log 'copy ok'
chmod +x {BashQuote(exePath)} ""$dst/cores/xray"" 2>/dev/null || true
nohup {BashQuote(exePath)} >/dev/null 2>&1 &
log 'restarted'
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

    private static string BashQuote(string path) =>
        "'" + path.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

    private static void LaunchDetached(string scriptPath)
    {
        Process? proc;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            proc = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
        else
        {
            proc = Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = scriptPath,
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }

        if (proc is null)
            throw new InvalidOperationException("Failed to start updater script — update aborted.");

        // Brief settle so Start failures surface before we exit the app.
        Thread.Sleep(200);
        if (proc.HasExited && proc.ExitCode != 0)
            throw new InvalidOperationException($"Updater script exited early (code {proc.ExitCode}).");
    }
}
