namespace v2rayF.Models;

/// <summary>Per-app network policy for App Network.</summary>
public enum AppNetworkMode
{
    /// <summary>Traffic through the VPN/TUN (default).</summary>
    Vpn = 0,

    /// <summary>Split out of VPN (Android: OS clearnet; Desktop TUN: core direct).</summary>
    Direct = 1,

    /// <summary>No internet while VPN is up (core block / blackhole).</summary>
    Block = 2
}
