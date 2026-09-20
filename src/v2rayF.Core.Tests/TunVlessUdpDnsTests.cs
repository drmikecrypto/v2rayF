using System.Linq;
using System.Text.Json.Nodes;
using v2rayF.Models;
using v2rayF.Services;

namespace v2rayF.Core.Tests;

/// <summary>
/// Sentinel-3306 plain VLESS TCP: TUN DNS detours via proxy; without packet_encoding
/// UDP DNS blackholes while SOCKS TCP latency stays green. SS carries UDP natively.
/// </summary>
public class TunVlessUdpDnsTests
{
    private const string Vless3306 =
        "vless://1947cbe1-1aff-4dfe-b325-003fdc711ed1@169.40.32.81:3306?security=none&encryption=none&type=tcp#Sentinel-3306-VLESS-TCP";

    private const string Ss8880 =
        "ss://YWVzLTEyOC1nY206bEY4cklEczBidzM2VkF1UQ==@169.40.32.81:8880#Sentinel-8880-Shadowsocks";

    private const string RealityVision =
        "vless://2a05c3ec-a0e2-4c33-ac92-35c36f4fdf16@169.40.32.81:443?security=reality&encryption=none&pbk=18bUh7KFc0-1RBGAOSy-KOB5qFjm1T0juWB50roV9S0&fp=chrome&type=tcp&flow=xtls-rprx-vision&sni=www.yahoo.com&sid=05d78a9d#Sentinel-443-Reality-Vision";

    [Fact]
    public void Policy_PlainTcpNone_LiveTun_InjectsXudp()
    {
        var server = ConfigImportParser.Parse(Vless3306).First();
        Assert.Equal("xudp", PacketEncodingPolicy.Resolve(server, liveTun: true));
        Assert.Null(PacketEncodingPolicy.Resolve(server, liveTun: false));
    }

    [Fact]
    public void Policy_RealityVision_LiveTun_DoesNotInject()
    {
        var server = ConfigImportParser.Parse(RealityVision).First();
        Assert.Null(PacketEncodingPolicy.Resolve(server, liveTun: true));
    }

    [Fact]
    public void Policy_ExplicitEncoding_Preserved()
    {
        var server = ConfigImportParser.Parse(Vless3306).First();
        server.PacketEncoding = "packet";
        Assert.Equal("packet", PacketEncodingPolicy.Resolve(server, liveTun: true));
    }

    [Fact]
    public void SingBox_LiveTun_Vless3306_HasPacketEncodingAndDnsDetour()
    {
        var server = ConfigImportParser.Parse(Vless3306).First();
        var settings = new AppSettings { EnableTunMode = true };
        var root = JsonNode.Parse(SingBoxConfigBuilder.Build(server, settings, tunFd: 1))!;
        var proxy = root["outbounds"]!.AsArray().First(o => o!["tag"]?.GetValue<string>() == "proxy")!;
        Assert.Equal("xudp", proxy["packet_encoding"]!.GetValue<string>());

        var dnsServers = root["dns"]!["servers"]!.AsArray();
        var udp = dnsServers.Select(n => n as JsonObject)
            .First(o => o?["tag"]?.GetValue<string>() == "udp");
        Assert.Equal("proxy", udp!["detour"]!.GetValue<string>());

        var rules = root["route"]!["rules"]!.AsArray();
        Assert.Contains(rules, r => r?["action"]?.GetValue<string>() == "hijack-dns");
    }

    [Fact]
    public void SingBox_Speedtest_Vless3306_OmitsPacketEncoding()
    {
        var server = ConfigImportParser.Parse(Vless3306).First();
        var root = JsonNode.Parse(SingBoxConfigBuilder.BuildSpeedtest(server, 10818))!;
        var proxy = root["outbounds"]!.AsArray().First(o => o!["tag"]?.GetValue<string>() == "proxy")!;
        Assert.Null(proxy["packet_encoding"]);
    }

    [Fact]
    public void SingBox_LiveTun_Shadowsocks_Unchanged_NoPacketEncoding()
    {
        var server = ConfigImportParser.Parse(Ss8880).First();
        var settings = new AppSettings { EnableTunMode = true };
        var root = JsonNode.Parse(SingBoxConfigBuilder.Build(server, settings, tunFd: 1))!;
        var proxy = root["outbounds"]!.AsArray().First(o => o!["tag"]?.GetValue<string>() == "proxy")!;
        Assert.Equal("shadowsocks", proxy["type"]!.GetValue<string>());
        Assert.Null(proxy["packet_encoding"]);
    }

    [Fact]
    public void Xray_LiveTun_Vless3306_HasPacketEncoding()
    {
        var server = ConfigImportParser.Parse(Vless3306).First();
        var settings = new AppSettings { EnableTunMode = true };
        var root = JsonNode.Parse(XrayConfigBuilder.Build(server, settings))!;
        var proxy = root["outbounds"]!.AsArray().First(o => o!["tag"]?.GetValue<string>() == "proxy")!;
        Assert.Equal(
            "xudp",
            proxy["settings"]!["vnext"]![0]!["users"]![0]!["packetEncoding"]!.GetValue<string>());
    }

    [Fact]
    public void Xray_Speedtest_Vless3306_OmitsPacketEncoding()
    {
        var server = ConfigImportParser.Parse(Vless3306).First();
        var root = JsonNode.Parse(XrayConfigBuilder.BuildSpeedtest(server))!;
        var proxy = root["outbounds"]!.AsArray().First(o => o!["tag"]?.GetValue<string>() == "proxy")!;
        Assert.Null(proxy["settings"]!["vnext"]![0]!["users"]![0]!["packetEncoding"]);
    }

    [Fact]
    public void SessionDiagnostics_SocksOkTunWeak_MentionsUdpDns()
    {
        var d = new SessionDiagnostics();
        var blob = d.Export(
            productVersion: "test",
            serverLabel: "Sentinel-3306-VLESS-TCP",
            socksOk: true,
            httpWeak: false,
            tunWeak: true,
            socksProbeMs: 40,
            tunMs: -1,
            gamingBoost: false,
            multipath: false,
            consecutivePathFails: 0,
            multipathHint: null);
        Assert.Contains("packet_encoding=xudp", blob, StringComparison.Ordinal);
    }
}
