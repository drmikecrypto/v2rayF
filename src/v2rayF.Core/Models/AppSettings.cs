namespace v2rayF.Models;

public sealed class AppSettings
{
    public RoutingMode RoutingMode { get; set; } = RoutingMode.BypassLan;

    public string CustomDirectRules { get; set; } = "";

    public bool EnableTunMode { get; set; }

    public bool EnableSystemProxy { get; set; } = true;

    public string SubscriptionUrl { get; set; } = "";
}
