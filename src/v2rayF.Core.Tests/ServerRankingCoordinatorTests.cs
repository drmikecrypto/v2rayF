using v2rayF.Models;
using v2rayF.Services;

namespace v2rayF.Core.Tests;

public class ServerRankingCoordinatorTests
{
    [Fact]
    public void PickFastest_PrefersLowestPositiveLatency()
    {
        var servers = new[]
        {
            new ProxyServer { Name = "slow", LatencyMs = 200 },
            new ProxyServer { Name = "fast", LatencyMs = 50 },
            new ProxyServer { Name = "dead", LatencyMs = -1 }
        };

        var fastest = ServerRankingCoordinator.PickFastest(servers);
        Assert.NotNull(fastest);
        Assert.Equal("fast", fastest!.Name);
    }

    [Fact]
    public void ShouldRunStartupRank_RespectsEnabledAndThrottle()
    {
        var settings = new AppSettings { StartupRankServersEnabled = false };
        Assert.False(ServerRankingCoordinator.ShouldRunStartupRank(settings, DateTimeOffset.UtcNow));

        settings.StartupRankServersEnabled = true;
        Assert.True(ServerRankingCoordinator.ShouldRunStartupRank(settings, DateTimeOffset.UtcNow));

        settings.LastStartupRankUtc = DateTimeOffset.UtcNow.AddMinutes(-5).ToString("O");
        Assert.False(ServerRankingCoordinator.ShouldRunStartupRank(settings, DateTimeOffset.UtcNow));

        settings.LastStartupRankUtc = DateTimeOffset.UtcNow.AddMinutes(-15).ToString("O");
        Assert.True(ServerRankingCoordinator.ShouldRunStartupRank(settings, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void StartupRank_DefaultsOn()
    {
        var settings = new AppSettings();
        Assert.True(settings.StartupRankServersEnabled);
        Assert.True(settings.AllowDesktopNotificationRouting);
    }
}
