using System.Text.Json.Nodes;
using v2rayF.Models;
using v2rayF.Services;

namespace v2rayF.Core.Tests;

public class BestInMarketImportTests
{
    [Fact]
    public void SsPlugin_IsSkippedWithReason()
    {
        var link =
            "ss://YWVzLTI1Ni1nY206cGFzc3dvcmQ@1.2.3.4:8388/?plugin=v2ray-plugin%3Btls#plug";
        var result = ShareLinkParser.ParseBulkDetailed(link);
        Assert.Empty(result.Servers);
        Assert.True(result.SkippedCount >= 1);
        Assert.Contains(result.SkipReasons, r => r.Contains("plugin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Hy2Anytls_ImportedForDualCore()
    {
        var text = """
            hy2://secret@h.example:443#h
            anytls://x@a.example:443#a
            vless://aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee@x.com:443?type=tcp#v
            """;
        var result = ConfigImportParser.ParseDetailed(text);
        Assert.Contains(result.Servers, s => s.Protocol == ProxyProtocol.VLESS);
        Assert.Contains(result.Servers, s => s.Protocol == ProxyProtocol.Hysteria2);
        Assert.Contains(result.Servers, s => s.Protocol == ProxyProtocol.AnyTls);
    }

    [Fact]
    public void WsEarlyData_AndPacketEncoding_ParseAndBuild()
    {
        var link =
            "vless://aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee@ws.example:443" +
            "?type=ws&security=tls&path=%2Fws&host=ws.example&ed=2048&eh=Sec-WebSocket-Protocol&packetEncoding=xudp#ed";
        var server = ShareLinkParser.Parse(link)!;
        Assert.Equal(2048, server.MaxEarlyData);
        Assert.Equal("Sec-WebSocket-Protocol", server.EarlyDataHeaderName);
        Assert.Equal("xudp", server.PacketEncoding);

        var json = JsonNode.Parse(XrayConfigBuilder.Build(server, new AppSettings()))!;
        var proxy = json["outbounds"]!.AsArray().First(o => o!["tag"]?.GetValue<string>() == "proxy")!;
        Assert.Equal(2048, proxy["streamSettings"]!["wsSettings"]!["maxEarlyData"]!.GetValue<int>());
        Assert.Equal("xudp", proxy["settings"]!["vnext"]![0]!["users"]![0]!["packetEncoding"]!.GetValue<string>());
    }

    [Fact]
    public void PacketEncoding_OmittedWhenUnset()
    {
        var vless = new ProxyServer
        {
            Protocol = ProxyProtocol.VLESS,
            Address = "1.2.3.4",
            Port = 443,
            UserId = Guid.NewGuid().ToString(),
            Network = "tcp",
            Security = "tls"
        };
        var vmess = new ProxyServer
        {
            Protocol = ProxyProtocol.VMess,
            Address = "1.2.3.4",
            Port = 443,
            UserId = Guid.NewGuid().ToString(),
            Network = "ws",
            Security = "tls",
            Path = "/ws"
        };

        foreach (var server in new[] { vless, vmess })
        {
            var proxy = JsonNode.Parse(XrayConfigBuilder.Build(server, new AppSettings()))![
                "outbounds"]!.AsArray().First(o => o!["tag"]?.GetValue<string>() == "proxy")!;
            Assert.Null(proxy["settings"]!["vnext"]![0]!["users"]![0]!["packetEncoding"]);
        }
    }

    [Fact]
    public void PacketEncoding_PreservesExplicitLinkValue()
    {
        var link =
            "vless://aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee@1.2.3.4:443" +
            "?type=tcp&security=tls&packetEncoding=packet#p";
        var server = ShareLinkParser.Parse(link)!;
        Assert.Equal("packet", server.PacketEncoding);
        var proxy = JsonNode.Parse(XrayConfigBuilder.Build(server, new AppSettings()))![
            "outbounds"]!.AsArray().First(o => o!["tag"]?.GetValue<string>() == "proxy")!;
        Assert.Equal("packet", proxy["settings"]!["vnext"]![0]!["users"]![0]!["packetEncoding"]!.GetValue<string>());
    }

    [Fact]
    public void ClashMeta_ImportsVlessAndHy2()
    {
        var yaml = """
            proxies:
              - { name: "ok", type: vless, server: 1.2.3.4, port: 443, uuid: aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee, tls: true, network: ws, servername: sni.example }
              - { name: "hy", type: hysteria2, server: 5.6.7.8, port: 443, password: x }
            proxy-groups:
              - { name: PROXY, type: select, proxies: [ok, hy] }
            """;
        var result = ClashMetaImportParser.Parse(yaml);
        Assert.Equal(2, result.Servers.Count);
        Assert.Contains(result.Servers, s => s.Protocol == ProxyProtocol.VLESS);
        Assert.Contains(result.Servers, s => s.Protocol == ProxyProtocol.Hysteria2);
    }

    [Fact]
    public void SingBoxJson_ImportsVlessAndTuic()
    {
        var json = """
            {
              "outbounds": [
                { "type": "vless", "tag": "vl", "server": "1.2.3.4", "server_port": 443, "uuid": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                  "tls": { "enabled": true, "server_name": "sni.example" },
                  "transport": { "type": "ws", "path": "/ws" } },
                { "type": "tuic", "tag": "t", "server": "5.6.7.8", "server_port": 443, "uuid": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", "password": "p" },
                { "type": "direct", "tag": "direct" }
              ]
            }
            """;
        var result = SingBoxJsonImportParser.Parse(json);
        Assert.Equal(2, result.Servers.Count);
        Assert.Contains(result.Servers, s => s.Protocol == ProxyProtocol.VLESS);
        Assert.Contains(result.Servers, s => s.Protocol == ProxyProtocol.Tuic);
    }
}
