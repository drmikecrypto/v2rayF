using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.OS;
using Java.IO;
using Java.Lang;
using v2rayF.Services;
using Process = Java.Lang.Process;
using IOException = Java.IO.IOException;
using Exception = System.Exception;

namespace v2rayF.Android.Services;

/// <summary>
/// Starts Xray on Android. When a TUN fd is present, uses libc posix_spawn + dup2 so the
/// child inherits the VPN fd (Java ProcessBuilder closes non-stdio fds).
/// </summary>
public sealed class AndroidJavaCoreProcessHost : ICoreProcessHost
{
    private const int InheritedTunFd = 3;
    private const int FileActionsBytes = 256;
    private const int Sigterm = 15;
    private const int Sigkill = 9;

    private readonly object _lock = new();
    private Process? _process;
    private int _spawnedPid = -1;
    private int _stdoutReadFd = -1;
    private string _recentOutput = "";
    private bool _manualStop;
    private CancellationTokenSource? _watchCts;

    public bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                if (_spawnedPid > 0)
                    return IsPidAlive(_spawnedPid);
                return IsAlive(_process);
            }
        }
    }

    public bool HasExited
    {
        get
        {
            lock (_lock)
            {
                if (_spawnedPid > 0)
                    return !IsPidAlive(_spawnedPid);
                return _process is null || !IsAlive(_process);
            }
        }
    }

    public event EventHandler? UnexpectedExited;

    public async Task StartAsync(
        string corePath,
        string configPath,
        string workingDirectory,
        int? tunFd = null,
        CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken).ConfigureAwait(false);

        if (!System.IO.File.Exists(corePath))
            throw new System.IO.FileNotFoundException("Core binary not found.", corePath);

        if (!System.IO.File.Exists(configPath))
            throw new System.IO.FileNotFoundException("Core config not found.", configPath);

        lock (_lock)
            _recentOutput = "";

        var nativeLibDir = Application.Context?.ApplicationInfo?.NativeLibraryDir ?? workingDirectory;

        try
        {
            if (tunFd is int fd && fd >= 0)
            {
                StartWithPosixSpawn(corePath, configPath, workingDirectory, nativeLibDir, fd);
            }
            else
            {
                StartWithProcessBuilder(corePath, configPath, workingDirectory, nativeLibDir);
            }
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                $"Xray core failed to start: {ex.Message}. Reinstall the app or check that your device is ARM64.",
                ex);
        }
        catch (Exception ex) when (ex is not InvalidOperationException and not System.IO.FileNotFoundException)
        {
            throw new InvalidOperationException(
                $"Xray core failed to start: {ex.Message}. Reinstall the app or check that your device is ARM64.",
                ex);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _manualStop = true;
        try
        {
            _watchCts?.Cancel();
            _watchCts?.Dispose();
            _watchCts = null;
        }
        catch
        {
            // ignore
        }

        lock (_lock)
        {
            if (_spawnedPid > 0)
            {
                var pid = _spawnedPid;
                _spawnedPid = -1;
                try
                {
                    if (IsPidAlive(pid))
                    {
                        NativeKill(pid, Sigterm);
                        for (var i = 0; i < 20 && IsPidAlive(pid); i++)
                            System.Threading.Thread.Sleep(50);
                        if (IsPidAlive(pid))
                            NativeKill(pid, Sigkill);
                    }
                }
                catch
                {
                    // Best effort shutdown.
                }
            }

            CloseStdoutFd();

            if (_process is not null)
            {
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
        }

        return Task.CompletedTask;
    }

    public string GetRecentError()
    {
        lock (_lock)
        {
            if (_spawnedPid > 0 && IsPidAlive(_spawnedPid))
                return _recentOutput.Trim();

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

            var text = _recentOutput.Trim();
            return string.IsNullOrEmpty(text) && _spawnedPid > 0
                ? $"Xray exited (pid was {_spawnedPid})"
                : text;
        }
    }

    private void StartWithProcessBuilder(
        string corePath,
        string configPath,
        string workingDirectory,
        string nativeLibDir)
    {
        var builder = new ProcessBuilder(corePath, "run", "-c", configPath);
        builder.Directory(new Java.IO.File(workingDirectory));
        builder.RedirectErrorStream(true);

        var env = builder.Environment();
        env["LD_LIBRARY_PATH"] = nativeLibDir;
        env["TMPDIR"] = workingDirectory;

        Process process;
        lock (_lock)
        {
            _manualStop = false;
            _process = builder.Start();
            process = _process;
        }

        _ = Task.Run(() => DrainJavaOutputAsync(process), CancellationToken.None);
        _watchCts = new CancellationTokenSource();
        _ = Task.Run(() => WatchJavaExitAsync(process, _watchCts.Token), CancellationToken.None);
    }

    private void StartWithPosixSpawn(
        string corePath,
        string configPath,
        string workingDirectory,
        string nativeLibDir,
        int tunFd)
    {
        var pipe = new int[2];
        if (NativePipe(pipe) != 0)
            throw new InvalidOperationException("Failed to create stdout pipe for Xray.");

        var stdoutRead = pipe[0];
        var stdoutWrite = pipe[1];

        var fileActions = new byte[FileActionsBytes];
        if (posix_spawn_file_actions_init(fileActions) != 0)
        {
            SafeCloseFd(stdoutRead);
            SafeCloseFd(stdoutWrite);
            throw new InvalidOperationException("posix_spawn_file_actions_init failed.");
        }

        try
        {
            // Inherit TUN as fd 3; map stdout/stderr to our pipe.
            if (posix_spawn_file_actions_adddup2(fileActions, tunFd, InheritedTunFd) != 0 ||
                posix_spawn_file_actions_adddup2(fileActions, stdoutWrite, 1) != 0 ||
                posix_spawn_file_actions_adddup2(fileActions, stdoutWrite, 2) != 0 ||
                posix_spawn_file_actions_addclose(fileActions, stdoutRead) != 0 ||
                posix_spawn_file_actions_addclose(fileActions, stdoutWrite) != 0)
            {
                throw new InvalidOperationException("posix_spawn file actions (dup2/close) failed.");
            }

            var argv = BuildArgvPointer(corePath, "run", "-c", configPath);
            var envp = BuildEnvpPointer(
                ("LD_LIBRARY_PATH", nativeLibDir),
                ("TMPDIR", workingDirectory),
                ("xray.tun.fd", InheritedTunFd.ToString()),
                ("XRAY_TUN_FD", InheritedTunFd.ToString()),
                ("PATH", "/system/bin:/system/xbin"),
                ("ANDROID_DATA", "/data"),
                ("ANDROID_ROOT", "/system"));

            try
            {
                var cwdGuard = IntPtr.Zero;
                    var previousCwd = System.Environment.CurrentDirectory;
                try
                {
                    if (!string.IsNullOrEmpty(workingDirectory))
                        System.Environment.CurrentDirectory = workingDirectory;

                    var rc = posix_spawn(out var pid, corePath, fileActions, IntPtr.Zero, argv, envp);
                    if (rc != 0)
                        throw new InvalidOperationException($"posix_spawn failed with code {rc}.");

                    SafeCloseFd(stdoutWrite);
                    stdoutWrite = -1;

                    lock (_lock)
                    {
                        _manualStop = false;
                        _spawnedPid = pid;
                        _stdoutReadFd = stdoutRead;
                        stdoutRead = -1;
                    }

                    _ = Task.Run(() => DrainFdOutputAsync(_stdoutReadFd), CancellationToken.None);
                    _watchCts = new CancellationTokenSource();
                    _ = Task.Run(() => WatchPidExitAsync(pid, _watchCts.Token), CancellationToken.None);
                }
                finally
                {
                    try { System.Environment.CurrentDirectory = previousCwd; } catch { /* ignore */ }
                    FreeStringArray(argv);
                    FreeStringArray(envp);
                    _ = cwdGuard;
                }
            }
            catch
            {
                SafeCloseFd(stdoutRead);
                SafeCloseFd(stdoutWrite);
                throw;
            }
        }
        finally
        {
            posix_spawn_file_actions_destroy(fileActions);
            SafeCloseFd(stdoutRead);
            SafeCloseFd(stdoutWrite);
        }
    }

    private async Task WatchPidExitAsync(int pid, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && IsPidAlive(pid))
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested || _manualStop)
                return;

            lock (_lock)
            {
                if (_spawnedPid == pid)
                    _spawnedPid = -1;
                CloseStdoutFd();
            }

            UnexpectedExited?.Invoke(this, EventArgs.Empty);
        }
        catch (System.OperationCanceledException)
        {
            // Stopped intentionally.
        }
        catch
        {
            // ignore
        }
    }

    private async Task WatchJavaExitAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && IsAlive(process))
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested || _manualStop)
                return;

            lock (_lock)
            {
                if (ReferenceEquals(_process, process))
                    _process = null;
            }

            UnexpectedExited?.Invoke(this, EventArgs.Empty);
        }
        catch (System.OperationCanceledException)
        {
            // Stopped intentionally.
        }
        catch
        {
            // ignore
        }
    }

    private void DrainFdOutputAsync(int readFd)
    {
        var buffer = new byte[1024];
        try
        {
            while (true)
            {
                var n = NativeRead(readFd, buffer, buffer.Length);
                if (n <= 0)
                    break;

                var chunk = Encoding.UTF8.GetString(buffer, 0, n);
                AppendOutput(chunk);
            }
        }
        catch
        {
            // Process ended / fd closed.
        }
    }

    private void DrainJavaOutputAsync(Process process)
    {
        try
        {
            using var reader = new BufferedReader(new InputStreamReader(process.InputStream));
            string? line;
            while ((line = reader.ReadLine()) is not null)
                AppendOutput(line + "\n");
        }
        catch
        {
            // Process ended.
        }
    }

    private void AppendOutput(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        lock (_lock)
        {
            _recentOutput += text;
            if (_recentOutput.Length > 8192)
                _recentOutput = _recentOutput[^4096..];
        }
    }

    private void CloseStdoutFd()
    {
        if (_stdoutReadFd < 0)
            return;
        SafeCloseFd(_stdoutReadFd);
        _stdoutReadFd = -1;
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

    private static bool IsPidAlive(int pid)
    {
        if (pid <= 0)
            return false;
        // kill(pid, 0) returns 0 if the process exists.
        return NativeKill(pid, 0) == 0;
    }

    internal static void CloseFd(int fd)
    {
        if (fd < 0)
            return;
        try { NativeClose(fd); }
        catch { /* ignore */ }
    }

    private static void SafeCloseFd(int fd) => CloseFd(fd);

    private static IntPtr BuildArgvPointer(params string[] args)
    {
        var pointers = new IntPtr[args.Length + 1];
        for (var i = 0; i < args.Length; i++)
            pointers[i] = Marshal.StringToHGlobalAnsi(args[i]);
        pointers[^1] = IntPtr.Zero;

        var argv = Marshal.AllocHGlobal(IntPtr.Size * pointers.Length);
        for (var i = 0; i < pointers.Length; i++)
            Marshal.WriteIntPtr(argv, i * IntPtr.Size, pointers[i]);
        return argv;
    }

    private static IntPtr BuildEnvpPointer(params (string Key, string Value)[] pairs)
    {
        var list = new List<string>(pairs.Length);
        foreach (var (key, value) in pairs)
            list.Add($"{key}={value}");

        var pointers = new IntPtr[list.Count + 1];
        for (var i = 0; i < list.Count; i++)
            pointers[i] = Marshal.StringToHGlobalAnsi(list[i]);
        pointers[^1] = IntPtr.Zero;

        var envp = Marshal.AllocHGlobal(IntPtr.Size * pointers.Length);
        for (var i = 0; i < pointers.Length; i++)
            Marshal.WriteIntPtr(envp, i * IntPtr.Size, pointers[i]);
        return envp;
    }

    private static void FreeStringArray(IntPtr array)
    {
        if (array == IntPtr.Zero)
            return;

        for (var i = 0; ; i++)
        {
            var p = Marshal.ReadIntPtr(array, i * IntPtr.Size);
            if (p == IntPtr.Zero)
                break;
            Marshal.FreeHGlobal(p);
        }

        Marshal.FreeHGlobal(array);
    }

    [DllImport("libc", EntryPoint = "posix_spawn", SetLastError = true)]
    private static extern int posix_spawn(
        out int pid,
        string path,
        byte[] fileActions,
        IntPtr attr,
        IntPtr argv,
        IntPtr envp);

    [DllImport("libc", EntryPoint = "posix_spawn_file_actions_init", SetLastError = true)]
    private static extern int posix_spawn_file_actions_init(byte[] fileActions);

    [DllImport("libc", EntryPoint = "posix_spawn_file_actions_destroy", SetLastError = true)]
    private static extern int posix_spawn_file_actions_destroy(byte[] fileActions);

    [DllImport("libc", EntryPoint = "posix_spawn_file_actions_adddup2", SetLastError = true)]
    private static extern int posix_spawn_file_actions_adddup2(byte[] fileActions, int fildes, int newfildes);

    [DllImport("libc", EntryPoint = "posix_spawn_file_actions_addclose", SetLastError = true)]
    private static extern int posix_spawn_file_actions_addclose(byte[] fileActions, int fildes);

    [DllImport("libc", EntryPoint = "pipe", SetLastError = true)]
    private static extern int NativePipe(int[] pipefd);

    [DllImport("libc", EntryPoint = "read", SetLastError = true)]
    private static extern int NativeRead(int fd, byte[] buffer, int count);

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int NativeKill(int pid, int sig);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int NativeClose(int fd);
}
