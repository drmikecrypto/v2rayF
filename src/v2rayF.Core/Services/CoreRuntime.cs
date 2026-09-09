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
    /// Android classic protocols use sing-box TUN — same live path as Connect (PLAN Phase 1 / D13).
    /// Desktop keeps Xray for VLESS/VMess/Trojan/SS. Speedtest omits ephemeral 10809 via BuildSpeedtest.
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
    /// Test delay / speedtest uses the same core as live Connect on Android (D13).
    /// Ephemeral SOCKS-only config (no 10809) avoids parallel Test All port clashes.
    /// </summary>
    public static bool UseSingBoxForSpeedtest(ProxyServer server) => UseSingBox(server);

    public static string CoreLabel(ProxyServer server) =>
        UseSingBox(server) ? "sing-box" : "Xray";
}
