namespace v2rayF.Services;

/// <summary>Shared Free-button contract (slots + on-device latency gate).</summary>
public static class PulseFreeConstants
{
    public const string SourceId = "pulse-free";
    public const int MaxSlots = 5;
    public const int MaxLatencyMs = 150;
    public const int ProbeCap = 40;
    public const int CooldownSeconds = 60;
}
