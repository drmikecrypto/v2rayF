using System.Text.Json.Nodes;
using v2rayF.Models;
using v2rayF.Services;

namespace v2rayF.Core.Tests;

public class ConnectReadyWaitTests
{
    [Fact]
    public void GetConnectReadyWaitMs_SingBoxTun_ExceedsSpeedtestWait()
    {
        var server = new ProxyServer
        {
            Name = "ws-node",
            Protocol = ProxyProtocol.VLESS,
            Address = "example.com",
            Port = 443,
            UserId = Guid.NewGuid().ToString(),
            Security = "tls",
            Network = "ws"
        };

        var speedtestWait = LatencyService.GetCoreReadyWaitMs(server);
        var connectWait = ProxyCoreService.GetConnectReadyWaitMs(server, useSingBox: true, tunFd: 42);

        Assert.True(connectWait > speedtestWait);
        Assert.Equal(
            Math.Min(ProxyCoreService.ConnectTimeoutMs, speedtestWait + ProxyCoreService.SingBoxTunReadyBonusMs),
            connectWait);
    }

    [Fact]
    public void GetConnectReadyWaitMs_NeverExceedsConnectTimeout()
    {
        var grpc = new ProxyServer
        {
            Protocol = ProxyProtocol.VLESS,
            Address = "1.2.3.4",
            Port = 443,
            UserId = Guid.NewGuid().ToString(),
            Network = "grpc"
        };

        var wait = ProxyCoreService.GetConnectReadyWaitMs(grpc, useSingBox: true, tunFd: 3);
        Assert.True(wait <= ProxyCoreService.ConnectTimeoutMs);
    }

    [Fact]
    public void GetConnectReadyWaitMs_XrayWithoutTun_UsesTransportBudgetOnly()
    {
        var reality = new ProxyServer
        {
            Protocol = ProxyProtocol.VLESS,
            Address = "1.2.3.4",
            Port = 443,
            UserId = Guid.NewGuid().ToString(),
            Security = "reality",
            Network = "tcp"
        };

        var wait = ProxyCoreService.GetConnectReadyWaitMs(reality, useSingBox: false, tunFd: null);
        Assert.Equal(LatencyService.GetCoreReadyWaitMs(reality), wait);
    }

    [Fact]
    public void SingBoxLiveConfig_WithTun_DisablesAutoDetectInterface()
    {
        var server = new ProxyServer
        {
            Protocol = ProxyProtocol.VLESS,
            Address = "1.2.3.4",
            Port = 443,
            UserId = Guid.NewGuid().ToString(),
            Security = "tls",
            Network = "tcp"
        };

        var withTun = JsonNode.Parse(SingBoxConfigBuilder.Build(server, new AppSettings(), tunFd: 5))!;
        var withoutTun = JsonNode.Parse(SingBoxConfigBuilder.Build(server, new AppSettings()))!;

        Assert.False(withTun["route"]!["auto_detect_interface"]!.GetValue<bool>());
        Assert.True(withoutTun["route"]!["auto_detect_interface"]!.GetValue<bool>());
        var tun = withTun["inbounds"]!.AsArray().First(i => i!["tag"]?.GetValue<string>() == "tun-in")!;
        Assert.Null(tun["file_descriptor"]);
        Assert.Contains("172.19.0.1/30", tun["address"]!.AsArray().Select(a => a!.GetValue<string>()));
    }

    [Fact]
    public void CoreStartupErrorFormatter_DecodeConfig_IsActionable()
    {
        var msg = CoreStartupErrorFormatter.Format("FATAL decode config: invalid field tun");
        Assert.Contains("config", msg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Xray core exited", msg, StringComparison.Ordinal);
    }
}
