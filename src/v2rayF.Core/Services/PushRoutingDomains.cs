using System;
using System.Collections.Generic;
using System.Linq;

namespace v2rayF.Services;

/// <summary>
/// Shared push / messaging domain lists for Android sing-box TUN and desktop Xray TUN.
/// Real UDP dns.final + explicit proxy routes.
/// </summary>
public static class PushRoutingDomains
{
    /// <summary>WNS / Microsoft desktop push host suffixes.</summary>
    public static readonly string[] WindowsNotificationSuffixes =
    [
        "wns.windows.com",
        "notify.windows.com",
        "push.services.microsoft.com",
        "mp.microsoft.com"
    ];

    /// <summary>FCM / Google push — exact hosts (long-lived MQTT/WebSocket).</summary>
    public static readonly string[] FcmDnsExactHosts =
    [
        "mtalk.google.com",
        "fcm.googleapis.com",
        "firebaseinstallations.googleapis.com"
    ];

    /// <summary>WhatsApp / Telegram / Discord / Signal / Slack — real UDP DNS via dns.final.</summary>
    public static readonly string[] MessagingDnsSuffixes =
    [
        "whatsapp.net",
        "whatsapp.com",
        "telegram.org",
        "t.me",
        "discord.com",
        "discordapp.com",
        "discord.gg",
        "signal.org",
        "slack.com",
        "slack-msgs.com",
        "slackb.com",
        "slack-edge.com"
    ];

    /// <summary>OEM push CDNs — proxy route (dns.final already UDP).</summary>
    public static readonly string[] OemPushDnsSuffixes =
    [
        "push.hicloud.com",
        "push.apple.com",
        "xiaomi.com",
        "xmpush.xiaomi.com",
        "getui.com",
        "jpush.cn",
        "heytapmobi.com",
        "mcs.heytapmobi.com"
    ];

    /// <summary>
    /// Push/realtime endpoints that need exact-host proxy before Chromium UDP blocks.
    /// Prefer MessagingDnsSuffixes for coverage; keep this list short (exception-list policy).
    /// FCM hosts live in <see cref="FcmDnsExactHosts"/> only — do not duplicate here.
    /// </summary>
    public static readonly string[] MessagingPushRouteHosts =
    [
        // WhatsApp edge nodes not always under whatsapp.net suffix in some OEM resolvers.
        "g.whatsapp.net",
        "e1.whatsapp.net",
        "e2.whatsapp.net"
    ];

    /// <summary>Desktop TUN: WNS + messenger + OEM (includes Apple push).</summary>
    public static readonly string[] DesktopPushDomainSuffixes = CombineUnique(
        WindowsNotificationSuffixes,
        MessagingDnsSuffixes,
        OemPushDnsSuffixes);

    private static string[] CombineUnique(params IEnumerable<string>[] groups)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            foreach (var item in group)
                set.Add(item);
        }

        return set.ToArray();
    }
}
