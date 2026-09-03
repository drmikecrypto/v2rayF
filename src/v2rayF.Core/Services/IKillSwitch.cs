using System.Threading;
using System.Threading.Tasks;

namespace v2rayF.Services;

public interface IKillSwitch
{
    bool IsSupported { get; }

    bool IsArmed { get; }

    string? LastError { get; }

    /// <summary>
    /// Block clearnet until <see cref="DisableAsync"/>. Pass the live core executable
    /// (Xray or sing-box) to allow through the firewall.
    /// When <paramref name="allowTunInterface"/> is true (desktop TUN mode), also allow outbound
    /// on the TUN adapter so apps are not blackholed by the block-all rule.
    /// </summary>
    Task EnableAsync(
        string coreExecutablePath,
        bool allowTunInterface = false,
        CancellationToken cancellationToken = default);

    Task DisableAsync(CancellationToken cancellationToken = default);
}
