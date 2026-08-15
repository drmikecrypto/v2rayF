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
    public void Hy2AnytlsWg_SkippedWithHints()
    {
        var text = """
            hy2://secret@h.example:443#h
            anytls://x@a.example:443#a
            wg://ignored
            vless://aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee@x.com:443?type=tcp#v
            """;
        var result = ConfigImportParser.ParseDetailed(text);
        Assert.Contains(result.Servers, s => s.Protocol == ProxyProtocol.VLESS);
        Assert.DoesNotContain(result.Servers, s => s.Name is "h" or "a");
        Assert.True(result.SkippedCount >= 2);
        Assert.Contains("sing-box", result.SummaryHint, StringComparison.OrdinalIgnoreCase);
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
    public void ClashMeta_ImportsVless_SkipsHy2()
    {
        var yaml = """
            proxies:
              - { name: "ok", type: vless, server: 1.2.3.4, port: 443, uuid: aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee, tls: true, network: ws, servername: sni.example }
              - { name: "hy", type: hysteria2, server: 5.6.7.8, port: 443, password: x }
            proxy-groups:
              - { name: PROXY, type: select, proxies: [ok, hy] }
            """;
        var result = ClashMetaImportParser.Parse(yaml);
        Assert.Single(result.Servers);
        Assert.Equal(ProxyProtocol.VLESS, result.Servers[0].Protocol);
        Assert.Equal("ws", result.Servers[0].Network);
        Assert.True(result.SkippedCount >= 1);
        Assert.Contains("Hysteria2", result.SummaryHint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SingBoxJson_ImportsVless_SkipsTuic()
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
        Assert.Single(result.Servers);
        Assert.Equal(ProxyProtocol.VLESS, result.Servers[0].Protocol);
        Assert.Equal("ws", result.Servers[0].Network);
        Assert.Contains(result.SkipReasons, r => r.Contains("TUIC", StringComparison.OrdinalIgnoreCase));
    }
}
