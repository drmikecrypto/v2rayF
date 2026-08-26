namespace v2rayF.Services;

public enum UpdateCheckStatus
{
    UpToDate,
    Offer,
    TransientError,
    NoAsset
}

public sealed class UpdateCheckResult
{
    public UpdateCheckStatus Status { get; init; }
    public UpdateOffer? Offer { get; init; }
    public string? ErrorMessage { get; init; }

    public static UpdateCheckResult UpToDate() => new() { Status = UpdateCheckStatus.UpToDate };

    public static UpdateCheckResult WithOffer(UpdateOffer offer) => new()
    {
        Status = UpdateCheckStatus.Offer,
        Offer = offer
    };

    public static UpdateCheckResult Transient(string message) => new()
    {
        Status = UpdateCheckStatus.TransientError,
        ErrorMessage = message
    };

    public static UpdateCheckResult MissingAsset(string message) => new()
    {
        Status = UpdateCheckStatus.NoAsset,
        ErrorMessage = message
    };
}
