using System.Threading;
using System.Threading.Tasks;

namespace v2rayF.Services;

public interface IPlatformIntegration
{
    bool IsMobile { get; }

    bool CanUseTunMode { get; }

    string TunRequirementMessage { get; }

    string? LastProxyMethod { get; }

    Task<int?> EstablishVpnAsync(CancellationToken cancellationToken = default);

    Task EnableProxyAsync(CancellationToken cancellationToken = default);

    Task DisableProxyAsync(CancellationToken cancellationToken = default);
}
