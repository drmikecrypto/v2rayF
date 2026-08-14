using System.Text.Json;
using System.Text.Json.Nodes;
using v2rayF.Models;
using v2rayF.Services;

namespace v2rayF.Core.Tests;

/// <summary>
/// Full deep_fix.sh matrix: each of 15 Sentinel links must produce distinct per-transport
/// outbound JSON in both live Build and speedtest BuildServerRuntime paths.
/// </summary>
public class DeepFixMatrixTests
{
    private static readonly string[] SentinelLinks =
    [
        "vless://116e3206-619d-4519-9eb2-5ea66cf9eb63@169.40.32.81:443?security=reality&encryption=none&pbk=78QM5L3u2pbCVIA_y0KSUYawq7qMRPb5146UKHNcyVk&fp=chrome&type=tcp&flow=xtls-rprx-vision&sni=www.yahoo.com&sid=bf16faaa#Sentinel-443-Reality-Vision",
        "vless://aa00f774-4998-441a-a8f6-04ec3500c02c@169.40.32.81:443?security=reality&encryption=none&pbk=78QM5L3u2pbCVIA_y0KSUYawq7qMRPb5146UKHNcyVk&fp=chrome&type=tcp&sni=www.yahoo.com&sid=bf16faaa#Sentinel-443-Reality-Compat",
        "vless://aa00f774-4998-441a-a8f6-04ec3500c02c@169.40.32.81:2052?security=reality&encryption=none&pbk=78QM5L3u2pbCVIA_y0KSUYawq7qMRPb5146UKHNcyVk&fp=chrome&type=tcp&sni=www.yahoo.com&sid=bf16faaa#Sentinel-2052-Reality-Compat",
        "vless://818bd4ea-d00f-46ba-9f1b-85fdf23fbb34@rfau8vd61dcf.dop33.com:8443?security=tls&encryption=none&fp=chrome&type=tcp&flow=xtls-rprx-vision&sni=rfau8vd61dcf.dop33.com#Sentinel-8443-VLESS-TLS-Vision",
        "vless://3be5ab2b-3b13-4d33-893a-df852ebcab21@rfau8vd61dcf.dop33.com:2053?security=tls&encryption=none&fp=chrome&type=ws&host=rfau8vd61dcf.dop33.com&path=%2Fws&sni=rfau8vd61dcf.dop33.com#Sentinel-2053-VLESS-WS-TLS",
        "vless://759276de-0e0d-4251-8896-cefab1413f65@rfau8vd61dcf.dop33.com:2083?security=tls&encryption=none&fp=chrome&type=grpc&serviceName=GunService&mode=gun&sni=rfau8vd61dcf.dop33.com#Sentinel-2083-VLESS-gRPC-TLS",
        "trojan://2d11adaa-b7e0-4406-abd6-6e52368aa143@rfau8vd61dcf.dop33.com:2087?security=tls&fp=chrome&type=tcp&sni=rfau8vd61dcf.dop33.com#Sentinel-2087-Trojan-TLS",
        "trojan://48be3e0f-2b22-4a13-bd93-73f6b55b7f0e@rfau8vd61dcf.dop33.com:2082?security=tls&fp=chrome&type=ws&host=rfau8vd61dcf.dop33.com&path=%2Ftrojan&sni=rfau8vd61dcf.dop33.com#Sentinel-2082-Trojan-WS-TLS",
        "vmess://eyJ2IjoiMiIsInBzIjoiU2VudGluZWwtMjA5Ni1WTWVzcy1XUy1UTFMiLCJhZGQiOiJyZmF1OHZkNjFkY2YuZG9wMzMuY29tIiwicG9ydCI6IjIwOTYiLCJpZCI6IjhkMmFiMDI2LTIwNjEtNDYyOC1hM2UwLTg0MDM1ZDZjYTk3MiIsImFpZCI6IjAiLCJzY3kiOiJhdXRvIiwibmV0Ijoid3MiLCJ0eXBlIjoibm9uZSIsImhvc3QiOiJyZmF1OHZkNjFkY2YuZG9wMzMuY29tIiwicGF0aCI6Ii92bWVzcyIsInRscyI6InRscyIsInNuaSI6InJmYXU4dmQ2MWRjZi5kb3AzMy5jb20iLCJmcCI6ImNocm9tZSJ9",
        "vless://21434149-7fad-47c0-aff4-32ee16cc2ba6@rfau8vd61dcf.dop33.com:2086?security=tls&encryption=none&fp=chrome&type=httpupgrade&host=rfau8vd61dcf.dop33.com&path=%2Fhup&sni=rfau8vd61dcf.dop33.com#Sentinel-2086-VLESS-HTTPUpgrade-TLS",
        "vless://708bee5c-1486-4001-8439-0bf66c402487@rfau8vd61dcf.dop33.com:80?security=none&encryption=none&type=ws&host=rfau8vd61dcf.dop33.com&path=%2Fws#Sentinel-80-VLESS-WS",
        "vless://708bee5c-1486-4001-8439-0bf66c402487@rfau8vd61dcf.dop33.com:8080?security=none&encryption=none&type=ws&host=rfau8vd61dcf.dop33.com&path=%2Fws#Sentinel-8080-VLESS-WS",
        "vless://a8103b01-0fb7-4353-9839-a1bea3db010f@169.40.32.81:3306?security=none&encryption=none&type=tcp#Sentinel-3306-VLESS-TCP",
        "ss://YWVzLTEyOC1nY206aXdFR2hYSmh3Zk13SkxYVw==@169.40.32.81:8880#Sentinel-8880-Shadowsocks",
        "vless://7bae5a78-2af3-4714-b27f-d6ccd7db2d65@169.40.32.81:53?security=none&encryption=none&type=tcp#Sentinel-53-VLESS-TCP"
    ];

    private static List<ProxyServer> ParseMatrix() =>
        SentinelLinks.SelectMany(l => ConfigImportParser.Parse(l)).ToList();

    [Fact]
    public void Matrix_ParsesFifteenDistinctServers()
    {
        var servers = ParseMatrix();
        Assert.Equal(15, servers.Count);
        Assert.Equal(15, servers.Select(s => s.Id).Distinct().Count());
    }

    [Fact]
    public void Matrix_EachLink_ProducesDistinctOutboundJson()
    {
        var settings = new AppSettings();
        var outboundJson = new HashSet<string>(StringComparer.Ordinal);

        foreach (var server in ParseMatrix())
        {
            var build = JsonNode.Parse(XrayConfigBuilder.Build(server, settings))!;
            var proxy = build["outbounds"]!.AsArray().First(o => o!["tag"]?.GetValue<string>() == "proxy")!;
            var json = proxy.ToJsonString();
            Assert.True(outboundJson.Add(json), $"Duplicate outbound for {server.Name}");
        }
    }

    [Fact]
    public void Matrix_SpeedtestAndLive_ShareStreamSettings()
    {
        var settings = new AppSettings();
        foreach (var server in ParseMatrix())
        {
            var live = JsonNode.Parse(XrayConfigBuilder.Build(server, settings))!;
            var speed = JsonNode.Parse(XrayConfigBuilder.BuildSpeedtest(server))!;

            var liveProxy = live["outbounds"]!.AsArray().First(o => o!["tag"]?.GetValue<string>() == "proxy")!;
            var speedProxy = speed["outbounds"]!.AsArray().First(o => o!["tag"]?.GetValue<string>() == "proxy")!;

            Assert.Equal(
                liveProxy["protocol"]!.GetValue<string>(),
                speedProxy["protocol"]!.GetValue<string>());

            if (server.Protocol == ProxyProtocol.Shadowsocks)
            {
                Assert.Equal(
                    liveProxy["settings"]!.ToJsonString(),
                    speedProxy["settings"]!.ToJsonString());
                continue;
            }

            var liveStream = liveProxy["streamSettings"]!.ToJsonString();
            var speedStream = speedProxy["streamSettings"]!.ToJsonString();
            Assert.True(
                JsonSerializer.Serialize(JsonDocument.Parse(liveStream)) ==
                JsonSerializer.Serialize(JsonDocument.Parse(speedStream)),
                $"Stream mismatch for {server.Name}");
        }
    }

    [Fact]
    public void Matrix_DomainNodes_SpeedtestIncludesOutboundHostDnsBootstrap()
    {
        var domainServers = ParseMatrix()
            .Where(s => !System.Net.IPAddress.TryParse(s.Address, out _))
            .ToList();

        Assert.NotEmpty(domainServers);

        foreach (var server in domainServers)
        {
            var speed = JsonNode.Parse(XrayConfigBuilder.BuildSpeedtest(server))!;
            var dnsServers = speed["dns"]!["servers"]!.AsArray();
            var bootstrap = dnsServers
                .Select(n => n as JsonObject)
                .FirstOrDefault(o => o?["domains"] is JsonArray);

            Assert.NotNull(bootstrap);
            var domains = bootstrap!["domains"]!.AsArray().Select(d => d!.GetValue<string>()).ToList();
            Assert.Contains($"full:{server.Address}", domains);
        }
    }

    [Theory]
    [InlineData("Sentinel-443-Reality-Vision", "vless", "tcp", "reality")]
    [InlineData("Sentinel-2053-VLESS-WS-TLS", "vless", "ws", "tls")]
    [InlineData("Sentinel-2083-VLESS-gRPC-TLS", "vless", "grpc", "tls")]
    [InlineData("Sentinel-2087-Trojan-TLS", "trojan", "tcp", "tls")]
    [InlineData("Sentinel-2082-Trojan-WS-TLS", "trojan", "ws", "tls")]
    [InlineData("Sentinel-2096-VMess-WS-TLS", "vmess", "ws", "tls")]
    [InlineData("Sentinel-2086-VLESS-HTTPUpgrade-TLS", "vless", "httpupgrade", "tls")]
    [InlineData("Sentinel-80-VLESS-WS", "vless", "ws", "none")]
    public void Matrix_StreamSettings_MatchExpected(
        string name,
        string protocol,
        string network,
        string security)
    {
        var server = ParseMatrix().First(s => s.Name == name);
        var root = JsonNode.Parse(XrayConfigBuilder.BuildSpeedtest(server))!;
        var proxy = root["outbounds"]!.AsArray().First(o => o!["tag"]?.GetValue<string>() == "proxy")!;

        Assert.Equal(protocol, proxy["protocol"]!.GetValue<string>());

        if (protocol == "shadowsocks")
        {
            Assert.NotNull(proxy["settings"]!["servers"]);
            return;
        }

        Assert.Equal(network, proxy["streamSettings"]!["network"]!.GetValue<string>());
        var sec = proxy["streamSettings"]!["security"]?.GetValue<string>() ?? "none";
        Assert.Equal(security, sec);
    }

    [Fact]
    public void Matrix_Shadowsocks_HasMethodAndPassword()
    {
        var ss = ParseMatrix().First(s => s.Name == "Sentinel-8880-Shadowsocks");
        var proxy = JsonNode.Parse(XrayConfigBuilder.BuildSpeedtest(ss))!["outbounds"]!.AsArray()
            .First(o => o!["tag"]?.GetValue<string>() == "proxy")!;
        Assert.Equal("shadowsocks", proxy["protocol"]!.GetValue<string>());
        var serverNode = proxy["settings"]!["servers"]![0]!;
        Assert.Equal("aes-128-gcm", serverNode["method"]!.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(serverNode["password"]!.GetValue<string>()));
    }

    [Fact]
    public void Matrix_DisplayTransport_ShowsProtocolNetworkSecurity()
    {
        var ws = ParseMatrix().First(s => s.Name == "Sentinel-2053-VLESS-WS-TLS");
        Assert.Equal("VLESS · ws · tls", ws.DisplayTransport);

        var trojan = ParseMatrix().First(s => s.Name == "Sentinel-2087-Trojan-TLS");
        Assert.Equal("Trojan · tcp · tls", trojan.DisplayTransport);

        var ss = ParseMatrix().First(s => s.Name == "Sentinel-8880-Shadowsocks");
        Assert.Equal("SS · tcp", ss.DisplayTransport);
    }

    [Fact]
    public void GetCoreReadyWaitMs_IsTransportAware()
    {
        var ws = new ProxyServer { Network = "ws" };
        var grpc = new ProxyServer { Network = "grpc" };
        var reality = new ProxyServer { Network = "tcp", Security = "reality" };
        var plain = new ProxyServer { Network = "tcp", Security = "none" };

        Assert.Equal(3000, LatencyService.GetCoreReadyWaitMs(ws));
        Assert.Equal(4000, LatencyService.GetCoreReadyWaitMs(grpc));
        Assert.Equal(2500, LatencyService.GetCoreReadyWaitMs(reality));
        Assert.Equal(2000, LatencyService.GetCoreReadyWaitMs(plain));
    }

    [Fact]
    public void BuildShortlist_CoversMultipleTransportFamilies()
    {
        var servers = ParseMatrix();
        var tcpResults = servers
            .Select((s, i) => (Server: s, TcpMs: 100 + i))
            .ToArray();
        var reachable = tcpResults.OrderBy(t => t.TcpMs).ToList();

        var shortlist = SmartConnectService.BuildShortlist(tcpResults, reachable);

        Assert.Contains(shortlist, s => s.Name == "Sentinel-2053-VLESS-WS-TLS");
        Assert.Contains(shortlist, s => s.Name == "Sentinel-2083-VLESS-gRPC-TLS");
        Assert.Contains(shortlist, s => s.Name == "Sentinel-2087-Trojan-TLS");
        Assert.Contains(shortlist, s => s.Name == "Sentinel-2096-VMess-WS-TLS");
        Assert.Contains(shortlist, s => s.Name == "Sentinel-8880-Shadowsocks");
    }
}
