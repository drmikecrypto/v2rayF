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
}
