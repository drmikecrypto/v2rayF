using System;
using System.Threading;
using System.Threading.Tasks;

namespace v2rayF.Services;

public interface ICoreProcessHost
{
    bool IsRunning { get; }

    bool HasExited { get; }

    /// <summary>Raised when the core process exits unexpectedly (not via StopAsync).</summary>
    event EventHandler? UnexpectedExited;

    Task StartAsync(
        string corePath,
        string configPath,
        string workingDirectory,
        int? tunFd = null,
        CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    string GetRecentError();
}
