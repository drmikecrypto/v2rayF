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

    /// <summary>WhatsApp / Telegram / Discord / Signal / Slack — real UDP DNS, not FakeIP.</summary>
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

    /// <summary>Apple push (desktop bridges / iOS sync apps).</summary>
    public static readonly string[] DesktopOnlyPushSuffixes =
    [
        "push.apple.com"
    ];

    /// <summary>Push/realtime endpoints that must route to proxy explicitly on Android TUN.</summary>
    public static readonly string[] MessagingPushRouteHosts =
    [
        "mtalk.google.com",
        "fcm.googleapis.com",
        "firebaseinstallations.googleapis.com",
        "g.whatsapp.net",
        "e1.whatsapp.net",
        "e2.whatsapp.net",
        "web.telegram.org",
        "api.telegram.org",
        "pluto.web.telegram.org",
        "venus.web.telegram.org",
        "gateway.discord.gg",
        "chat.signal.org",
        "uds.signal.org",
        "hooks.slack.com",
        "wss-primary.slack.com"
    ];

    /// <summary>Desktop TUN: WNS + messenger + Signal/Slack + Apple push suffixes.</summary>
    public static readonly string[] DesktopPushDomainSuffixes = CombineUnique(
        WindowsNotificationSuffixes,
        MessagingDnsSuffixes,
        DesktopOnlyPushSuffixes);

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
