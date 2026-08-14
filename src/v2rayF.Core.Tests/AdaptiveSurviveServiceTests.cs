using v2rayF.Models;
using v2rayF.Services;

namespace v2rayF.Core.Tests;

public class AdaptiveSurviveServiceTests
{
    [Fact]
    public void BuildRetryAttempts_OffersFragmentThenSentinel()
    {
        var svc = new AdaptiveSurviveService();
        var settings = new AppSettings
        {
            AdaptiveSurviveEnabled = true,
            EnablePacketFragment = false,
            DnsThroughProxy = false,
            BlockIpv6 = false,
            RoutingMode = RoutingMode.BypassLan
        };

        var attempts = svc.BuildRetryAttempts(settings);
        Assert.NotEmpty(attempts);
        Assert.Contains(attempts, a => a.Tactic == AdaptiveSurviveService.TacticFragment);
        Assert.Contains(attempts, a => a.Tactic == AdaptiveSurviveService.TacticSentinel);
        Assert.All(attempts, a => Assert.NotSame(settings, a.Settings));
        Assert.False(settings.EnablePacketFragment);
    }

    [Fact]
    public void BuildRetryAttempts_Disabled_ReturnsEmpty()
    {
        var svc = new AdaptiveSurviveService();
        Assert.Empty(svc.BuildRetryAttempts(new AppSettings { AdaptiveSurviveEnabled = false }));
    }

    [Fact]
    public void BuildRetryAttempts_Force_WorksWhenDisabled()
    {
        var svc = new AdaptiveSurviveService();
        var settings = new AppSettings
        {
            AdaptiveSurviveEnabled = false,
            EnablePacketFragment = false,
            DnsThroughProxy = false,
            BlockIpv6 = false,
            RoutingMode = RoutingMode.BypassLan
        };

        var attempts = svc.BuildRetryAttempts(settings, force: true);
        Assert.NotEmpty(attempts);
        Assert.Contains(attempts, a => a.Tactic == AdaptiveSurviveService.TacticFragment);
    }

    [Fact]
    public void MaxSurviveCandidates_KeepsRetriesTight()
    {
        Assert.True(AdaptiveSurviveService.MaxSurviveCandidates <= 2);
        Assert.True(SmartConnectService.EarlyExitGoodPeers <= SmartConnectService.MaxProxyPathProbes);
    }
}
