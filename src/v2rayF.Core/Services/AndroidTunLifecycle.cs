namespace v2rayF.Services;

/// <summary>
/// Android VpnService TUN fd lifecycle — one conceptual state machine for establish / rebind / teardown.
/// Callers still live in V2rayVpnService + ProxyCoreService; this documents legal transitions.
/// </summary>
public static class AndroidTunLifecycle
{
    public enum State
    {
        /// <summary>No VPN interface; no inherited fd.</summary>
        Idle,

        /// <summary>VpnService.Builder established; fd valid for posix_spawn dup2.</summary>
        Established,

        /// <summary>Core process holding inherited TUN fd (SING_BOX_TUN_FD / xray.tun.fd).</summary>
        CoreBound,

        /// <summary>Soft recovery: tearing down before a forced rebind.</summary>
        Rebinding,

        /// <summary>Teardown in progress; fd must not be reused.</summary>
        TearingDown
    }

    /// <summary>Force-rebind when TUN path failed — never RefreshRuntime with a dead fd.</summary>
    public static bool MustForceRebindOnTunPathFailed => true;

    /// <summary>Clear cached fd after failed establish so the next Connect starts Idle→Established.</summary>
    public static bool ClearFdAfterFailedEstablish => true;
}
