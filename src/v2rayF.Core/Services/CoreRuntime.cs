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
    /// Android classic protocols use sing-box TUN (system stack) — V2Box-class path for
    /// raw-socket apps (Instagram Direct / MQTT). Desktop keeps Xray for VLESS/VMess/Trojan/SS.
    /// Speedtest still uses UDP DNS + no ephemeral 10809 (v2.2.1 safeguards).
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
    /// (live Connect) is true — Android sing-box speedtest caused universal timeouts in 2.2.2.
    /// </summary>
    public static bool UseSingBoxForSpeedtest(ProxyServer server) => RequiresSingBox(server);

    public static string CoreLabel(ProxyServer server) =>
        UseSingBox(server) ? "sing-box" : "Xray";
}
