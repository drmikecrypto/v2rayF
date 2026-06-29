using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace v2rayF.Services;

public sealed class ManagedCoreProcessHost : ICoreProcessHost
{
    private readonly object _stderrLock = new();
    private readonly StringBuilder _recentStderr = new();
    private Process? _process;
    private bool _manualStop;

    public bool IsRunning => _process is { HasExited: false };

    public bool HasExited => _process is null || _process.HasExited;

    public Task StartAsync(
        string corePath,
        string configPath,
        string workingDirectory,
        int? tunFd = null,
        CancellationToken cancellationToken = default)
    {
        StopAsync(cancellationToken).GetAwaiter().GetResult();

        lock (_stderrLock)
        {
            _recentStderr.Clear();
        }

        _process = CoreProcessLauncher.CreateProcess(corePath, configPath, workingDirectory);
        _process.ErrorDataReceived += OnErrorDataReceived;
        _process.Exited += OnProcessExited;
        _manualStop = false;

        CoreProcessLauncher.Start(_process, line =>
        {
            lock (_stderrLock)
            {
                if (_recentStderr.Length > 0)
                    _recentStderr.AppendLine();
                _recentStderr.Append(line);
            }
        });

        _ = DrainOutputAsync(_process);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var process = Interlocked.Exchange(ref _process, null);
        if (process is null)
            return;

        _manualStop = true;
        try
        {
            if (!process.HasExited)
            {
                CoreProcessLauncher.Kill(process);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(3000);
                try
                {
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Timed out waiting for exit.
                }
            }
        }
        catch
        {
            // Best effort shutdown.
        }
        finally
        {
            process.ErrorDataReceived -= OnErrorDataReceived;
            process.Exited -= OnProcessExited;
            process.Dispose();
            _manualStop = false;
        }
    }

    public string GetRecentError()
    {
        lock (_stderrLock)
        {
            return _recentStderr.ToString().Trim();
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (_manualStop)
            return;

        _process = null;
    }

    private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Data))
            return;

        lock (_stderrLock)
        {
            if (_recentStderr.Length > 0)
                _recentStderr.AppendLine();
            _recentStderr.Append(e.Data);
        }
    }

    private static async Task DrainOutputAsync(Process process)
    {
        try
        {
            while (!process.HasExited)
            {
                var line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                    break;
            }
        }
        catch
        {
            // Process ended.
        }
    }
}
