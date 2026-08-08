using v2rayF.Services;

namespace v2rayF.Core.Tests;

public class AppVersionTests
{
    [Fact]
    public void ToComparable_TreatsThreeAndFourPartAsEqual()
    {
        Assert.Equal(AppVersion.ToComparable("1.4.3"), AppVersion.ToComparable("1.4.3.0"));
    }

    [Fact]
    public void IsNewerThanCurrent_DetectsRemoteBump()
    {
        AppVersion.OverrideCurrent("1.4.2");
        try
        {
            Assert.True(AppVersion.IsNewerThanCurrent("v1.4.3"));
            Assert.False(AppVersion.IsNewerThanCurrent("1.4.2"));
            Assert.False(AppVersion.IsNewerThanCurrent("1.4.2.0"));
            Assert.False(AppVersion.IsNewerThanCurrent("v1.4.1"));
        }
        finally
        {
            AppVersion.OverrideCurrent("");
        }
    }

    [Fact]
    public void Normalize_StripsPrefixAndMetadata()
    {
        Assert.Equal("1.4.3", AppVersion.Normalize("v1.4.3-beta+git"));
    }
}
