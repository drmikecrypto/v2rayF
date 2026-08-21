using v2rayF.Models;

namespace v2rayF.Services;

/// <summary>Chooses Xray vs sing-box for a server.</summary>
public static class CoreRuntime
{
    /// <summary>Protocols that only run on sing-box (never Xray).</summary>
    public static bool RequiresSingBox(ProxyServer server) =>
        server.Protocol is ProxyProtocol.Hysteria2
            or ProxyProtocol.Tuic
            or ProxyProtocol.WireGuard
            or ProxyProtocol.AnyTls;

    /// <summary>
    /// Disabled in v2.2.1 — Android classic-on-sing-box caused universal timeouts.
    /// Classic VLESS/VMess/Trojan/SS stay on Xray (desktop + Android) until TUN path is re-proven.
    /// </summary>
    public static bool PreferSingBoxOnAndroid(ProxyServer server) => false;

    public static bool UseSingBox(ProxyServer server) =>
        RequiresSingBox(server) || PreferSingBoxOnAndroid(server);

    public static string CoreLabel(ProxyServer server) =>
        UseSingBox(server) ? "sing-box" : "Xray";
}
