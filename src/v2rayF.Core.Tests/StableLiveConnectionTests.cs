using v2rayF.Models;
using v2rayF.Services;

namespace v2rayF.Core.Tests;

public class StableLiveConnectionTests
{
    [Fact]
    public void HealthSocksFails_RequiresThreeBeforeRaise()
    {
        Assert.False(ProxyCoreService.ShouldRaiseOnSocksFails(0));
        Assert.False(ProxyCoreService.ShouldRaiseOnSocksFails(1));
        Assert.False(ProxyCoreService.ShouldRaiseOnSocksFails(2));
        Assert.True(ProxyCoreService.ShouldRaiseOnSocksFails(3));
        Assert.Equal(3, ProxyCoreService.HealthSocksFailThreshold);
        Assert.Equal(500, ProxyCoreService.HealthPortTimeoutMs);
        Assert.Equal(5000, ProxyCoreService.HealthCheckIntervalMs);
    }

    [Fact]
    public void TrafficStats_PollAndQueryTimeouts_AreGentle()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(5000), TrafficStatsHub.DefaultPollInterval);
        Assert.Equal(5000, (int)TrafficStatsHub.DefaultPollInterval.TotalMilliseconds);
        Assert.Equal(1500, TrafficStatsService.QueryTimeoutMs);
        Assert.Equal(TrafficStatsHub.DefaultPollInterval, new TrafficStatsHub().PollInterval);
    }

    [Fact]
    public void Defaults_SurviveAndDoH_Off()
    {
        var s = new AppSettings();
        Assert.False(s.AdaptiveSurviveEnabled);
        Assert.False(s.DnsThroughProxy);
        Assert.False(s.EnablePacketFragment);
    }

    [Fact]
    public void KillSwitch_OnlyArmedWithTun_IsDocumentedBySettingsGate()
    {
        // Desktop connect arms KS only when KillSwitchEnabled && EnableTunMode.
        // System-proxy-only sessions must not blackhole non-proxy apps.
        Assert.True(new AppSettings().KillSwitchEnabled);
        Assert.False(new AppSettings().EnableTunMode);
    }
}
