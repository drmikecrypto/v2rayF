namespace v2rayF.Models;

public sealed class AppSettings
{
    public RoutingMode RoutingMode { get; set; } = RoutingMode.BypassLan;

    public string CustomDirectRules { get; set; } = "";

    /// <summary>Domains/CIDRs always forced through the proxy (Custom mode).</summary>
    public string CustomProxyRules { get; set; } = "";

    /// <summary>Domains/CIDRs sent to blackhole (Custom mode).</summary>
    public string CustomBlockRules { get; set; } = "";

    public bool EnableTunMode { get; set; }

    public bool EnableSystemProxy { get; set; } = true;

    public string SubscriptionUrl { get; set; } = "";

    public bool SmartConnectEnabled { get; set; }

    public bool SmartMultipathEnabled { get; set; }

    public bool KillSwitchEnabled { get; set; } = true;

    public bool BlockIpv6 { get; set; } = true;

    public bool DnsThroughProxy { get; set; } = true;

    public bool SecureShareEnabled { get; set; }

    public int ShareBindPort { get; set; } = 10880;

    public string ShareAuthUser { get; set; } = "";

    public string ShareAuthPass { get; set; } = "";

    /// <summary>DPI evasion via TLS hello fragment (speed cost; off by default).</summary>
    public bool EnablePacketFragment { get; set; }

    /// <summary>When connected, fetch subscriptions via local HTTP proxy.</summary>
    public bool SubscriptionViaProxy { get; set; } = true;

    /// <summary>Package names (Android) excluded from the VPN tunnel.</summary>
    public string AndroidBypassPackages { get; set; } = "";

    public string LastGoodServerId { get; set; } = "";
}
