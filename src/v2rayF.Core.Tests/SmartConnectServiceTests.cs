using v2rayF.Models;
using v2rayF.Services;

namespace v2rayF.Core.Tests;

public class SmartConnectServiceTests
{
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
