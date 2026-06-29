using System.Threading;
using System.Threading.Tasks;

namespace v2rayF.Services;

public interface ICoreProcessHost
{
    bool IsRunning { get; }

    bool HasExited { get; }

    Task StartAsync(
        string corePath,
        string configPath,
        string workingDirectory,
        int? tunFd = null,
        CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    string GetRecentError();
}
