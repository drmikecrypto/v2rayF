using System.Threading;
using System.Threading.Tasks;

namespace v2rayF.Services;

public sealed class NullKillSwitch : IKillSwitch
{
    public bool IsSupported => false;

    public bool IsArmed => false;

    public string? LastError => null;

    public Task EnableAsync(
        string coreExecutablePath,
        bool allowTunInterface = false,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task DisableAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
