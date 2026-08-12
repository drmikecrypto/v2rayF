using System.Text.Json.Nodes;
using v2rayF.Models;
using v2rayF.Services;

namespace v2rayF.Core.Tests;

/// <summary>
/// Regression: multi-line paste / .txt payloads must split into separate servers
/// and produce valid Xray Build + BuildSpeedtest JSON for common Sentinel-style links.
/// </summary>
public class SentinelBulkImportTests
{
    // Exact multi-link sample from import UX plan (15 share links).
    private const string SentinelBulkPaste = """
        vless://116e3206-619d-4519-9eb2-5ea66cf9eb63@169.40.32.81:443?security=reality&encryption=none&pbk=78QM5L3u2pbCVIA_y0KSUYawq7qMRPb5146UKHNcyVk&fp=chrome&type=tcp&flow=xtls-rprx-vision&sni=www.yahoo.com&sid=bf16faaa#Sentinel-443-Reality-Vision
        vless://aa00f774-4998-441a-a8f6-04ec3500c02c@169.40.32.81:443?security=reality&encryption=none&pbk=78QM5L3u2pbCVIA_y0KSUYawq7qMRPb5146UKHNcyVk&fp=chrome&type=tcp&sni=www.yahoo.com&sid=bf16faaa#Sentinel-443-Reality-Compat
        vless://aa00f774-4998-441a-a8f6-04ec3500c02c@169.40.32.81:2052?security=reality&encryption=none&pbk=78QM5L3u2pbCVIA_y0KSUYawq7qMRPb5146UKHNcyVk&fp=chrome&type=tcp&sni=www.yahoo.com&sid=bf16faaa#Sentinel-2052-Reality-Compat
        vless://818bd4ea-d00f-46ba-9f1b-85fdf23fbb34@rfau8vd61dcf.dop33.com:8443?security=tls&encryption=none&fp=chrome&type=tcp&flow=xtls-rprx-vision&sni=rfau8vd61dcf.dop33.com#Sentinel-8443-VLESS-TLS-Vision
        vless://3be5ab2b-3b13-4d33-893a-df852ebcab21@rfau8vd61dcf.dop33.com:2053?security=tls&encryption=none&fp=chrome&type=ws&host=rfau8vd61dcf.dop33.com&path=%2Fws&sni=rfau8vd61dcf.dop33.com#Sentinel-2053-VLESS-WS-TLS
        vless://759276de-0e0d-4251-8896-cefab1413f65@rfau8vd61dcf.dop33.com:2083?security=tls&encryption=none&fp=chrome&type=grpc&serviceName=GunService&mode=gun&sni=rfau8vd61dcf.dop33.com#Sentinel-2083-VLESS-gRPC-TLS
        trojan://2d11adaa-b7e0-4406-abd6-6e52368aa143@rfau8vd61dcf.dop33.com:2087?security=tls&fp=chrome&type=tcp&sni=rfau8vd61dcf.dop33.com#Sentinel-2087-Trojan-TLS
        trojan://48be3e0f-2b22-4a13-bd93-73f6b55b7f0e@rfau8vd61dcf.dop33.com:2082?security=tls&fp=chrome&type=ws&host=rfau8vd61dcf.dop33.com&path=%2Ftrojan&sni=rfau8vd61dcf.dop33.com#Sentinel-2082-Trojan-WS-TLS
        vmess://eyJ2IjoiMiIsInBzIjoiU2VudGluZWwtMjA5Ni1WTWVzcy1XUy1UTFMiLCJhZGQiOiJyZmF1OHZkNjFkY2YuZG9wMzMuY29tIiwicG9ydCI6IjIwOTYiLCJpZCI6IjhkMmFiMDI2LTIwNjEtNDYyOC1hM2UwLTg0MDM1ZDZjYTk3MiIsImFpZCI6IjAiLCJzY3kiOiJhdXRvIiwibmV0Ijoid3MiLCJ0eXBlIjoibm9uZSIsImhvc3QiOiJyZmF1OHZkNjFkY2YuZG9wMzMuY29tIiwicGF0aCI6Ii92bWVzcyIsInRscyI6InRscyIsInNuaSI6InJmYXU4dmQ2MWRjZi5kb3AzMy5jb20iLCJmcCI6ImNocm9tZSJ9
        vless://21434149-7fad-47c0-aff4-32ee16cc2ba6@rfau8vd61dcf.dop33.com:2086?security=tls&encryption=none&fp=chrome&type=httpupgrade&host=rfau8vd61dcf.dop33.com&path=%2Fhup&sni=rfau8vd61dcf.dop33.com#Sentinel-2086-VLESS-HTTPUpgrade-TLS
        vless://708bee5c-1486-4001-8439-0bf66c402487@rfau8vd61dcf.dop33.com:80?security=none&encryption=none&type=ws&host=rfau8vd61dcf.dop33.com&path=%2Fws#Sentinel-80-VLESS-WS
        vless://708bee5c-1486-4001-8439-0bf66c402487@rfau8vd61dcf.dop33.com:8080?security=none&encryption=none&type=ws&host=rfau8vd61dcf.dop33.com&path=%2Fws#Sentinel-8080-VLESS-WS
        vless://a8103b01-0fb7-4353-9839-a1bea3db010f@169.40.32.81:3306?security=none&encryption=none&type=tcp#Sentinel-3306-VLESS-TCP
        ss://YWVzLTEyOC1nY206aXdFR2hYSmh3Zk13SkxYVw==@169.40.32.81:8880#Sentinel-8880-Shadowsocks
        vless://7bae5a78-2af3-4714-b27f-d6ccd7db2d65@169.40.32.81:53?security=none&encryption=none&type=tcp#Sentinel-53-VLESS-TCP
        """;

    [Fact]
    public void ParseBulk_SentinelPaste_YieldsFifteenSeparateServers()
    {
        var servers = ConfigImportParser.Parse(SentinelBulkPaste);
        Assert.Equal(15, servers.Count);

        Assert.Contains(servers, s => s.Name == "Sentinel-443-Reality-Vision");
        Assert.Contains(servers, s => s.Name == "Sentinel-443-Reality-Compat");
        Assert.Contains(servers, s => s.Name == "Sentinel-2052-Reality-Compat");
        Assert.Contains(servers, s => s.Name == "Sentinel-8443-VLESS-TLS-Vision");
        Assert.Contains(servers, s => s.Name == "Sentinel-2053-VLESS-WS-TLS");
        Assert.Contains(servers, s => s.Name == "Sentinel-2083-VLESS-gRPC-TLS");
        Assert.Contains(servers, s => s.Name == "Sentinel-2087-Trojan-TLS");
        Assert.Contains(servers, s => s.Name == "Sentinel-2082-Trojan-WS-TLS");
        Assert.Contains(servers, s => s.Name == "Sentinel-2096-VMess-WS-TLS");
        Assert.Contains(servers, s => s.Name == "Sentinel-2086-VLESS-HTTPUpgrade-TLS");
        Assert.Contains(servers, s => s.Name == "Sentinel-80-VLESS-WS");
        Assert.Contains(servers, s => s.Name == "Sentinel-8080-VLESS-WS");
        Assert.Contains(servers, s => s.Name == "Sentinel-3306-VLESS-TCP");
        Assert.Contains(servers, s => s.Name == "Sentinel-8880-Shadowsocks");
        Assert.Contains(servers, s => s.Name == "Sentinel-53-VLESS-TCP");
    }

    [Fact]
    public void ParseBulk_SentinelPaste_MapsProtocolNetworkSecurity()
    {
        var byName = ConfigImportParser.Parse(SentinelBulkPaste)
            .ToDictionary(s => s.Name, StringComparer.Ordinal);

        var vision = byName["Sentinel-443-Reality-Vision"];
        Assert.Equal(ProxyProtocol.VLESS, vision.Protocol);
        Assert.Equal("tcp", vision.Network);
        Assert.Equal("reality", vision.Security);
        Assert.Equal("xtls-rprx-vision", vision.Flow);
        Assert.Equal("78QM5L3u2pbCVIA_y0KSUYawq7qMRPb5146UKHNcyVk", vision.PublicKey);
        Assert.Equal("bf16faaa", vision.ShortId);
        Assert.Equal("www.yahoo.com", vision.Sni);

        var compat = byName["Sentinel-443-Reality-Compat"];
        Assert.Equal("reality", compat.Security);
        Assert.True(string.IsNullOrEmpty(compat.Flow));

        var ws = byName["Sentinel-2053-VLESS-WS-TLS"];
        Assert.Equal("ws", ws.Network);
        Assert.Equal("tls", ws.Security);
        Assert.Equal("/ws", ws.Path);
        Assert.Equal("rfau8vd61dcf.dop33.com", ws.Host);

        var grpc = byName["Sentinel-2083-VLESS-gRPC-TLS"];
        Assert.Equal("grpc", grpc.Network);
        Assert.Equal("GunService", grpc.ServiceName);
        Assert.Equal("tls", grpc.Security);

        var trojanWs = byName["Sentinel-2082-Trojan-WS-TLS"];
        Assert.Equal(ProxyProtocol.Trojan, trojanWs.Protocol);
        Assert.Equal("ws", trojanWs.Network);
        Assert.Equal("/trojan", trojanWs.Path);

        var vmess = byName["Sentinel-2096-VMess-WS-TLS"];
        Assert.Equal(ProxyProtocol.VMess, vmess.Protocol);
        Assert.Equal("ws", vmess.Network);
        Assert.Equal("tls", vmess.Security);
        Assert.Equal(2096, vmess.Port);

        var httpUpgrade = byName["Sentinel-2086-VLESS-HTTPUpgrade-TLS"];
        Assert.Equal("httpupgrade", httpUpgrade.Network);
        Assert.Equal("/hup", httpUpgrade.Path);

        var ss = byName["Sentinel-8880-Shadowsocks"];
        Assert.Equal(ProxyProtocol.Shadowsocks, ss.Protocol);
        Assert.Equal(8880, ss.Port);
        Assert.False(string.IsNullOrWhiteSpace(ss.Cipher));
        Assert.False(string.IsNullOrWhiteSpace(ss.Password));

        var plain = byName["Sentinel-3306-VLESS-TCP"];
        Assert.Equal("tcp", plain.Network);
        Assert.True(string.IsNullOrEmpty(plain.Security) ||
                    plain.Security.Equals("none", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ParseBytes_TxtFile_SameAsPaste()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(SentinelBulkPaste);
        var fromFile = ConfigImportParser.ParseBytes(bytes, "sentinel.txt");
        var fromPaste = ConfigImportParser.Parse(SentinelBulkPaste);
        Assert.Equal(15, fromFile.Count);
        Assert.Equal(fromPaste.Select(s => s.Name).OrderBy(n => n),
            fromFile.Select(s => s.Name).OrderBy(n => n));
    }

    [Fact]
    public void BuildAndSpeedtest_RealityVision_ContainsRealitySettingsAndDns()
    {
        var server = ConfigImportParser.Parse(SentinelBulkPaste)
            .First(s => s.Name == "Sentinel-443-Reality-Vision");

        var build = JsonNode.Parse(XrayConfigBuilder.Build(server, new AppSettings()))!.AsObject();
        var proxy = build["outbounds"]!.AsArray().First(o => o!["tag"]?.GetValue<string>() == "proxy")!;
        Assert.Equal("reality", proxy["streamSettings"]!["security"]!.GetValue<string>());
        Assert.Equal(
            "78QM5L3u2pbCVIA_y0KSUYawq7qMRPb5146UKHNcyVk",
            proxy["streamSettings"]!["realitySettings"]!["publicKey"]!.GetValue<string>());
        Assert.Equal(
            "xtls-rprx-vision",
            proxy["settings"]!["vnext"]![0]!["users"]![0]!["flow"]!.GetValue<string>());

        var speed = JsonNode.Parse(XrayConfigBuilder.BuildSpeedtest(server))!.AsObject();
        Assert.NotNull(speed["dns"]);
        var dnsServers = speed["dns"]!["servers"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
        Assert.Contains("1.1.1.1", dnsServers);
        Assert.Contains("8.8.8.8", dnsServers);
        var speedProxy = speed["outbounds"]!.AsArray().First(o => o!["tag"]?.GetValue<string>() == "proxy")!;
        Assert.Equal("reality", speedProxy["streamSettings"]!["security"]!.GetValue<string>());
    }

    [Fact]
    public void BuildAndSpeedtest_WsGrpcHttpupgradeSs_ContainExpectedStreamSettings()
    {
        var byName = ConfigImportParser.Parse(SentinelBulkPaste)
            .ToDictionary(s => s.Name, StringComparer.Ordinal);
        var settings = new AppSettings();

        var ws = JsonNode.Parse(XrayConfigBuilder.Build(byName["Sentinel-2053-VLESS-WS-TLS"], settings))!;
        var wsProxy = ws["outbounds"]!.AsArray().First(o => o!["tag"]?.GetValue<string>() == "proxy")!;
        Assert.Equal("ws", wsProxy["streamSettings"]!["network"]!.GetValue<string>());
        Assert.Equal("/ws", wsProxy["streamSettings"]!["wsSettings"]!["path"]!.GetValue<string>());

        var grpc = JsonNode.Parse(XrayConfigBuilder.Build(byName["Sentinel-2083-VLESS-gRPC-TLS"], settings))!;
        var grpcProxy = grpc["outbounds"]!.AsArray().First(o => o!["tag"]?.GetValue<string>() == "proxy")!;
        Assert.Equal("grpc", grpcProxy["streamSettings"]!["network"]!.GetValue<string>());
        Assert.Equal("GunService", grpcProxy["streamSettings"]!["grpcSettings"]!["serviceName"]!.GetValue<string>());

        var hu = JsonNode.Parse(XrayConfigBuilder.Build(byName["Sentinel-2086-VLESS-HTTPUpgrade-TLS"], settings))!;
        var huProxy = hu["outbounds"]!.AsArray().First(o => o!["tag"]?.GetValue<string>() == "proxy")!;
        Assert.Equal("httpupgrade", huProxy["streamSettings"]!["network"]!.GetValue<string>());
        Assert.Equal("/hup", huProxy["streamSettings"]!["httpupgradeSettings"]!["path"]!.GetValue<string>());

        var ss = JsonNode.Parse(XrayConfigBuilder.Build(byName["Sentinel-8880-Shadowsocks"], settings))!;
        var ssProxy = ss["outbounds"]!.AsArray().First(o => o!["tag"]?.GetValue<string>() == "proxy")!;
        Assert.Equal("shadowsocks", ssProxy["protocol"]!.GetValue<string>());

        // Speedtest for a domain-based node must still embed DNS + proxy outbound.
        var speedWs = JsonNode.Parse(XrayConfigBuilder.BuildSpeedtest(byName["Sentinel-2053-VLESS-WS-TLS"]))!;
        Assert.NotNull(speedWs["dns"]);
        Assert.Contains(
            speedWs["outbounds"]!.AsArray(),
            o => o!["tag"]?.GetValue<string>() == "proxy");
    }
}
