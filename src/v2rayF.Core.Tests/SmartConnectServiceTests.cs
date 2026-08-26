using v2rayF.Models;
using v2rayF.Services;

namespace v2rayF.Core.Tests;

public class SmartConnectServiceTests
{
    [Fact]
    public void BuildShortlist_PreferredIsFirst()
    {
        var preferred = new ProxyServer { Name = "pref", Address = "9.9.9.9", Port = 443 };
        var a = new ProxyServer { Name = "a", Address = "1.1.1.1", Port = 443 };
        var b = new ProxyServer { Name = "b", Address = "2.2.2.2", Port = 443 };
        var tcp = new[] { (a, 10), (b, 20), (preferred, 50) };
        var reachable = tcp.Where(t => t.Item2 < int.MaxValue).Select(t => (t.Item1, t.Item2)).ToList();
        var shortlist = SmartConnectService.BuildShortlist(tcp, reachable, preferred);
        Assert.Equal(preferred.Id, shortlist[0].Id);
    }

    [Fact]
    public void GetRankProbeTimeout_VisionGetsLongerBudget()
    {
        var vision = new ProxyServer
        {
            Protocol = ProxyProtocol.VLESS,
            Security = "reality",
            Flow = "xtls-rprx-vision",
            Address = "1.1.1.1",
            Port = 443
        };
        var plain = new ProxyServer { Address = "1.1.1.1", Port = 443, Protocol = ProxyProtocol.VLESS };
        Assert.Equal(6000, LatencyService.GetRankProbeTimeoutMs(vision));
        Assert.Equal(4000, LatencyService.GetRankProbeTimeoutMs(plain));
    }

    [Fact]
    public void SelectConnectOrder_OnlyProxyPathOk()
    {
        var latency = new LatencyService(new FakeEnv());
        var smart = new SmartConnectService(latency);

        var a = new ProxyServer { Name = "A", Address = "1.1.1.1", Port = 443 };
        var b = new ProxyServer { Name = "B", Address = "2.2.2.2", Port = 443 };
        var ranked = new List<SmartConnectService.RankedServer>
        {
            new(a, 50, 50, true),
            new(b, 20, 20, false)
        };

        var order = smart.SelectConnectOrder(ranked, preferred: b, lastGoodServerId: null);
        Assert.Single(order);
        Assert.Equal(a.Id, order[0].Id);
    }

    [Fact]
    public void SelectConnectOrder_WithoutBoost_KeepsFastestFirst()
    {
        var smart = new SmartConnectService(new LatencyService(new FakeEnv()));
        var slow = new ProxyServer { Name = "slow", Address = "1.1.1.1", Port = 443 };
        var fast = new ProxyServer { Name = "fast", Address = "2.2.2.2", Port = 443 };
        // Already sorted proxy-path OK + score (same order RankAsync returns).
        var ranked = new List<SmartConnectService.RankedServer>
        {
            new(fast, 20, 20, true),
            new(slow, 100, 100, true)
        };

        var boosted = smart.SelectConnectOrder(ranked, preferred: slow, lastGoodServerId: null);
        Assert.Equal(slow.Id, boosted[0].Id);
        Assert.Equal(fast.Id, boosted[1].Id);

        var trueFastest = smart.SelectConnectOrder(ranked, preferred: null, lastGoodServerId: null);
        Assert.Equal(fast.Id, trueFastest[0].Id);
        Assert.Equal(slow.Id, trueFastest[1].Id);
    }

    [Fact]
    public void SelectSurviveConnectOrder_WithoutBoost_KeepsScoreOrder()
    {
        var smart = new SmartConnectService(new LatencyService(new FakeEnv()));
        var slow = new ProxyServer { Name = "slow", Address = "1.1.1.1", Port = 443 };
        var fast = new ProxyServer { Name = "fast", Address = "node.example.com", Port = 443, Security = "reality" };
        var ranked = new List<SmartConnectService.RankedServer>
        {
            new(fast, 30, 30, true),
            new(slow, 90, 90, true)
        };

        var order = smart.SelectSurviveConnectOrder(ranked, preferred: null, lastGoodServerId: null);
        Assert.Equal(fast.Id, order[0].Id);
    }

    [Fact]
    public void SelectConnectOrder_EmptyWhenNoneOk()
    {
        var smart = new SmartConnectService(new LatencyService(new FakeEnv()));
        var a = new ProxyServer { Name = "A", Address = "1.1.1.1", Port = 443 };
        var ranked = new List<SmartConnectService.RankedServer>
        {
            new(a, int.MaxValue - 1, 230, false)
        };

        Assert.Empty(smart.SelectConnectOrder(ranked, a, null));
    }

    private sealed class FakeEnv : ICoreEnvironment
    {
        public string GetDataDirectory() => Path.GetTempPath();
        public string GetCoresDirectory() => Path.GetTempPath();
        public string GetCorePath() => Path.Combine(Path.GetTempPath(), "missing-xray");
        public string GetSingBoxPath() => Path.Combine(Path.GetTempPath(), "missing-sing-box");
        public Task EnsureCoreAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ICoreProcessHost CreateProcessHost() => new ManagedCoreProcessHost();
    }
}
