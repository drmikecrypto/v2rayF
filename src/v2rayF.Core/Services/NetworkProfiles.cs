using System;
using v2rayF.Models;

namespace v2rayF.Services;

/// <summary>One-tap CN/IR-oriented network presets (honest; user still Saves to persist).</summary>
public static class NetworkProfiles
{
    public const string IranId = "iran";
    public const string ChinaId = "china";
    public const string SentinelId = "sentinel";

    /// <summary>Full proxy + DoH + IPv6 block — typical Iran / heavy DPI.</summary>
    public static void ApplyIran(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.RoutingMode = RoutingMode.Global;
        settings.DnsThroughProxy = true;
        settings.BlockIpv6 = true;
        settings.KillSwitchEnabled = true;
        settings.EnableTunMode = true;
        settings.EnableSystemProxy = false;
    }

    /// <summary>CN sites/IPs direct via sing-box/Xray geosite; rest proxied.</summary>
    public static void ApplyChina(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.RoutingMode = RoutingMode.BypassChina;
        settings.DnsThroughProxy = true;
        settings.BlockIpv6 = true;
        settings.KillSwitchEnabled = true;
        settings.EnableTunMode = true;
        settings.EnableSystemProxy = false;
    }

    /// <summary>Legacy Sentinel = Global fail-closed (same lean posture as Iran).</summary>
    public static void ApplySentinel(AppSettings settings) => ApplyIran(settings);
}
