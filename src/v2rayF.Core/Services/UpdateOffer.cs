namespace v2rayF.Services;

public sealed class UpdateOffer
{
    public required string Tag { get; init; }
    public required string Version { get; init; }
    public required string DownloadUrl { get; init; }
    public required string AssetFileName { get; init; }

    /// <summary>Optional SHA256 hex of the asset (from GitHub digest or SHA256SUMS).</summary>
    public string? Sha256 { get; init; }
}
