using System.Text.Json.Nodes;
using v2rayF.Models;
using v2rayF.Services;

namespace v2rayF.Core.Tests;

public class XrayParityImportTests
{
    [Fact]
    public void SsPlugin_IsSkippedWithReason()
    {
        var link =
            "ss://YWVzLTI1Ni1nY206cGFzcw@1.2.3.4:8388/?plugin=v2ray-plugin%3Bmode%3Dwebsocket#plug";
        var result = ShareLinkParser.ParseBulkDetailed(link);
        Assert.Empty(result.Servers);
        Assert.True(result.SkippedCount >= 1);
        Assert.Contains(result.SkipReasons, r => r.Contains("plugin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Hy2AndWg_AreSkippedWithHonestHints()
    {
        var text = "hy2://secret@h.example:443#h\nwg://example\nvless://aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee@x.com:443?type=tcp#v";
        var result = ConfigImportParser.ParseDetailed(text);
        Assert.Contains(result.Servers, s => s.Protocol == ProxyProtocol.VLESS);
        Assert.True(result.SkippedCount >= 1);
        Assert.False(string.IsNullOrEmpty(result.SummaryHint));
        Assert.Contains("sing-box", result.SummaryHint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VlessWs_EarlyDataAndPacketEncoding_ParseAndBuild()
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
        Assert.Equal(
            "xudp",
            proxy["settings"]!["vnext"]![0]!["users"]![0]!["packetEncoding"]!.GetValue<string>());
    }

    [Fact]
    public void ClashMeta_ImportsVmess_SkipsHy2()
    {
        var yaml = """
            proxies:
              - { name: c1, type: vmess, server: 1.2.3.4, port: 443, uuid: aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee, alterId: 0, cipher: auto, network: ws, tls: true, servername: a.com, ws-opts.path: /v }
              - { name: h2, type: hysteria2, server: 5.6.7.8, port: 443, password: x }
            proxy-groups:
              - { name: PROXY, type: select, proxies: [c1, h2] }
            """;
        var result = ClashMetaImportParser.Parse(yaml);
        Assert.Contains(result.Servers, s => s.Name == "c1" && s.Protocol == ProxyProtocol.VMess);
        Assert.DoesNotContain(result.Servers, s => s.Name == "h2");
        Assert.True(result.SkippedCount >= 1);
        Assert.Contains(result.SkipReasons, r => r.Contains("Hysteria2", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SingBoxJson_ImportsVless_SkipsTuic()
    {
        var json = """
            {
              "outbounds": [
                {
                  "type": "vless",
                  "tag": "vl",
                  "server": "v.example",
                  "server_port": 443,
                  "uuid": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                  "tls": { "enabled": true, "server_name": "v.example" },
                  "transport": { "type": "ws", "path": "/w" }
                },
                {
                  "type": "tuic",
                  "tag": "t",
                  "server": "t.example",
                  "server_port": 443,
                  "uuid": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                  "password": "p"
                },
                { "type": "direct", "tag": "direct" }
              ]
            }
            """;
        var result = SingBoxJsonImportParser.Parse(json);
        Assert.Contains(result.Servers, s => s.Name == "vl" && s.Protocol == ProxyProtocol.VLESS && s.Network == "ws");
        Assert.True(result.SkippedCount >= 1);
        Assert.Contains(result.SkipReasons, r => r.Contains("TUIC", StringComparison.OrdinalIgnoreCase));
    }
}
