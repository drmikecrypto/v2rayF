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
    /// Android classic protocols use sing-box TUN (V2Box-class path).
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

    public static string CoreLabel(ProxyServer server) =>
        UseSingBox(server) ? "sing-box" : "Xray";
}
