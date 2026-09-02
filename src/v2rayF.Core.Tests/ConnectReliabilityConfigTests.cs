using System.Text.Json.Nodes;
using v2rayF.Models;
using v2rayF.Services;

namespace v2rayF.Core.Tests;

public class ConnectReliabilityConfigTests
{
    [Fact]
    public void Build_IncludesOutboundHostDirectDnsAndPublicResolverDirectRules()
    {
        var server = new ProxyServer
        {
            Name = "domain-node",
            Protocol = ProxyProtocol.VLESS,
            Address = "rfau8vd61dcf.dop33.com",
            Port = 443,
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid().ToString(),
            Security = "tls",
            Network = "tcp"
        };

        var settings = new AppSettings { DnsThroughProxy = true };
        var root = JsonNode.Parse(XrayConfigBuilder.Build(server, settings))!.AsObject();

        var dnsServers = root["dns"]!["servers"]!.AsArray();
        Assert.True(dnsServers.Count >= 3);

        var bootstrap = dnsServers
            .Select(n => n as JsonObject)
            .FirstOrDefault(o => o?["address"]?.GetValue<string>() == "1.1.1.1" && o["domains"] is not null);
        Assert.NotNull(bootstrap);
        var domains = bootstrap!["domains"]!.AsArray().Select(d => d!.GetValue<string>()).ToList();
        Assert.Contains("full:rfau8vd61dcf.dop33.com", domains);

        var rules = root["routing"]!["rules"]!.AsArray();
        Assert.Contains(rules, r =>
            r!["outboundTag"]?.GetValue<string>() == "direct" &&
            r["ip"] is JsonArray ips &&
            ips.Any(i => i!.GetValue<string>() == "1.1.1.1") &&
            ips.Any(i => i!.GetValue<string>() == "8.8.8.8") &&
            r["inboundTag"] is JsonArray tags &&
            tags.Any(t => t!.GetValue<string>() == "dns-module"));

        Assert.Contains(rules, r =>
            r!["outboundTag"]?.GetValue<string>() == "direct" &&
            r["inboundTag"] is JsonArray tags &&
            tags.Any(t => t!.GetValue<string>() == "dns-module"));

        var socks = root["inbounds"]!.AsArray().First(i => i!["tag"]?.GetValue<string>() == "socks-in")!;
        Assert.True(socks["sniffing"]!["enabled"]!.GetValue<bool>());
        var http = root["inbounds"]!.AsArray().First(i => i!["tag"]?.GetValue<string>() == "http-in")!;
        Assert.True(http["sniffing"]!["enabled"]!.GetValue<bool>());
    }

    [Fact]
    public void Build_DnsThroughProxyFalse_StillRoutesDnsModuleDirect()
    {
        var server = new ProxyServer
        {
            Name = "ip-node",
            Protocol = ProxyProtocol.VLESS,
            Address = "169.40.32.81",
            Port = 443,
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid().ToString(),
            Network = "tcp"
        };

        var root = JsonNode.Parse(XrayConfigBuilder.Build(server, new AppSettings { DnsThroughProxy = false }))!;
        var rules = root["routing"]!["rules"]!.AsArray();
        Assert.Contains(rules, r =>
            r!["outboundTag"]?.GetValue<string>() == "direct" &&
            r["inboundTag"] is JsonArray tags &&
            tags.Any(t => t!.GetValue<string>() == "dns-module"));
    }

    [Fact]
    public void BuildSpeedtest_EphemeralPortAndFragmentDialer()
    {
        var server = new ProxyServer
        {
            Name = "frag",
            Protocol = ProxyProtocol.VLESS,
            Address = "1.2.3.4",
            Port = 443,
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid().ToString(),
            Security = "tls",
            Network = "tcp"
        };

        var speed = JsonNode.Parse(XrayConfigBuilder.BuildSpeedtest(server, socksPort: 34567, enableFragment: true))!;
        Assert.Equal(34567, speed["inbounds"]![0]!["port"]!.GetValue<int>());
        Assert.Contains(speed["outbounds"]!.AsArray(), o => o!["tag"]?.GetValue<string>() == "fragment");
        var proxy = speed["outbounds"]!.AsArray().First(o => o!["tag"]?.GetValue<string>() == "proxy")!;
        Assert.Equal("fragment", proxy["streamSettings"]!["sockopt"]!["dialerProxy"]!.GetValue<string>());
        Assert.Equal(XrayConfigBuilder.TcpKeepAliveIdleSec,
            proxy["streamSettings"]!["sockopt"]!["tcpKeepAliveIdle"]!.GetValue<int>());
        Assert.Equal(XrayConfigBuilder.TcpKeepAliveIntervalSec,
            proxy["streamSettings"]!["sockopt"]!["tcpKeepAliveInterval"]!.GetValue<int>());
    }

    [Fact]
    public void BuildSpeedtest_Vision_SkipsFragmentDialer()
    {
        var server = new ProxyServer
        {
            Name = "vision",
            Protocol = ProxyProtocol.VLESS,
            Address = "1.2.3.4",
            Port = 443,
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid().ToString(),
            Security = "reality",
            Network = "tcp",
            Flow = "xtls-rprx-vision",
            PublicKey = "test"
        };

        var speed = JsonNode.Parse(XrayConfigBuilder.BuildSpeedtest(server, enableFragment: true))!;
        Assert.DoesNotContain(speed["outbounds"]!.AsArray(), o => o!["tag"]?.GetValue<string>() == "fragment");
    }

    [Fact]
    public void BuildSpeedtest_DomainNode_IncludesOutboundHostDnsBootstrap()
    {
        var server = new ProxyServer
        {
            Name = "domain-ws",
            Protocol = ProxyProtocol.VLESS,
            Address = "rfau8vd61dcf.dop33.com",
            Port = 2053,
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid().ToString(),
            Security = "tls",
            Network = "ws",
            Host = "rfau8vd61dcf.dop33.com",
            Path = "/ws"
        };

        var speed = JsonNode.Parse(XrayConfigBuilder.BuildSpeedtest(server))!.AsObject();
        var dnsServers = speed["dns"]!["servers"]!.AsArray();
        var bootstrap = dnsServers
            .Select(n => n as JsonObject)
            .FirstOrDefault(o => o?["domains"] is JsonArray);
        Assert.NotNull(bootstrap);
        var domains = bootstrap!["domains"]!.AsArray().Select(d => d!.GetValue<string>()).ToList();
        Assert.Contains("full:rfau8vd61dcf.dop33.com", domains);
    }
}

public class SmartConnectShortlistTests
{
    [Fact]
    public void BuildShortlist_ReservesRealityAndIncludesFailedDomainTcp()
    {
        var servers = new List<(ProxyServer Server, int TcpMs)>();
        for (var i = 0; i < 8; i++)
        {
            servers.Add((new ProxyServer
            {
                Name = $"tcp-{i}",
                Address = $"1.1.1.{i + 1}",
                Port = 443,
                Id = Guid.NewGuid()
            }, 10 + i));
        }

        var reality = new ProxyServer
        {
            Name = "reality-slow",
            Address = "9.9.9.9",
            Port = 443,
            Id = Guid.NewGuid(),
            Security = "reality"
        };
        servers.Add((reality, 5000));

        var domain = new ProxyServer
        {
            Name = "domain-fail",
            Address = "node.example.com",
            Port = 443,
            Id = Guid.NewGuid()
        };
        servers.Add((domain, int.MaxValue));

        var arr = servers.ToArray();
        var reachable = arr.Where(t => t.TcpMs < int.MaxValue).OrderBy(t => t.TcpMs).ToList();
        var shortlist = SmartConnectService.BuildShortlist(arr, reachable);

        Assert.Contains(shortlist, s => s.Id == reality.Id);
        Assert.Contains(shortlist, s => s.Id == domain.Id);
        Assert.True(shortlist.Count <= SmartConnectService.TcpPrefilterLimit);
    }

    [Fact]
    public void SelectSurviveConnectOrder_ReturnsCandidatesWhenNoProxyPathOk()
    {
        var latency = new LatencyService(new FakeEnv());
        var smart = new SmartConnectService(latency);
        var a = new ProxyServer { Name = "A", Address = "node.example.com", Port = 443, Security = "reality" };
        var b = new ProxyServer { Name = "B", Address = "2.2.2.2", Port = 443 };
        var ranked = new List<SmartConnectService.RankedServer>
        {
            new(a, int.MaxValue - 1, -1, false),
            new(b, int.MaxValue - 1, 40, false)
        };

        Assert.Empty(smart.SelectConnectOrder(ranked, preferred: null, lastGoodServerId: null));
        var survive = smart.SelectSurviveConnectOrder(ranked, preferred: null, lastGoodServerId: null);
        Assert.NotEmpty(survive);
        Assert.Equal(a.Id, survive[0].Id);
    }

    [Fact]
    public void Speedtest_UsesUdpDns_AndSingBoxOmitsEphemeralHttpPort()
    {
        var server = new ProxyServer
        {
            Name = "hy2-node",
            Protocol = ProxyProtocol.Hysteria2,
            Address = "example.com",
            Port = 443,
            Id = Guid.NewGuid(),
            Password = "secret",
            Sni = "www.cloudflare.com"
        };

        var ephemeral = 34567;
        var xrayJson = XrayConfigBuilder.BuildSpeedtest(
            new ProxyServer
            {
                Name = "vless-node",
                Protocol = ProxyProtocol.VLESS,
                Address = "example.com",
                Port = 443,
                Id = Guid.NewGuid(),
                UserId = Guid.NewGuid().ToString(),
                Security = "tls",
                Network = "tcp"
            },
            ephemeral);
        var xrayDns = JsonNode.Parse(xrayJson)!["dns"]!["servers"]!.AsArray()
            .Select(n => n is JsonValue v ? v.GetValue<string>() : n?["address"]?.GetValue<string>() ?? "")
            .ToList();
        Assert.DoesNotContain(xrayDns, a => a.StartsWith("https://", StringComparison.Ordinal));

        var sbPorts = JsonNode.Parse(SingBoxConfigBuilder.BuildSpeedtest(server, ephemeral))!
            ["inbounds"]!.AsArray()
            .Select(i => i!["listen_port"]?.GetValue<int>() ?? -1)
            .ToList();
        Assert.Contains(ephemeral, sbPorts);
        Assert.DoesNotContain(XrayConfigBuilder.HttpPort, sbPorts);

        var livePorts = JsonNode.Parse(
                SingBoxConfigBuilder.Build(server, new AppSettings { DnsThroughProxy = false }))!
            ["inbounds"]!.AsArray()
            .Select(i => i!["listen_port"]?.GetValue<int>() ?? -1)
            .ToList();
        Assert.Contains(XrayConfigBuilder.SocksPort, livePorts);
        Assert.Contains(XrayConfigBuilder.HttpPort, livePorts);
    }

    [Fact]
    public void PreferSingBoxOnAndroid_ClassicUsesSingBoxOnlyWhenMobile()
    {
        var classic = new ProxyServer
        {
            Protocol = ProxyProtocol.VLESS,
            Address = "1.2.3.4",
            Port = 443,
            UserId = Guid.NewGuid().ToString()
        };
        var hy2 = new ProxyServer
        {
            Protocol = ProxyProtocol.Hysteria2,
            Address = "1.2.3.4",
            Port = 443,
            Password = "x"
        };

        var prev = AppServices.Platform;
        try
        {
            AppServices.Platform = new FakePlatform(isMobile: false);
            Assert.False(CoreRuntime.PreferSingBoxOnAndroid(classic));
            Assert.False(CoreRuntime.UseSingBox(classic));
            Assert.True(CoreRuntime.RequiresSingBox(hy2));
            Assert.True(CoreRuntime.UseSingBox(hy2));

            AppServices.Platform = new FakePlatform(isMobile: true);
            Assert.True(CoreRuntime.PreferSingBoxOnAndroid(classic));
            Assert.True(CoreRuntime.UseSingBox(classic));
            Assert.False(CoreRuntime.RequiresSingBox(classic));
            Assert.False(CoreRuntime.UseSingBoxForSpeedtest(classic));
            Assert.True(CoreRuntime.UseSingBox(hy2));
            Assert.True(CoreRuntime.UseSingBoxForSpeedtest(hy2));
        }
        finally
        {
            AppServices.Platform = prev!;
        }
    }

    private sealed class FakePlatform : IPlatformIntegration
    {
        public FakePlatform(bool isMobile) => IsMobile = isMobile;

        public bool IsMobile { get; }
        public bool CanUseTunMode => IsMobile;
        public string TunRequirementMessage => "";
        public string? LastProxyMethod => null;
        public string? LastEstablishError => null;
        public Task<int?> EstablishVpnAsync(
            IReadOnlyList<string>? bypassPackages = null,
            bool blockIpv6 = true,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<int?>(null);
        public Task EnableProxyAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisableProxyAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NotifyVpnReadyAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int?> ProbeTunAppPathAsync(
            CancellationToken cancellationToken = default,
            int timeoutMs = LatencyService.TunAppPathProbeMs) =>
            Task.FromResult<int?>(0);
        public Task PromptBatteryOptimizationIfNeededAsync(
            AppSettings settings,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public string? GetLanIPv4Address() => null;
        public Task<IReadOnlyList<InstalledAppInfo>> GetNetworkAppsAsync(
            bool forceRefresh = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InstalledAppInfo>>([]);
        public Task<IReadOnlyDictionary<string, AppTrafficSnapshot>> GetAppTrafficAsync(
            IReadOnlyList<string> ids,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, AppTrafficSnapshot>>(
                new Dictionary<string, AppTrafficSnapshot>());
    }

    [Fact]
    public void TunMode_WindowsNotificationDomains_RouteViaProxy()
    {
        var server = ShareLinkParser.Parse("vless://aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee@x.com:443?type=tcp#v")!;
        var settings = new AppSettings { EnableTunMode = true, AllowDesktopNotificationRouting = true };
        var rules = JsonNode.Parse(XrayConfigBuilder.Build(server, settings))!["routing"]!["rules"]!.AsArray();
        foreach (var suffix in XrayConfigBuilder.DesktopPushDomainSuffixes)
        {
            Assert.Contains(rules, r =>
                r?["domain"] is JsonArray domains &&
                domains.Any(d => d!.GetValue<string>() == $"domain:{suffix}") &&
                r["outboundTag"]?.GetValue<string>() == "proxy");
        }
    }

    [Fact]
    public void TunMode_NotificationRoutingOff_OmitsWnsRules()
    {
        var server = ShareLinkParser.Parse("vless://aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee@x.com:443?type=tcp#v")!;
        var settings = new AppSettings { EnableTunMode = true, AllowDesktopNotificationRouting = false };
        var rules = JsonNode.Parse(XrayConfigBuilder.Build(server, settings))!["routing"]!["rules"]!.AsArray();
        Assert.DoesNotContain(rules, r =>
            r?["domain"] is JsonArray domains &&
            domains.Any(d => d!.GetValue<string>() == "domain:wns.windows.com"));
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
