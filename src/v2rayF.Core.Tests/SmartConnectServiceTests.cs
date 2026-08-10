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
        public Task EnsureCoreAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ICoreProcessHost CreateProcessHost() => new ManagedCoreProcessHost();
    }
}
