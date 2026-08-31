using System;
using System.Collections.Generic;
using System.Linq;

namespace v2rayF.Services;

/// <summary>
/// Shared push / messaging domain lists for Android sing-box TUN and desktop Xray TUN.
/// Real DNS + explicit proxy routes — not FakeIP + sniff alone.
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

    /// <summary>WhatsApp / Telegram / Discord — suffixes resolved via real UDP DNS, not FakeIP.</summary>
    public static readonly string[] MessagingDnsSuffixes =
    [
        "whatsapp.net",
        "whatsapp.com",
        "telegram.org",
        "t.me",
        "discord.com",
        "discordapp.com",
        "discord.gg"
    ];

    /// <summary>Push/realtime endpoints that must route to proxy explicitly on Android TUN.</summary>
    public static readonly string[] MessagingPushRouteHosts =
    [
        "mtalk.google.com",
        "fcm.googleapis.com",
        "firebaseinstallations.googleapis.com",
        "web.telegram.org",
        "gateway.discord.gg"
    ];

    /// <summary>Desktop TUN: WNS + messenger push suffixes when notification routing is enabled.</summary>
    public static readonly string[] DesktopPushDomainSuffixes = CombineUnique(
        WindowsNotificationSuffixes,
        MessagingDnsSuffixes);

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
