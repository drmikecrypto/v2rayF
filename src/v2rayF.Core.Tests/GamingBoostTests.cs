using System;
using System.Collections.Generic;
using System.Linq;
using v2rayF.Models;
using v2rayF.Services;

namespace v2rayF.Core.Tests;

public class GamingBoostTests
{
    [Fact]
    public void GamingBoost_SetsFullPosture()
    {
        var s = new AppSettings
        {
            ChromiumHttpProxyAssist = true,
            EnablePacketFragment = true,
            AdaptiveSurviveEnabled = true,
            SmartMultipathEnabled = false,
            SmartConnectEnabled = false
        };
        NetworkProfiles.ApplyGaming(s);
        Assert.True(s.GamingBoostActive);
        Assert.False(s.ChromiumHttpProxyAssist);
        Assert.True(s.EnableTunMode);
        Assert.False(s.EnablePacketFragment);
        Assert.False(s.AdaptiveSurviveEnabled);
        Assert.True(s.SmartMultipathEnabled);
        Assert.True(s.SmartConnectEnabled);
    }

    [Fact]
    public void Daily_ClearsGamingBoost()
    {
        var s = new AppSettings { GamingBoostActive = true };
        NetworkProfiles.ApplyDaily(s);
        Assert.False(s.GamingBoostActive);
        Assert.True(s.ChromiumHttpProxyAssist);
    }

    [Fact]
    public void Iran_ClearsGamingBoost()
    {
        var s = new AppSettings { GamingBoostActive = true };
        NetworkProfiles.ApplyIran(s);
        Assert.False(s.GamingBoostActive);
    }

    [Fact]
    public void ResolveRankProfile_FollowsGamingBoost()
    {
        Assert.Equal(
            SmartConnectService.RankProfile.Default,
            SmartConnectService.ResolveRankProfile(new AppSettings()));
        Assert.Equal(
            SmartConnectService.RankProfile.Gaming,
            SmartConnectService.ResolveRankProfile(new AppSettings { GamingBoostActive = true }));
    }

    [Fact]
    public void GamingScore_PrefersUdpNativeOverNearbyVision()
    {
        var hy2 = new ProxyServer
        {
            Id = Guid.NewGuid(),
            Name = "hy2",
            Protocol = ProxyProtocol.Hysteria2,
            Address = "1.1.1.1",
            Port = 443
        };
        var vision = new ProxyServer
        {
            Id = Guid.NewGuid(),
            Name = "vision",
            Protocol = ProxyProtocol.VLESS,
            Address = "1.1.1.2",
            Port = 443,
            Security = "reality",
            Flow = "xtls-rprx-vision",
            PublicKey = "pk"
        };

        var ranked = new List<SmartConnectService.RankedServer>
        {
            new(vision, 50, 50, true, 40),
            new(hy2, 55, 55, true, 42)
        };

        var adjusted = SmartConnectService.ApplyGamingScoreAdjustments(ranked);
        Assert.Equal(hy2.Id, adjusted[0].Server.Id);
        Assert.True(adjusted[0].Score < adjusted[1].Score);
    }

    [Fact]
    public void ObservatoryInterval_FasterWhenGamingMultipath()
    {
        Assert.Equal("1m", XrayConfigBuilder.ResolveObservatoryInterval(new AppSettings()));
        Assert.Equal(
            "15s",
            XrayConfigBuilder.ResolveObservatoryInterval(new AppSettings
            {
                GamingBoostActive = true,
                SmartMultipathEnabled = true
            }));
        Assert.Equal(
            "1m",
            XrayConfigBuilder.ResolveObservatoryInterval(new AppSettings
            {
                GamingBoostActive = true,
                SmartMultipathEnabled = false
            }));
    }

    [Fact]
    public void PathHealthInterval_TighterWhenGaming()
    {
        Assert.Equal(
            ProxyCoreService.GamingPathHealthIntervalMs,
            ProxyCoreService.ResolvePathHealthIntervalMs(trafficFlat: true, gamingBoost: true));
        Assert.Equal(
            ProxyCoreService.GamingActivePathHealthIntervalMs,
            ProxyCoreService.ResolvePathHealthIntervalMs(trafficFlat: false, gamingBoost: true));
        Assert.Equal(
            ProxyCoreService.PathHealthIntervalMs,
            ProxyCoreService.ResolvePathHealthIntervalMs(trafficFlat: true, gamingBoost: false));
    }

    [Fact]
    public void GameCatalog_LoadsEmbeddedEntries()
    {
        GameCatalogService.ResetCacheForTests();
        Assert.NotEmpty(GameCatalogService.Entries);
        Assert.Contains(GameCatalogService.Entries, e => e.Id == "discord");
    }

    [Fact]
    public void GameCatalog_ApplyToDirect_MergesPackages()
    {
        GameCatalogService.ResetCacheForTests();
        var s = new AppSettings();
        var n = GameCatalogService.ApplyToDirect(s, mobile: true, entryIds: ["discord"]);
        Assert.True(n > 0);
        Assert.Contains("com.discord", s.AndroidBypassPackages, StringComparison.OrdinalIgnoreCase);
        var again = GameCatalogService.ApplyToDirect(s, mobile: true, entryIds: ["discord"]);
        Assert.Equal(0, again);
    }

    [Fact]
    public void DualCore_GamingStillDisablesAssist()
    {
        var gaming = new AppSettings();
        NetworkProfiles.ApplyGaming(gaming);
        Assert.False(gaming.ChromiumHttpProxyAssist);
        Assert.True(gaming.GamingBoostActive);
    }
}
