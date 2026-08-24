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

    /// <summary>When true, Connect ranks servers and prefers the fastest proxy-path peer.</summary>
    public bool SmartConnectEnabled { get; set; } = true;

    public bool SmartMultipathEnabled { get; set; }

    /// <summary>Persisted list selection (server Id GUID string).</summary>
    public string SelectedServerId { get; set; } = "";

    public bool KillSwitchEnabled { get; set; } = true;

    public bool BlockIpv6 { get; set; } = true;

    public bool DnsThroughProxy { get; set; } = true;

    public bool SecureShareEnabled { get; set; }

    public int ShareBindPort { get; set; } = 10880;

    public string ShareAuthUser { get; set; } = "";

    public string ShareAuthPass { get; set; } = "";

    /// <summary>
    /// When false (default), Secure Share binds to the primary LAN IPv4 only.
    /// When true, listens on 0.0.0.0 (all interfaces).
    /// </summary>
    public bool ShareListenAllInterfaces { get; set; }

    /// <summary>DPI evasion via TLS hello fragment (speed cost; off by default).</summary>
    public bool EnablePacketFragment { get; set; }

    /// <summary>When connected, fetch subscriptions via local HTTP proxy.</summary>
    public bool SubscriptionViaProxy { get; set; } = true;

    /// <summary>Package names (Android) excluded from the VPN tunnel (App Network Direct).</summary>
    public string AndroidBypassPackages { get; set; } = "";

    /// <summary>Package names (Android) blocked via TUN package_name → block while VPN is up.</summary>
    public string AndroidBlockPackages { get; set; } = "";

    /// <summary>Process names (Desktop TUN) routed to direct egress (App Network Direct).</summary>
    public string DesktopDirectProcesses { get; set; } = "";

    /// <summary>Process names (Desktop TUN) routed to blackhole (App Network Block).</summary>
    public string DesktopBlockProcesses { get; set; } = "";

    public string LastGoodServerId { get; set; } = "";

    /// <summary>Escalate fragment / Sentinel DNS tactics when Smart Connect failover exhausts (opt-in; fragment is slow).</summary>
    public bool AdaptiveSurviveEnabled { get; set; }

    /// <summary>After unexpected core drop, try one reconnect with user settings (no Survive fragment).</summary>
    public bool AutoReconnectEnabled { get; set; } = true;

    /// <summary>Hint from last successful Adaptive Survive session (fragment / sentinel).</summary>
    public string LastSurviveTactic { get; set; } = "";

    /// <summary>Storage schema version (2 = encrypted sensitive fields). Default 1 = legacy plaintext.</summary>
    public int StorageVersion { get; set; } = 1;
}
