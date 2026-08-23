using System.Text.Json.Nodes;
using v2rayF.Models;
using v2rayF.Services;

namespace v2rayF.Core.Tests;

public class SingBoxDnsConfigTests
{
    [Fact]
    public void BuildDns_UsesSingBox112ServerTypes_NotLegacyAddress()
    {
        var server = new ProxyServer
        {
            Protocol = ProxyProtocol.VLESS,
            Address = "node.example.com",
            Port = 443,
            UserId = Guid.NewGuid().ToString(),
            Security = "tls",
            Network = "tcp"
        };

        var dns = JsonNode.Parse(SingBoxConfigBuilder.Build(server, new AppSettings { DnsThroughProxy = false }))!["dns"]!;
        foreach (var entry in dns["servers"]!.AsArray())
        {
            Assert.NotNull(entry!["type"]);
            Assert.Null(entry["address"]);
            Assert.Null(entry["detour"]);
        }

        var udp = dns["servers"]!.AsArray().First(s => s!["tag"]?.GetValue<string>() == SingBoxConfigBuilder.UdpDnsTag)!;
        Assert.Equal("udp", udp["type"]!.GetValue<string>());
        Assert.Equal("1.1.1.1", udp["server"]!.GetValue<string>());
    }

    [Fact]
    public void BuildDns_DomainNode_HasBootstrapServerRuleAndOutboundResolver()
    {
        var server = new ProxyServer
        {
            Protocol = ProxyProtocol.VLESS,
            Address = "rfau8vd61dcf.dop33.com",
            Port = 443,
            UserId = Guid.NewGuid().ToString(),
            Security = "tls",
            Network = "tcp"
        };

        var root = JsonNode.Parse(SingBoxConfigBuilder.Build(server, new AppSettings()))!;
        var dns = root["dns"]!;
        Assert.Contains(
            dns["servers"]!.AsArray(),
            s => s!["tag"]?.GetValue<string>() == SingBoxConfigBuilder.BootstrapDnsTag &&
                 s["type"]?.GetValue<string>() == "udp");

        var bootstrapRule = dns["rules"]!.AsArray().First(r =>
            r!["server"]?.GetValue<string>() == SingBoxConfigBuilder.BootstrapDnsTag);
        Assert.Equal("route", bootstrapRule["action"]!.GetValue<string>());
        Assert.Contains(
            "rfau8vd61dcf.dop33.com",
            bootstrapRule["domain"]!.AsArray().Select(d => d!.GetValue<string>()));

        var proxy = root["outbounds"]!.AsArray().First(o => o!["tag"]?.GetValue<string>() == "proxy")!;
        Assert.Equal(SingBoxConfigBuilder.BootstrapDnsTag, proxy["domain_resolver"]!.GetValue<string>());
    }

    [Fact]
    public void BuildDns_IpNode_HasNoBootstrapRuleOrDomainResolver()
    {
        var server = new ProxyServer
        {
            Protocol = ProxyProtocol.VLESS,
            Address = "169.40.32.81",
            Port = 443,
            UserId = Guid.NewGuid().ToString(),
            Network = "tcp"
        };

        var root = JsonNode.Parse(SingBoxConfigBuilder.Build(server, new AppSettings()))!;
        Assert.Null(root["dns"]!["rules"]);
        var proxy = root["outbounds"]!.AsArray().First(o => o!["tag"]?.GetValue<string>() == "proxy")!;
        Assert.Null(proxy["domain_resolver"]);
    }

    [Fact]
    public void BuildDns_DohPath_UsesHttpsServers()
    {
        var server = new ProxyServer
        {
            Protocol = ProxyProtocol.Hysteria2,
            Address = "1.2.3.4",
            Port = 443,
            Password = "secret"
        };

        var dns = JsonNode.Parse(SingBoxConfigBuilder.Build(server, new AppSettings { DnsThroughProxy = true }))!["dns"]!;
        var doh = dns["servers"]!.AsArray().First(s => s!["tag"]?.GetValue<string>() == SingBoxConfigBuilder.DohDnsTag)!;
        Assert.Equal("https", doh["type"]!.GetValue<string>());
        Assert.Equal("1.1.1.1", doh["server"]!.GetValue<string>());
        Assert.Equal(SingBoxConfigBuilder.DohDnsTag, dns["final"]!.GetValue<string>());
    }
}

public class CoreStartupOutputTests
{
    [Fact]
    public void ExtractActionableLine_PrefersFatalOverMigrationUrlTail()
    {
        var stderr =
            "WARN legacy DNS servers is deprecated … migration/#migrate-to-new-dns-server-formats\n" +
            "FATAL start service: bind mixed-in: address already in use";

        var line = CoreStartupErrorFormatter.ExtractActionableLine(stderr);
        Assert.Contains("FATAL", line, StringComparison.Ordinal);
        Assert.Contains("address already in use", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExtractActionableLine_LegacyDnsWarn_IsFriendly()
    {
        var stderr = "WARN legacy DNS servers is deprecated … migrate-to-new-dns-server-formats";
        var line = CoreStartupErrorFormatter.ExtractActionableLine(stderr);
        Assert.Contains("Legacy DNS", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("grate-to-new", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_LegacyDnsWarning_SuggestsUpdate()
    {
        var msg = CoreStartupErrorFormatter.Format(
            "WARN legacy DNS servers is deprecated in sing-box 1.12.0 … migrate-to-new-dns-server-formats");
        Assert.Contains("DNS config is outdated", msg, StringComparison.OrdinalIgnoreCase);
    }
}
