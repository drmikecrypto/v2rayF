using System;
using System.Threading.Tasks;

namespace v2rayF.Services;

public static class AppServices
{
    public static ICoreEnvironment CoreEnvironment { get; set; } = null!;

    public static IPlatformIntegration Platform { get; set; } = null!;

    public static ICoreProcessHost CoreProcessHost { get; set; } = new ManagedCoreProcessHost();

    public static IAppUpdater? Updater { get; set; }

    public static IKillSwitch KillSwitch { get; set; } = new NullKillSwitch();

    /// <summary>Encrypts secrets at rest. Platform hosts replace the default passthrough.</summary>
    public static ISecretProtector SecretProtector { get; set; } = new PassthroughSecretProtector();

    /// <summary>Called when the Android activity finishes — tear down VPN so network is not left hijacked.</summary>
    public static Func<Task>? EmergencyDisconnectAsync { get; set; }

    /// <summary>Re-check GitHub releases (e.g. after returning from the browser update flow).</summary>
    public static Action? RefreshUpdateCheck { get; set; }

    /// <summary>Verify session after wake/resume (desktop activate / Android OnResume).</summary>
    public static Action? OnSessionResumed { get; set; }

    /// <summary>Periodic VPN validation after successful path health (ReportVpnReady on Android).</summary>
    public static Action? OnVpnKeepalive { get; set; }

    /// <summary>Android VpnService.OnRevoke — system tore down the VPN interface.</summary>
    public static Action? OnVpnRevoked { get; set; }

    /// <summary>Surface updater status into the UI status line.</summary>
    public static Action<string>? ReportStatus { get; set; }

    /// <summary>Live proxy traffic rates (uplink B/s, downlink B/s, optional ping ms) for notification updates.</summary>
    public static Action<long, long, int?>? OnLiveTraffic { get; set; }
}
