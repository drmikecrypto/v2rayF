using System.Linq;
using System.Text.Json.Nodes;
using v2rayF.Models;
using v2rayF.Services;
using Xunit;

namespace v2rayF.Core.Tests;

public class TrafficStatsServiceTests
{
    [Fact]
    public void Build_IncludesStatsApiAndPolicy()
    {
        var server = new ProxyServer
        {
            Protocol = ProxyProtocol.VLESS,
            Address = "1.2.3.4",
            Port = 443,
            UserId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
            Network = "tcp",
            Security = "none"
        };

        var json = XrayConfigBuilder.Build(server, new AppSettings());
        var root = JsonNode.Parse(json)!.AsObject();

        Assert.NotNull(root["stats"]);
        Assert.Equal("api", root["api"]!["tag"]!.GetValue<string>());
        Assert.Contains(
            root["api"]!["services"]!.AsArray().Select(n => n!.GetValue<string>()),
            s => s == "StatsService");
        Assert.True(root["policy"]!["system"]!["statsOutboundUplink"]!.GetValue<bool>());
        Assert.True(root["policy"]!["system"]!["statsOutboundDownlink"]!.GetValue<bool>());

        var apiInbound = root["inbounds"]!.AsArray()
            .First(i => i!["tag"]?.GetValue<string>() == "api")!;
        Assert.Equal(XrayConfigBuilder.ApiPort, apiInbound["port"]!.GetValue<int>());
        Assert.Equal("127.0.0.1", apiInbound["listen"]!.GetValue<string>());

        var apiRule = root["routing"]!["rules"]!.AsArray()
            .First(r => r!["inboundTag"] is JsonArray tags && tags.Any(t => t!.GetValue<string>() == "api"));
        Assert.Equal("api", apiRule!["outboundTag"]!.GetValue<string>());
    }

    [Fact]
    public void Build_MultipathObservatoryUsesGoogle()
    {
        var primary = new ProxyServer
        {
            Protocol = ProxyProtocol.VLESS,
            Address = "1.2.3.4",
            Port = 443,
            UserId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
            Network = "tcp",
            Security = "none"
        };
        var peer = new ProxyServer
        {
            Protocol = ProxyProtocol.VLESS,
            Address = "5.6.7.8",
            Port = 443,
            UserId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
            Network = "tcp",
            Security = "none"
        };

        var settings = new AppSettings { SmartMultipathEnabled = true };
        var json = XrayConfigBuilder.Build(primary, settings, multipathServers: [peer]);
        var root = JsonNode.Parse(json)!.AsObject();
        Assert.Equal(
            XrayConfigBuilder.GooglePingUrl,
            root["burstObservatory"]!["pingConfig"]!["destination"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("outbound>>>proxy>>>traffic>>>uplink", true)]
    [InlineData("outbound>>>proxy-1>>>traffic>>>downlink", true)]
    [InlineData("outbound>>>direct>>>traffic>>>uplink", false)]
    [InlineData("inbound>>>socks-in>>>traffic>>>uplink", false)]
    public void IsProxyTrafficStat_FiltersTags(string name, bool expected)
    {
        Assert.Equal(expected, TrafficStatsService.IsProxyTrafficStat(name));
    }

    [Fact]
    public void ParseStatsQueryJson_SumsProxyOutbounds()
    {
        const string json = """
            {
              "stat": [
                { "name": "outbound>>>proxy>>>traffic>>>uplink", "value": "100" },
                { "name": "outbound>>>proxy>>>traffic>>>downlink", "value": 200 },
                { "name": "outbound>>>proxy-0>>>traffic>>>uplink", "value": 50 },
                { "name": "outbound>>>direct>>>traffic>>>uplink", "value": 9999 }
              ]
            }
            """;

        var snap = TrafficStatsService.ParseStatsQueryJson(json);
        Assert.NotNull(snap);
        Assert.Equal(150, snap.Value.UplinkBytes);
        Assert.Equal(200, snap.Value.DownlinkBytes);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(2048, "2 KB")]
    [InlineData(1536 * 1024, "1.5 MB")]
    public void FormatBytes_UsesReadableUnits(long bytes, string expected)
    {
        Assert.Equal(expected, TrafficStatsService.FormatBytes(bytes));
    }

    [Fact]
    public void FormatNotificationLine_IsClean()
    {
        Assert.Equal("↑ 1 KB  ·  ↓ 2 KB", TrafficStatsService.FormatNotificationLine(1024, 2048));
    }
}
