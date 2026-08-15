using System.Net;
using System.Text.Json.Nodes;
using v2rayF.Models;
using v2rayF.Services;

namespace v2rayF.Core.Tests;

public class VisionSpeedSettingsTests
{
    [Fact]
    public void VisionWithoutSecurity_InfersTls()
    {
        var link =
            "vless://aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee@tls.example:443" +
            "?flow=xtls-rprx-vision&type=tcp&sni=tls.example#Vision";
        var server = ShareLinkParser.Parse(link)!;
        Assert.Equal("xtls-rprx-vision", server.Flow);
        Assert.Equal("tls", server.Security);
    }

    [Fact]
    public void VisionWithoutSecurity_InfersRealityWhenPbkPresent()
    {
        var link =
            "vless://aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee@r.example:443" +
            "?flow=vision&type=tcp&pbk=publicKeyHere&sni=www.microsoft.com#R";
        var server = ShareLinkParser.Parse(link)!;
        Assert.Equal("reality", server.Security);
        Assert.Equal("xtls-rprx-vision", server.Flow);
    }

    [Fact]
    public void EnsureOutboundReady_EmptyRealityPbk_Throws()
    {
        var server = new ProxyServer
        {
            Protocol = ProxyProtocol.VLESS,
            Address = "1.2.3.4",
            Port = 443,
            UserId = Guid.NewGuid().ToString(),
            Network = "tcp",
            Security = "reality",
            Flow = "xtls-rprx-vision",
            PublicKey = ""
        };

        var ex = Assert.Throws<InvalidOperationException>(() => XrayConfigBuilder.EnsureOutboundReady(server));
        Assert.Contains("public key", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConnectHealthBudget_IsLongerForVision()
    {
        var vision = new ProxyServer { Flow = "xtls-rprx-vision", Security = "reality", Network = "tcp" };
        var plain = new ProxyServer { Network = "tcp", Security = "tls" };
        Assert.Equal(12000, LatencyService.GetConnectHealthProbeMs(vision));
        Assert.Equal(8000, LatencyService.GetConnectHealthProbeMs(plain));
    }

    [Fact]
    public void AdaptiveSurvive_DefaultsOff()
    {
        Assert.False(new AppSettings().AdaptiveSurviveEnabled);
    }

    [Fact]
    public void ConnectHealth_UsesWarmupThenTimedSample()
    {
        Assert.Equal(1, LatencyService.TimedProbeCount);
        Assert.Equal(1, LatencyService.ConnectHealthTimedProbeCount);
        Assert.True(LatencyService.ConnectHealthProbeMs >= 8000);
        Assert.True(LatencyService.ConnectHealthProbeVisionMs >= LatencyService.ConnectHealthProbeMs);
    }

    [Fact]
    public void ShouldSkipProxyPath_IpTcpDead_NotDomain()
    {
        var ip = new ProxyServer { Address = "1.2.3.4", Port = 443, Network = "tcp" };
        var domain = new ProxyServer { Address = "node.example.com", Port = 443, Network = "tcp" };
        var wsIp = new ProxyServer { Address = "1.2.3.4", Port = 443, Network = "ws" };

        Assert.True(LatencyService.ShouldSkipProxyPath(ip, tcpMs: -1));
        Assert.False(LatencyService.ShouldSkipProxyPath(domain, tcpMs: -1));
        Assert.False(LatencyService.ShouldSkipProxyPath(wsIp, tcpMs: -1));
        Assert.False(LatencyService.ShouldSkipProxyPath(ip, tcpMs: 80));
    }

    [Fact]
    public void ServerLatencySort_FastestFirst_TimeoutsLast()
    {
        var a = new ProxyServer { Name = "slow", LatencyMs = 400 };
        var b = new ProxyServer { Name = "fast", LatencyMs = 90 };
        var c = new ProxyServer { Name = "dead", LatencyMs = -1 };
        var ordered = ServerLatencySort.Order([a, b, c]);
        Assert.Equal(["fast", "slow", "dead"], ordered.Select(s => s.Name).ToArray());
    }

    [Fact]
    public void Sniffing_RouteOnlyTrue_AndTunMtu1500()
    {
        var server = new ProxyServer
        {
            Protocol = ProxyProtocol.VLESS,
            Address = "1.2.3.4",
            Port = 443,
            UserId = Guid.NewGuid().ToString(),
            Network = "tcp",
            Security = "tls"
        };
        var json = JsonNode.Parse(XrayConfigBuilder.Build(server, new AppSettings { EnableTunMode = true }))!;
        var socks = json["inbounds"]!.AsArray().First(i => i!["tag"]?.GetValue<string>() == "socks-in")!;
        Assert.True(socks["sniffing"]!["routeOnly"]!.GetValue<bool>());
        var tun = json["inbounds"]!.AsArray().First(i => i!["tag"]?.GetValue<string>() == "tun-in")!;
        Assert.Equal(1500, tun["settings"]!["MTU"]!.GetValue<int>());
        Assert.Equal(1500, XrayConfigBuilder.TunMtu);
    }

    [Fact]
    public void BuildStream_QuicUsesKeyAndSecurity()
    {
        var server = new ProxyServer
        {
            Network = "quic",
            Security = "none",
            QuicSecurity = "aes-128-gcm",
            QuicKey = "secret",
            HeaderType = "none",
            Address = "1.1.1.1"
        };
        var stream = XrayConfigBuilder.BuildStreamSettings(server);
        Assert.Equal("aes-128-gcm", stream["quicSettings"]!["security"]!.GetValue<string>());
        Assert.Equal("secret", stream["quicSettings"]!["key"]!.GetValue<string>());
    }

    [Fact]
    public void Parse_SsPluginQuery_DoesNotBreakHostPort()
    {
        var user = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("aes-128-gcm:password"));
        var link = $"ss://{user}@example.com:8388/?plugin=obfs-local%3Bobfs%3Dhttp#ss1";
        var server = ShareLinkParser.Parse(link)!;
        Assert.Equal("example.com", server.Address);
        Assert.Equal(8388, server.Port);
        Assert.Equal("aes-128-gcm", server.Cipher);
    }

    [Fact]
    public void Parse_VmessQueryUri()
    {
        var link =
            "vmess://aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee@vm.example:443" +
            "?encryption=auto&security=tls&type=ws&path=%2Fws&host=vm.example#Q";
        var server = ShareLinkParser.Parse(link)!;
        Assert.Equal(ProxyProtocol.VMess, server.Protocol);
        Assert.Equal("ws", server.Network);
        Assert.Equal("tls", server.Security);
        Assert.Equal("/ws", server.Path);
        Assert.Equal("Q", server.Name);
    }

    [Fact]
    public void PickMultipathPeers_VisionPrimary_StaysAlone()
    {
        var latency = new LatencyService(new FakeEnv());
        var smart = new SmartConnectService(latency);
        var vision = new ProxyServer
        {
            Name = "vision",
            Address = "1.1.1.1",
            Port = 443,
            Flow = "xtls-rprx-vision",
            Security = "reality"
        };
        var other = new ProxyServer { Name = "ws", Address = "2.2.2.2", Port = 443, Network = "ws" };
        var ranked = new List<SmartConnectService.RankedServer>
        {
            new(vision, 50, 50, true, 50),
            new(other, 40, 40, true, 40)
        };
        var peers = smart.PickMultipathPeers(ranked, vision);
        Assert.Single(peers);
        Assert.Equal(vision.Id, peers[0].Id);
    }

    [Fact]
    public void LiveSockopt_OnNonVision()
    {
        var server = new ProxyServer
        {
            Protocol = ProxyProtocol.VLESS,
            Address = "1.2.3.4",
            Port = 443,
            UserId = Guid.NewGuid().ToString(),
            Network = "tcp",
            Security = "tls"
        };
        var json = JsonNode.Parse(XrayConfigBuilder.Build(server, new AppSettings()))!;
        var proxy = json["outbounds"]!.AsArray().First(o => o!["tag"]?.GetValue<string>() == "proxy")!;
        Assert.True(proxy["streamSettings"]!["sockopt"]!["tcpNoDelay"]!.GetValue<bool>());
    }

    [Fact]
    public void LiveSockopt_SkippedOnVision()
    {
        var server = new ProxyServer
        {
            Protocol = ProxyProtocol.VLESS,
            Address = "1.2.3.4",
            Port = 443,
            UserId = Guid.NewGuid().ToString(),
            Network = "tcp",
            Security = "reality",
            Flow = "xtls-rprx-vision",
            PublicKey = "pk"
        };
        var json = JsonNode.Parse(XrayConfigBuilder.Build(server, new AppSettings()))!;
        var proxy = json["outbounds"]!.AsArray().First(o => o!["tag"]?.GetValue<string>() == "proxy")!;
        Assert.Null(proxy["streamSettings"]!["sockopt"]);
    }

    [Fact]
    public void Import_SkipsHy2_WithHint()
    {
        var text = "hy2://secret@example.com:443#h\nvless://aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee@x.com:443?type=tcp#v";
        var servers = ConfigImportParser.Parse(text);
        Assert.Contains(servers, s => s.Protocol == ProxyProtocol.VLESS);
        Assert.DoesNotContain(servers, s => s.Name == "h");
        Assert.Contains("sing-box", ConfigImportParser.LastSkippedSingBoxHint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FragmentSkip_UsesVisionContains()
    {
        var server = new ProxyServer { Flow = "XTLS-RPRX-VISION" };
        Assert.False(AdaptiveSurviveService.ShouldApplyFragmentForServer(server, fragmentEnabled: true));
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
