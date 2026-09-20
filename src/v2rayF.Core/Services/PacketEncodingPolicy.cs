using System;
using v2rayF.Models;

namespace v2rayF.Services;

/// <summary>
/// TUN UDP DNS detours through the proxy. VLESS/VMess need XUDP for UDP-over-TCP;
/// Shadowsocks / Trojan / Hysteria carry UDP natively. Vision already muxes UDP→XUDP
/// in-core — do not inject. Forced on every build in 2.0.7–2.0.8 (incl. speedtest)
/// caused Connected crawl (reverted 2.0.9) — inject only for live TUN when the link
/// omits packetEncoding.
/// </summary>
public static class PacketEncodingPolicy
{
    public const string TunUdpDefault = "xudp";

    /// <summary>
    /// Resolves VLESS/VMess packet encoding for JSON builders.
    /// Explicit link values always win. Live TUN injects <see cref="TunUdpDefault"/>
    /// for non-Vision VLESS/VMess (any network/security). Vision skipped.
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

        // Vision path already converts UDP → mux/XUDP; stacking packetEncoding fights splice.
        if (ShareLinkParser.IsVisionFlow(server))
            return null;

        return TunUdpDefault;
    }
}
