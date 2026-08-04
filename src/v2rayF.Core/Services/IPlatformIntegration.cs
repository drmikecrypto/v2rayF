using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace v2rayF.Services;

public interface IPlatformIntegration
{
    bool IsMobile { get; }

    bool CanUseTunMode { get; }

    string TunRequirementMessage { get; }

    string? LastProxyMethod { get; }

    string? LastEstablishError { get; }

    /// <summary>
    /// Establish VPN/TUN. Optional Android per-app bypass package names and IPv6 block flag.
    /// </summary>
    Task<int?> EstablishVpnAsync(
        IReadOnlyList<string>? bypassPackages = null,
        bool blockIpv6 = true,
        CancellationToken cancellationToken = default);

    Task EnableProxyAsync(CancellationToken cancellationToken = default);

    Task DisableProxyAsync(CancellationToken cancellationToken = default);

    /// <summary>Best-effort first non-loopback IPv4 for Secure Share display.</summary>
    string? GetLanIPv4Address();
}
