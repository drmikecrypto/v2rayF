using System;
using v2rayF.Models;

namespace v2rayF.Services;

/// <summary>
/// TUN UDP DNS detours through the proxy. Plain VLESS/VMess TCP (security=none) needs
/// XUDP packet encoding for UDP-over-TCP; Shadowsocks carries UDP natively.
/// Forced everywhere in 2.0.7–2.0.8 caused Connected crawl (reverted 2.0.9) — apply only
/// for live TUN + plain TCP/none when the link omits packetEncoding.
/// </summary>
public static class PacketEncodingPolicy
{
    public const string TunUdpDefault = "xudp";

    /// <summary>
    /// Resolves VLESS/VMess packet encoding for JSON builders.
    /// Explicit link values always win. Live TUN may inject <see cref="TunUdpDefault"/>
    /// for plain TCP/none only (not Vision / TLS / REALITY / WS / …).
    /// </summary>
    public static string? Resolve(ProxyServer server, bool liveTun)
    {
        ArgumentNullException.ThrowIfNull(server);

        if (!string.IsNullOrWhiteSpace(server.PacketEncoding))
            return server.PacketEncoding.Trim();

        if (!liveTun)
            return null;

        if (server.Protocol is not (ProxyProtocol.VLESS or ProxyProtocol.VMess))
            return null;

        if (ShareLinkParser.IsVisionFlow(server))
            return null;

        var network = ShareLinkParser.NormalizeNetwork(server.Network);
        var security = ShareLinkParser.NormalizeSecurity(server.Security);
        if (network is not "tcp")
            return null;
        if (security is not ("none" or ""))
            return null;

        return TunUdpDefault;
    }
}
