using System.Threading;
using System.Threading.Tasks;

namespace v2rayF.Services;

public interface IKillSwitch
{
    bool IsSupported { get; }

    bool IsArmed { get; }

    string? LastError { get; }

    /// <summary>Block clearnet until DisableAsync. Pass the Xray executable path to allow.</summary>
    Task EnableAsync(string coreExecutablePath, CancellationToken cancellationToken = default);

    Task DisableAsync(CancellationToken cancellationToken = default);
}
