namespace v2rayF.Services;

/// <summary>Shared Free-button contract (slots + on-device latency gate).</summary>
public static class PulseFreeConstants
{
    public const string SourceId = "pulse-free";
    public const int MaxSlots = 5;
    /// <summary>Preferred Free latency (shown as the “fast” target).</summary>
    public const int MaxLatencyMs = 150;
    /// <summary>
    /// Hard accept ceiling when preferred slots cannot be filled (Iran/China RTT to overseas
    /// edges is often 150–400ms even when the proxy works).
    /// </summary>
    public const int AcceptLatencyMs = 450;
    public const int ProbeCap = 40;
    public const int CooldownSeconds = 60;

    /// <summary>Default Worker mirror — Iran-reachable; override via AppSettings.PulseWorkerBase.</summary>
    public const string DefaultWorkerBase = "https://pulseconfigs-mirror.drmikecrypto.workers.dev";
}
