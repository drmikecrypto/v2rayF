using System;
using v2rayF.Models;

namespace v2rayF.Services;

/// <summary>One-tap network presets (honest; user still Saves to persist).</summary>
public static class NetworkProfiles
{
    public const string DailyId = "daily";
    public const string GamingId = "gaming";
    public const string IranId = "iran";
    public const string ChinaId = "china";
    public const string SentinelId = "sentinel";

    /// <summary>
    /// Default Android posture: Chromium HTTP assist on (Play Store / Translate).
    /// Does not force Global/Sentinel leak settings.
    /// </summary>
    public static void ApplyDaily(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.GamingBoostActive = false;
        settings.ChromiumHttpProxyAssist = true;
        settings.EnableTunMode = true;
        settings.EnableSystemProxy = false;
    }

    /// <summary>
    /// Gaming Boost: full TUN, no Chromium assist, no fragment/Survive, multipath + Smart Connect.
    /// Optimizes the user's exit for UDP games — not a private GearUP-style backbone.
    /// Expect Play Store / Translate to fail (same as assist-off / V2Box lab).
    /// </summary>
    public static void ApplyGaming(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.GamingBoostActive = true;
        settings.ChromiumHttpProxyAssist = false;
        settings.EnableTunMode = true;
        settings.EnableSystemProxy = false;
        settings.EnablePacketFragment = false;
        settings.AdaptiveSurviveEnabled = false;
        settings.SmartMultipathEnabled = true;
        settings.SmartConnectEnabled = true;
    }

    /// <summary>Full proxy + DoH + IPv6 block — typical Iran / heavy DPI.</summary>
    public static void ApplyIran(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.GamingBoostActive = false;
        settings.RoutingMode = RoutingMode.Global;
        settings.DnsThroughProxy = true;
        settings.BlockIpv6 = true;
        settings.KillSwitchEnabled = true;
        settings.EnableTunMode = true;
        settings.EnableSystemProxy = false;
        settings.ChromiumHttpProxyAssist = true;
    }

    /// <summary>CN sites/IPs direct via sing-box/Xray geosite; rest proxied.</summary>
    public static void ApplyChina(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.GamingBoostActive = false;
        settings.RoutingMode = RoutingMode.BypassChina;
        settings.DnsThroughProxy = true;
        settings.BlockIpv6 = true;
        settings.KillSwitchEnabled = true;
        settings.EnableTunMode = true;
        settings.EnableSystemProxy = false;
        settings.ChromiumHttpProxyAssist = true;
    }

    /// <summary>Legacy Sentinel = Global fail-closed (same lean posture as Iran) + Daily assist.</summary>
    public static void ApplySentinel(AppSettings settings) => ApplyIran(settings);

    public static string StatusHint(string profileId) => profileId switch
    {
        DailyId => "Daily mode applied — Chromium assist on (Play/Translate). Save settings to persist.",
        GamingId =>
            "Gaming Boost — full TUN, assist/fragment/Survive off, multipath on. Optimizes your exit for UDP games (not a private booster backbone). Save settings to persist.",
        ChinaId => "China profile applied — Save settings to persist.",
        IranId => "Iran profile applied — Save settings to persist.",
        _ => "Sentinel profile applied — Save settings to persist."
    };
}
