using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using v2rayF.Models;

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

    /// <summary>
    /// After the proxy core is up, tell the OS the VPN is validated (Android captive-portal).
    /// Desktop no-op.
    /// </summary>
    Task NotifyVpnReadyAsync(CancellationToken cancellationToken = default);

    /// <summary>Best-effort first non-loopback IPv4 for Secure Share display.</summary>
    string? GetLanIPv4Address();

    /// <summary>
    /// Apps/processes eligible for App Network. Cached by implementers; call when the panel opens.
    /// </summary>
    Task<IReadOnlyList<InstalledAppInfo>> GetNetworkAppsAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Per-app traffic samples. Only call while the App Network panel is visible.
    /// </summary>
    Task<IReadOnlyDictionary<string, AppTrafficSnapshot>> GetAppTrafficAsync(
        IReadOnlyList<string> ids,
        CancellationToken cancellationToken = default);
}
