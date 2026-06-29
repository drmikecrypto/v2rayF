using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.OS;
using Java.IO;
using Java.Lang;
using v2rayF.Services;
using Process = Java.Lang.Process;
using IOException = Java.IO.IOException;

namespace v2rayF.Android.Services;

/// <summary>
/// Starts Xray via Android ProcessBuilder — System.Diagnostics.Process crashes on many devices.
/// </summary>
public sealed class AndroidJavaCoreProcessHost : ICoreProcessHost
{
    private const string TunFdEnvKey = "XRAY_TUN_FD";

    private readonly object _lock = new();
    private Process? _process;
    private string _recentOutput = "";

    public bool IsRunning
    {
        get
        {
            lock (_lock)
                return IsAlive(_process);
        }
    }

    public bool HasExited
    {
        get
        {
            lock (_lock)
                return _process is null || !IsAlive(_process);
        }
    }

    public Task StartAsync(
        string corePath,
        string configPath,
        string workingDirectory,
        int? tunFd = null,
        CancellationToken cancellationToken = default)
    {
        StopAsync(cancellationToken).GetAwaiter().GetResult();

        if (!System.IO.File.Exists(corePath))
            throw new System.IO.FileNotFoundException("Xray core not found.", corePath);

        if (!System.IO.File.Exists(configPath))
            throw new System.IO.FileNotFoundException("Xray config not found.", configPath);

        lock (_lock)
            _recentOutput = "";

        var nativeLibDir = Application.Context?.ApplicationInfo?.NativeLibraryDir ?? workingDirectory;

        try
        {
            var builder = new ProcessBuilder(corePath, "run", "-c", configPath);
            builder.Directory(new Java.IO.File(workingDirectory));
            builder.RedirectErrorStream(true);

            var env = builder.Environment();
            env["LD_LIBRARY_PATH"] = nativeLibDir;
            env["TMPDIR"] = workingDirectory;

            if (tunFd is int fd && fd >= 0)
            {
                env[TunFdEnvKey] = fd.ToString();
                env["xray.tun.fd"] = fd.ToString();
            }

            Process process;
            lock (_lock)
            {
                _process = builder.Start();
                process = _process;
            }

            if (process is not null)
                _ = Task.Run(() => DrainOutputAsync(process), cancellationToken);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                $"Xray core failed to start: {ex.Message}. Reinstall the app or check that your device is ARM64.",
                ex);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_process is null)
                return Task.CompletedTask;

            try
            {
                if (IsAlive(_process))
                    _process.Destroy();
            }
            catch
            {
                // Best effort shutdown.
            }
            finally
            {
                _process = null;
            }
        }

        return Task.CompletedTask;
    }

    public string GetRecentError()
    {
        lock (_lock)
        {
            if (_process is not null && IsAlive(_process))
                return _recentOutput.Trim();

            if (_process is not null)
            {
                try
                {
                    var code = _process.ExitValue();
                    var output = _recentOutput.Trim();
                    return string.IsNullOrEmpty(output)
                        ? $"Xray exited with code {code}"
                        : output;
                }
                catch (IllegalThreadStateException)
                {
                    return _recentOutput.Trim();
                }
            }

            return _recentOutput.Trim();
        }
    }

    private static bool IsAlive(Process? process)
    {
        if (process is null)
            return false;

        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            return process.IsAlive;

        try
        {
            process.ExitValue();
            return false;
        }
        catch (IllegalThreadStateException)
        {
            return true;
        }
    }

    private void DrainOutputAsync(Process process)
    {
        try
        {
            using var reader = new BufferedReader(new InputStreamReader(process.InputStream));
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                lock (_lock)
                {
                    if (_recentOutput.Length > 0)
                        _recentOutput += '\n';
                    _recentOutput += line;
                    if (_recentOutput.Length > 8192)
                        _recentOutput = _recentOutput[^4096..];
                }
            }
        }
        catch
        {
            // Process ended.
        }
    }
}
