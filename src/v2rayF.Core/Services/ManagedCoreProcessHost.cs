using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace v2rayF.Services;

public sealed class ManagedCoreProcessHost : ICoreProcessHost
{
    private readonly object _outputLock = new();
    private readonly StringBuilder _recentOutput = new();
    private Process? _process;
    private bool _manualStop;

    public bool IsRunning => _process is { HasExited: false };

    public bool HasExited => _process is null || _process.HasExited;

    public event EventHandler? UnexpectedExited;

    public async Task StartAsync(
        string corePath,
        string configPath,
        string workingDirectory,
        int? tunFd = null,
        CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken).ConfigureAwait(false);

        lock (_outputLock)
        {
            _recentOutput.Clear();
        }

        _process = CoreProcessLauncher.CreateProcess(corePath, configPath, workingDirectory);
        _process.ErrorDataReceived += OnErrorDataReceived;
        _process.Exited += OnProcessExited;
        _manualStop = false;

        CoreProcessLauncher.Start(_process, AppendOutputLine);

        _ = DrainOutputAsync(_process);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _manualStop = true;
        var process = Interlocked.Exchange(ref _process, null);
        if (process is null)
        {
            _manualStop = false;
            return;
        }

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
        lock (_outputLock)
        {
            return _recentOutput.ToString().Trim();
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (_manualStop)
            return;

        _process = null;
        try
        {
            UnexpectedExited?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // Never throw from process exit handler.
        }
    }

    private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Data))
            AppendOutputLine(e.Data);
    }

    private void AppendOutputLine(string line)
    {
        lock (_outputLock)
        {
            if (_recentOutput.Length > 0)
                _recentOutput.AppendLine();
            _recentOutput.Append(line);
            if (_recentOutput.Length > 8192)
                _recentOutput.Remove(0, _recentOutput.Length - 4096);
        }
    }

    private async Task DrainOutputAsync(Process process)
    {
        try
        {
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                    break;

                AppendOutputLine(line);
            }
        }
        catch
        {
            // Process ended.
        }
    }
}
