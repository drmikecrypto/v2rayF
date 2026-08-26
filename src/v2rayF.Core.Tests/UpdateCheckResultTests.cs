using v2rayF.Services;

namespace v2rayF.Core.Tests;

public class UpdateCheckResultTests
{
    [Fact]
    public void Factories_SetExpectedStatus()
    {
        Assert.Equal(UpdateCheckStatus.UpToDate, UpdateCheckResult.UpToDate().Status);
        Assert.Equal(UpdateCheckStatus.TransientError, UpdateCheckResult.Transient("x").Status);
        Assert.Equal("x", UpdateCheckResult.Transient("x").ErrorMessage);
        Assert.Equal(UpdateCheckStatus.NoAsset, UpdateCheckResult.MissingAsset("y").Status);

        var offer = new UpdateOffer
        {
            Version = "2.6.1",
            Tag = "v2.6.1",
            DownloadUrl = "https://github.com/drmikecrypto/v2rayF/releases/download/v2.6.1/x.zip",
            AssetFileName = "x.zip"
        };
        var withOffer = UpdateCheckResult.WithOffer(offer);
        Assert.Equal(UpdateCheckStatus.Offer, withOffer.Status);
        Assert.Same(offer, withOffer.Offer);
    }

    [Fact]
    public void DownloadHelper_MaxAttempts_IsThree()
    {
        Assert.Equal(3, UpdateDownloadHelper.MaxDownloadAttempts);
    }
}
