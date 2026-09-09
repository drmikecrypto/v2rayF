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
    /// Android classic protocols use sing-box TUN — same live path as Connect (Instagram Direct).
    /// Desktop keeps Xray for VLESS/VMess/Trojan/SS.
    /// </summary>
    public static bool PreferSingBoxOnAndroid(ProxyServer server) =>
        AppServices.Platform?.IsMobile == true &&
        server.Protocol is ProxyProtocol.VLESS
            or ProxyProtocol.VMess
            or ProxyProtocol.Trojan
            or ProxyProtocol.Shadowsocks;

    public static bool UseSingBox(ProxyServer server) =>
        RequiresSingBox(server) || PreferSingBoxOnAndroid(server);

    /// <summary>
    /// Test delay / speedtest only. Classic stays on Xray even when PreferSingBoxOnAndroid
    /// (live Connect) is true — Android sing-box speedtest caused universal timeouts in v2.2.2
    /// (fixed v2.2.3); 2.6.2.14 D13 reintroduced UseSingBox here and broke Test All again.
    /// Hy2/TUIC/WG/anytls still probe via sing-box (RequiresSingBox).
    /// </summary>
    public static bool UseSingBoxForSpeedtest(ProxyServer server) => RequiresSingBox(server);

    public static string CoreLabel(ProxyServer server) =>
        UseSingBox(server) ? "sing-box" : "Xray";
}
