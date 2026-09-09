using System;

namespace v2rayF.Services;

/// <summary>
/// Pure policy for when Android VPN must be torn down and rebuilt.
/// Aligns with <see cref="AndroidTunLifecycle"/> (Established → Rebinding → Established).
/// </summary>
public static class AndroidTunRebindPolicy
{
    public enum Reason
    {
        /// <summary>TunPathFailed soft recovery — always rebind (drop stale MQTT / dead fd).</summary>
        TunPathFailed,
        /// <summary>Opportunistic session resume — throttle rebinds.</summary>
        SessionResume
    }

    public static bool ShouldForceRebind(Reason reason, DateTimeOffset lastRebindUtc, DateTimeOffset now, int minIntervalSeconds)
    {
        if (reason == Reason.TunPathFailed)
            return AndroidTunLifecycle.MustForceRebindOnTunPathFailed;
        return now - lastRebindUtc >= TimeSpan.FromSeconds(minIntervalSeconds);
    }
}
