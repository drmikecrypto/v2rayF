namespace v2rayF.Models;

/// <summary>Per-app traffic sample (panel-gated polling only).</summary>
public sealed class AppTrafficSnapshot
{
    public required string Id { get; init; }

    public long RxBytes { get; init; }

    public long TxBytes { get; init; }

    /// <summary>Estimated downlink B/s since previous sample (0 if unknown).</summary>
    public double DownloadBytesPerSec { get; init; }

    /// <summary>Estimated uplink B/s since previous sample (0 if unknown).</summary>
    public double UploadBytesPerSec { get; init; }
}
