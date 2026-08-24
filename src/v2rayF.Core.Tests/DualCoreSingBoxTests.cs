using System.Text.Json.Nodes;
using v2rayF.Models;
using v2rayF.Services;

namespace v2rayF.Core.Tests;

public class DualCoreSingBoxTests
{
    [Fact]
    public void Hy2Link_ParsesAndRequiresSingBox()
    {
        var server = ShareLinkParser.Parse("hy2://secret@h.example:443?sni=h.example#h")!;
        Assert.Equal(ProxyProtocol.Hysteria2, server.Protocol);
        Assert.True(CoreRuntime.RequiresSingBox(server));
        var json = JsonNode.Parse(SingBoxConfigBuilder.Build(server, new AppSettings()))!;
        var proxy = json["outbounds"]!.AsArray().First(o => o!["tag"]?.GetValue<string>() == "proxy")!;
        Assert.Equal("hysteria2", proxy["type"]!.GetValue<string>());
        Assert.Equal("secret", proxy["password"]!.GetValue<string>());
    }

    [Fact]
    public void TuicAndAnyTls_Parse()
    {
        var tuic = ShareLinkParser.Parse(
            "tuic://aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee:pass@t.example:443?sni=t.example&congestion_control=bbr#t")!;
        Assert.Equal(ProxyProtocol.Tuic, tuic.Protocol);
        Assert.Equal("pass", tuic.Password);

        var any = ShareLinkParser.Parse("anytls://pw@a.example:443?sni=a.example#a")!;
        Assert.Equal(ProxyProtocol.AnyTls, any.Protocol);
        Assert.True(CoreRuntime.RequiresSingBox(any));
    }

    [Fact]
    public void WireGuardLink_BuildsOutbound()
    {
        var wg = ShareLinkParser.Parse(
            "wireguard://PRIVATEKEY@1.2.3.4:51820?publickey=PEERPK&address=10.0.0.2/32#wg")!;
        Assert.Equal(ProxyProtocol.WireGuard, wg.Protocol);
        var json = JsonNode.Parse(SingBoxConfigBuilder.Build(wg, new AppSettings()))!;
        var proxy = json["outbounds"]!.AsArray().First(o => o!["tag"]?.GetValue<string>() == "proxy")!;
        Assert.Equal("wireguard", proxy["type"]!.GetValue<string>());
        Assert.Equal("PRIVATEKEY", proxy["private_key"]!.GetValue<string>());
    }

    [Fact]
    public void Import_Hy2_IsKeptForSingBox()
    {
        var text = "hy2://secret@h.example:443#h\nvless://aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee@x.com:443?type=tcp#v";
        var result = ConfigImportParser.ParseDetailed(text);
        Assert.Contains(result.Servers, s => s.Protocol == ProxyProtocol.Hysteria2);
        Assert.Contains(result.Servers, s => s.Protocol == ProxyProtocol.VLESS);
    }

    [Fact]
    public void Clash_ImportsHy2()
    {
        var yaml = """
            proxies:
              - { name: h2, type: hysteria2, server: 5.6.7.8, port: 443, password: x, sni: x.com }
            proxy-groups:
              - { name: PROXY, type: select, proxies: [h2] }
            """;
        var result = ClashMetaImportParser.Parse(yaml);
        Assert.Contains(result.Servers, s => s.Protocol == ProxyProtocol.Hysteria2 && s.Name == "h2");
    }

    [Fact]
    public void Hy2_Bandwidth_ParsesAndEmits()
    {
        var server = ShareLinkParser.Parse(
            "hy2://secret@h.example:443?sni=h.example&upmbps=100&downmbps=500#h")!;
        Assert.Equal(100, server.UpMbps);
        Assert.Equal(500, server.DownMbps);
        var json = JsonNode.Parse(SingBoxConfigBuilder.Build(server, new AppSettings()))!;
        var proxy = json["outbounds"]!.AsArray().First(o => o!["tag"]?.GetValue<string>() == "proxy")!;
        Assert.Equal(100, proxy["up_mbps"]!.GetValue<int>());
        Assert.Equal(500, proxy["down_mbps"]!.GetValue<int>());
    }

    [Fact]
    public void Hy2_NoBandwidth_OmitsMbpsFields()
    {
        var server = ShareLinkParser.Parse("hy2://secret@h.example:443#h")!;
        Assert.Equal(0, server.UpMbps);
        var json = JsonNode.Parse(SingBoxConfigBuilder.Build(server, new AppSettings()))!;
        var proxy = json["outbounds"]!.AsArray().First(o => o!["tag"]?.GetValue<string>() == "proxy")!;
        Assert.Null(proxy["up_mbps"]);
        Assert.Null(proxy["down_mbps"]);
    }

    [Fact]
    public void Clash_Hy2_Bandwidth_AndBuild()
    {
        var yaml = """
            proxies:
              - { name: h2, type: hysteria2, server: 5.6.7.8, port: 443, password: x, sni: x.com, up: 80, down: "200 Mbps" }
            """;
        var result = ClashMetaImportParser.Parse(yaml);
        var h2 = Assert.Single(result.Servers);
        Assert.Equal(80, h2.UpMbps);
        Assert.Equal(200, h2.DownMbps);
        var proxy = JsonNode.Parse(SingBoxConfigBuilder.Build(h2, new AppSettings()))![
            "outbounds"]!.AsArray().First(o => o!["tag"]?.GetValue<string>() == "proxy")!;
        Assert.Equal(80, proxy["up_mbps"]!.GetValue<int>());
        Assert.Equal(200, proxy["down_mbps"]!.GetValue<int>());
    }

    [Fact]
    public void Tuic_UdpRelayMode_Emitted()
    {
        var tuic = ShareLinkParser.Parse(
            "tuic://aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee:pass@t.example:443?congestion_control=bbr&udp_relay_mode=native#t")!;
        Assert.Equal("native", tuic.UdpRelayMode);
        var proxy = JsonNode.Parse(SingBoxConfigBuilder.Build(tuic, new AppSettings()))![
            "outbounds"]!.AsArray().First(o => o!["tag"]?.GetValue<string>() == "proxy")!;
        Assert.Equal("bbr", proxy["congestion_control"]!.GetValue<string>());
        Assert.Equal("native", proxy["udp_relay_mode"]!.GetValue<string>());
    }

    [Fact]
    public void WireGuard_Mtu_FromLink()
    {
        var wg = ShareLinkParser.Parse(
            "wireguard://PRIVATEKEY@1.2.3.4:51820?publickey=PEERPK&address=10.0.0.2/32&mtu=1280#wg")!;
        Assert.Equal(1280, wg.Mtu);
        var proxy = JsonNode.Parse(SingBoxConfigBuilder.Build(wg, new AppSettings()))![
            "outbounds"]!.AsArray().First(o => o!["tag"]?.GetValue<string>() == "proxy")!;
        Assert.Equal(1280, proxy["mtu"]!.GetValue<int>());
    }

    [Fact]
    public void BlockIpv6_AddsIpVersion6BlockRule()
    {
        var server = ShareLinkParser.Parse("hy2://secret@h.example:443#h")!;
        var json = JsonNode.Parse(SingBoxConfigBuilder.Build(server, new AppSettings { BlockIpv6 = true }))!;
        var rules = json["route"]!["rules"]!.AsArray();
        Assert.Contains(rules, r =>
            r!["ip_version"]?.GetValue<int>() == 6 &&
            r["outbound"]?.GetValue<string>() == "block");
    }

    [Fact]
    public void VlessRealityVision_BuildsSingBoxOutbound()
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
            PublicKey = "pk",
            ShortId = "abcd",
            Sni = "www.example.com"
        };
        var json = JsonNode.Parse(SingBoxConfigBuilder.Build(server, new AppSettings()))!;
        var proxy = json["outbounds"]!.AsArray().First(o => o!["tag"]?.GetValue<string>() == "proxy")!;
        Assert.Equal("vless", proxy["type"]!.GetValue<string>());
        Assert.Equal("xtls-rprx-vision", proxy["flow"]!.GetValue<string>());
        Assert.True(proxy["tls"]!["reality"]!["enabled"]!.GetValue<bool>());
        Assert.Equal("pk", proxy["tls"]!["reality"]!["public_key"]!.GetValue<string>());
    }

    [Fact]
    public void VmessWsTls_AndTrojan_AndSs_Build()
    {
        var vmess = new ProxyServer
        {
            Protocol = ProxyProtocol.VMess,
            Address = "1.2.3.4",
            Port = 443,
            UserId = Guid.NewGuid().ToString(),
            Network = "ws",
            Security = "tls",
            Path = "/ws",
            Host = "cdn.example"
        };
        var trojan = new ProxyServer
        {
            Protocol = ProxyProtocol.Trojan,
            Address = "1.2.3.4",
            Port = 443,
            Password = "pw",
            Network = "ws",
            Security = "tls",
            Path = "/t",
            Host = "cdn.example"
        };
        var ss = new ProxyServer
        {
            Protocol = ProxyProtocol.Shadowsocks,
            Address = "1.2.3.4",
            Port = 8388,
            Password = "pw",
            Cipher = "aes-128-gcm"
        };

        Assert.Equal("vmess", JsonNode.Parse(SingBoxConfigBuilder.Build(vmess, new AppSettings()))![
            "outbounds"]!.AsArray().First(o => o!["tag"]?.GetValue<string>() == "proxy")!["type"]!.GetValue<string>());
        Assert.Equal("trojan", JsonNode.Parse(SingBoxConfigBuilder.Build(trojan, new AppSettings()))![
            "outbounds"]!.AsArray().First(o => o!["tag"]?.GetValue<string>() == "proxy")!["type"]!.GetValue<string>());
        Assert.Equal("shadowsocks", JsonNode.Parse(SingBoxConfigBuilder.Build(ss, new AppSettings()))![
            "outbounds"]!.AsArray().First(o => o!["tag"]?.GetValue<string>() == "proxy")!["type"]!.GetValue<string>());
    }

    [Fact]
    public void AndroidTunFd_AddsTunInboundWithoutInvalidFileDescriptorField()
    {
        var server = ShareLinkParser.Parse("hy2://secret@h.example:443#h")!;
        var json = JsonNode.Parse(SingBoxConfigBuilder.Build(server, new AppSettings(), tunFd: 7))!;
        var tun = json["inbounds"]!.AsArray().First(i => i!["tag"]?.GetValue<string>() == "tun-in")!;
        Assert.Equal("tun", tun["type"]!.GetValue<string>());
        Assert.Null(tun["file_descriptor"]);
        Assert.Equal("172.19.0.1/30", tun["address"]!.AsArray()[0]!.GetValue<string>());
        Assert.False(tun["auto_route"]!.GetValue<bool>());
        Assert.Equal("mixed", tun["stack"]!.GetValue<string>());
        Assert.False(tun["sniff_override_destination"]!.GetValue<bool>());
    }

    [Fact]
    public void AndroidTunFd_HijackDnsRulesPrecedePrivateDirect()
    {
        var server = ShareLinkParser.Parse("hy2://secret@h.example:443#h")!;
        var rules = JsonNode.Parse(SingBoxConfigBuilder.Build(server, new AppSettings(), tunFd: 7))!
            ["route"]!["rules"]!.AsArray();

        Assert.Equal("sniff", rules[0]!["action"]!.GetValue<string>());

        var protocolHijackIdx = -1;
        var portHijackIdx = -1;
        var subnetHijackIdx = -1;
        var privateDirectIdx = -1;
        for (var i = 0; i < rules.Count; i++)
        {
            var r = rules[i]!;
            if (r["action"]?.GetValue<string>() == "hijack-dns" &&
                r["protocol"]?.GetValue<string>() == "dns")
                protocolHijackIdx = i;
            if (r["action"]?.GetValue<string>() == "hijack-dns" &&
                r["port"] is JsonArray ports &&
                ports.Any(p => p!.GetValue<int>() == 53))
                portHijackIdx = i;
            if (r["action"]?.GetValue<string>() == "hijack-dns" &&
                r["ip_cidr"] is JsonArray cidrs &&
                cidrs.Any(c => c!.GetValue<string>() == "172.19.0.0/30"))
                subnetHijackIdx = i;
            if (r["ip_is_private"]?.GetValue<bool>() == true &&
                r["outbound"]?.GetValue<string>() == "direct")
                privateDirectIdx = i;
        }

        Assert.True(protocolHijackIdx >= 0, "expected protocol dns hijack-dns rule");
        Assert.True(portHijackIdx >= 0, "expected port 53 hijack-dns rule");
        Assert.True(subnetHijackIdx >= 0, "expected 172.19.0.0/30 hijack-dns rule");
        Assert.True(privateDirectIdx >= 0, "expected ip_is_private direct rule");
        Assert.True(protocolHijackIdx < portHijackIdx);
        Assert.True(portHijackIdx < privateDirectIdx);
        Assert.True(subnetHijackIdx < privateDirectIdx);
    }

    [Fact]
    public void AndroidTunFd_ForcesUdpDnsEvenWhenDohEnabled()
    {
        var server = ShareLinkParser.Parse("hy2://secret@h.example:443#h")!;
        var dns = JsonNode.Parse(
            SingBoxConfigBuilder.Build(server, new AppSettings { DnsThroughProxy = true }, tunFd: 7))!["dns"]!;
        Assert.Equal(SingBoxConfigBuilder.UdpDnsTag, dns["final"]!.GetValue<string>());
        Assert.Contains(
            dns["servers"]!.AsArray(),
            s => s!["tag"]?.GetValue<string>() == SingBoxConfigBuilder.UdpDnsTag &&
                 s["type"]?.GetValue<string>() == "udp");
        Assert.DoesNotContain(
            dns["servers"]!.AsArray(),
            s => s!["tag"]?.GetValue<string>() == SingBoxConfigBuilder.DohDnsTag);
    }

    [Fact]
    public void AndroidTunFd_UsesFakeIpForAppDns()
    {
        var server = ShareLinkParser.Parse("hy2://secret@h.example:443#h")!;
        var dns = JsonNode.Parse(
            SingBoxConfigBuilder.Build(server, new AppSettings { DnsThroughProxy = true }, tunFd: 7))!["dns"]!;

        var fake = Assert.Single(
            dns["servers"]!.AsArray().Where(s => s!["tag"]?.GetValue<string>() == SingBoxConfigBuilder.FakeIpDnsTag));
        Assert.Equal("fakeip", fake!["type"]!.GetValue<string>());
        Assert.Equal(SingBoxConfigBuilder.FakeIpInet4Range, fake["inet4_range"]!.GetValue<string>());
        Assert.True(dns["independent_cache"]!.GetValue<bool>());

        var dnsRules = dns["rules"]!.AsArray();
        var metaIdx = -1;
        var fakeIdx = -1;
        for (var i = 0; i < dnsRules.Count; i++)
        {
            var r = dnsRules[i]!;
            if (r["server"]?.GetValue<string>() == SingBoxConfigBuilder.UdpDnsTag &&
                r["domain_suffix"] is JsonArray suffixes &&
                suffixes.Any(s => s!.GetValue<string>() == "instagram.com"))
                metaIdx = i;
            if (r["server"]?.GetValue<string>() == SingBoxConfigBuilder.FakeIpDnsTag &&
                r["query_type"] is JsonArray qt &&
                qt.Any(t => t!.GetValue<string>() == "A") &&
                r["domain_suffix"] is null)
                fakeIdx = i;
        }

        Assert.True(metaIdx >= 0, "expected Meta domain_suffix → udp before FakeIP");
        Assert.True(fakeIdx >= 0, "expected catch-all FakeIP A/AAAA rule");
        Assert.True(metaIdx < fakeIdx);

        Assert.Contains(
            dnsRules,
            r => r!["server"]?.GetValue<string>() == SingBoxConfigBuilder.FakeIpDnsTag &&
                 r["query_type"] is JsonArray qt &&
                 qt.Any(t => t!.GetValue<string>() == "A") &&
                 qt.Any(t => t!.GetValue<string>() == "AAAA"));
    }

    [Fact]
    public void MetaHttpProxyExclusions_MirrorDnsSuffixes()
    {
        var excl = SingBoxConfigBuilder.GetMetaHttpProxyExclusions();
        Assert.Contains("*.instagram.com", excl);
        Assert.Contains("instagram.com", excl);
        Assert.Contains("*.facebook.com", excl);
        Assert.Contains("facebook.com", excl);
        foreach (var suffix in SingBoxConfigBuilder.MetaDnsSuffixes)
        {
            Assert.Contains("*." + suffix, excl);
            Assert.Contains(suffix, excl);
        }

        Assert.Equal(
            SingBoxConfigBuilder.MetaDnsSuffixes.Length * 2 +
            SingBoxConfigBuilder.MetaDnsExactHosts.Length * 2,
            excl.Count);
    }

    [Fact]
    public void AndroidTunFd_UsesMixedStackAndBlocksQuic()
    {
        var server = ShareLinkParser.Parse("vless://aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee@x.com:443?type=tcp#v")!;
        var root = JsonNode.Parse(SingBoxConfigBuilder.Build(server, new AppSettings(), tunFd: 3))!;
        var tun = root["inbounds"]!.AsArray()
            .First(i => i!["tag"]?.GetValue<string>() == "tun-in")!;
        Assert.Equal("mixed", tun["stack"]!.GetValue<string>());

        var rules = root["route"]!["rules"]!.AsArray();
        var quicBlock = rules.FirstOrDefault(r =>
            r?["inbound"] is JsonArray &&
            r["protocol"]?.GetValue<string>() == "quic" &&
            r["outbound"]?.GetValue<string>() == "block");
        Assert.NotNull(quicBlock);
    }

    [Fact]
    public void AndroidTunFd_GoogleDnsBeforeFakeIp()
    {
        var server = ShareLinkParser.Parse("vless://aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee@x.com:443?type=tcp#v")!;
        var dns = JsonNode.Parse(SingBoxConfigBuilder.Build(server, new AppSettings(), tunFd: 3))!["dns"]!;
        var dnsRules = dns["rules"]!.AsArray();

        var googleIdx = -1;
        var fakeIdx = -1;
        for (var i = 0; i < dnsRules.Count; i++)
        {
            var r = dnsRules[i]!;
            if (r["server"]?.GetValue<string>() == SingBoxConfigBuilder.UdpDnsTag &&
                r["domain_suffix"] is JsonArray gs &&
                gs.Any(s => s!.GetValue<string>() == "google.com"))
                googleIdx = i;
            if (r["server"]?.GetValue<string>() == SingBoxConfigBuilder.FakeIpDnsTag &&
                r["domain_suffix"] is null)
                fakeIdx = i;
        }

        Assert.True(googleIdx >= 0, "expected Google domain_suffix → udp before FakeIP");
        Assert.True(fakeIdx >= 0, "expected FakeIP catch-all");
        Assert.True(googleIdx < fakeIdx);
    }

    [Fact]
    public void BuildWithoutTun_OmitsHijackDnsRules()
    {
        var server = ShareLinkParser.Parse("hy2://secret@h.example:443#h")!;
        var root = JsonNode.Parse(SingBoxConfigBuilder.Build(server, new AppSettings { DnsThroughProxy = true }))!;
        var rules = root["route"]!["rules"]!.AsArray();
        Assert.DoesNotContain(rules, r => r!["action"]?.GetValue<string>() == "hijack-dns");
        Assert.DoesNotContain(rules, r => r!["action"]?.GetValue<string>() == "sniff");
        Assert.Equal(SingBoxConfigBuilder.DohDnsTag, root["dns"]!["final"]!.GetValue<string>());
        Assert.DoesNotContain(
            root["dns"]!["servers"]!.AsArray(),
            s => s!["tag"]?.GetValue<string>() == SingBoxConfigBuilder.FakeIpDnsTag);
    }

    [Fact]
    public void ConnectHealthBudgets_AreSoftened()
    {
        Assert.Equal(12000, LatencyService.ConnectHealthProbeMs);
        Assert.Equal(16000, LatencyService.ConnectHealthProbeVisionMs);
    }

    [Fact]
    public void DnsThroughProxy_DefaultsOn()
    {
        Assert.True(new AppSettings().DnsThroughProxy);
    }
}
