using System.Collections.Generic;
using v2rayF.Models;

namespace v2rayF.Services;

/// <summary>
/// Escalation tactics when Smart Connect exhausts candidates or the path flaps.
/// Temporary overrides — does not permanently rewrite user preferences except LastSurviveTactic hint.
/// </summary>
public sealed class AdaptiveSurviveService
{
    public const string TacticFragment = "fragment";
    public const string TacticSentinel = "sentinel";
    public const string TacticNone = "";

    /// <summary>Survive retries only the top N ranked candidates (not the full failover list).</summary>
    public const int MaxSurviveCandidates = 2;

    public sealed record SurviveAttempt(AppSettings Settings, string Tactic, string StatusReason);

    /// <summary>
    /// Builds ordered temporary setting clones to retry after a failed connect wave.
    /// </summary>
    /// <param name="force">When true, build tactics even if AdaptiveSurviveEnabled is off (Smart Connect exhausted).</param>
    public IReadOnlyList<SurviveAttempt> BuildRetryAttempts(AppSettings userSettings, bool force = false)
    {
        if (!force && !userSettings.AdaptiveSurviveEnabled)
            return [];

        var attempts = new List<SurviveAttempt>();

        if (!userSettings.EnablePacketFragment)
        {
            var frag = Clone(userSettings);
            frag.EnablePacketFragment = true;
            attempts.Add(new SurviveAttempt(
                frag,
                TacticFragment,
                "Survive: enabled fragment after probe failures"));
        }

        // Global + BlockIpv6; DnsThroughProxy still uses DoH servers but dns-module stays direct
        // (see XrayConfigBuilder) so node bootstrap no longer chicken-and-eggs.
        if (!userSettings.DnsThroughProxy || !userSettings.BlockIpv6 || userSettings.RoutingMode != RoutingMode.Global)
        {
            var sentinel = Clone(userSettings);
            sentinel.DnsThroughProxy = true;
            sentinel.BlockIpv6 = true;
            sentinel.RoutingMode = RoutingMode.Global;
            // Keep fragment if already trying it or user had it.
            if (attempts.Count > 0)
                sentinel.EnablePacketFragment = true;
            attempts.Add(new SurviveAttempt(
                sentinel,
                TacticSentinel,
                "Survive: applied Sentinel DNS/IPv6/Global after failures"));
        }

        // Prefer last successful tactic first.
        if (!string.IsNullOrWhiteSpace(userSettings.LastSurviveTactic))
        {
            attempts.Sort((a, b) =>
            {
                var aMatch = a.Tactic == userSettings.LastSurviveTactic ? 0 : 1;
                var bMatch = b.Tactic == userSettings.LastSurviveTactic ? 0 : 1;
                return aMatch.CompareTo(bMatch);
            });
        }

        return attempts;
    }

    public static bool ShouldApplyFragmentForServer(ProxyServer server, bool fragmentEnabled) =>
        fragmentEnabled && !ShareLinkParser.IsVisionFlow(server);

    private static AppSettings Clone(AppSettings s) => new()
    {
        RoutingMode = s.RoutingMode,
        CustomDirectRules = s.CustomDirectRules,
        CustomProxyRules = s.CustomProxyRules,
        CustomBlockRules = s.CustomBlockRules,
        EnableTunMode = s.EnableTunMode,
        EnableSystemProxy = s.EnableSystemProxy,
        SubscriptionUrl = s.SubscriptionUrl,
        SmartConnectEnabled = s.SmartConnectEnabled,
        StartupRankServersEnabled = s.StartupRankServersEnabled,
        LastStartupRankUtc = s.LastStartupRankUtc,
        AllowDesktopNotificationRouting = s.AllowDesktopNotificationRouting,
        SmartMultipathEnabled = s.SmartMultipathEnabled,
        SelectedServerId = s.SelectedServerId,
        KillSwitchEnabled = s.KillSwitchEnabled,
        BlockIpv6 = s.BlockIpv6,
        DnsThroughProxy = s.DnsThroughProxy,
        SecureShareEnabled = s.SecureShareEnabled,
        ShareBindPort = s.ShareBindPort,
        ShareAuthUser = s.ShareAuthUser,
        ShareAuthPass = s.ShareAuthPass,
        ShareListenAllInterfaces = s.ShareListenAllInterfaces,
        EnablePacketFragment = s.EnablePacketFragment,
        SubscriptionViaProxy = s.SubscriptionViaProxy,
        AndroidBypassPackages = s.AndroidBypassPackages,
        AndroidBlockPackages = s.AndroidBlockPackages,
        DesktopDirectProcesses = s.DesktopDirectProcesses,
        DesktopBlockProcesses = s.DesktopBlockProcesses,
        LastGoodServerId = s.LastGoodServerId,
        AdaptiveSurviveEnabled = s.AdaptiveSurviveEnabled,
        AutoReconnectEnabled = s.AutoReconnectEnabled,
        BatteryOptimizationPromptShown = s.BatteryOptimizationPromptShown,
        LastSurviveTactic = s.LastSurviveTactic,
        StorageVersion = s.StorageVersion
    };
}
