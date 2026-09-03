using System.Text.Json.Nodes;
using v2rayF.Models;
using v2rayF.Services;

namespace v2rayF.Core.Tests;

public class PushRoutingTests
{
    [Fact]
    public void MessagingDnsSuffixes_AreDistinctFromMeta()
    {
        foreach (var suffix in PushRoutingDomains.MessagingDnsSuffixes)
        {
            Assert.DoesNotContain(suffix, SingBoxConfigBuilder.MetaDnsSuffixes);
        }
    }

    [Fact]
    public void FcmExactHosts_IncludedInAndroidPushRoute()
    {
        var routes = SingBoxConfigBuilder.GetAndroidPushRouteHosts();
        foreach (var host in PushRoutingDomains.FcmDnsExactHosts)
            Assert.Contains(host, routes);
    }

    [Fact]
    public void DesktopPush_IncludesMessengerSuffixes()
    {
        foreach (var suffix in PushRoutingDomains.MessagingDnsSuffixes)
            Assert.Contains(suffix, XrayConfigBuilder.DesktopPushDomainSuffixes);
    }

    [Fact]
    public void AndroidTun_MessagingDnsUsesRealUdpNotFakeIp()
    {
        var server = ShareLinkParser.Parse("vless://aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee@x.com:443?type=tcp#v")!;
        var dns = JsonNode.Parse(SingBoxConfigBuilder.Build(server, new AppSettings(), tunFd: 3))!["dns"]!;
        var rules = dns["rules"]!.AsArray();

        Assert.Contains(rules, r =>
            r?["domain_suffix"] is JsonArray suffixes &&
            suffixes.Any(s => s!.GetValue<string>() == "whatsapp.net") &&
            r["server"]?.GetValue<string>() == SingBoxConfigBuilder.UdpDnsTag);

        Assert.Contains(rules, r =>
            r?["domain"] is JsonArray domains &&
            domains.Any(d => d!.GetValue<string>() == "mtalk.google.com") &&
            r["server"]?.GetValue<string>() == SingBoxConfigBuilder.UdpDnsTag);
    }

    [Fact]
    public void AndroidTun_PushRouteHosts_ProxyOutbound()
    {
        var server = ShareLinkParser.Parse("vless://aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee@x.com:443?type=tcp#v")!;
        var rules = JsonNode.Parse(SingBoxConfigBuilder.Build(server, new AppSettings(), tunFd: 3))![
            "route"]!["rules"]!.AsArray();

        Assert.Contains(rules, r =>
            r?["domain"] is JsonArray domains &&
            domains.Any(d => d!.GetValue<string>() == "mtalk.google.com") &&
            r["outbound"]?.GetValue<string>() == "proxy");

        Assert.Contains(rules, r =>
            r?["domain"] is JsonArray domains &&
            domains.Any(d => d!.GetValue<string>() == "g.whatsapp.net") &&
            r["outbound"]?.GetValue<string>() == "proxy");
    }

    [Fact]
    public void AndroidTun_MessagingSuffixRoute_ProxyOutbound()
    {
        var server = ShareLinkParser.Parse("vless://aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee@x.com:443?type=tcp#v")!;
        var rules = JsonNode.Parse(SingBoxConfigBuilder.Build(server, new AppSettings(), tunFd: 3))![
            "route"]!["rules"]!.AsArray();

        Assert.Contains(rules, r =>
            r?["domain_suffix"] is JsonArray suffixes &&
            suffixes.Any(s => s!.GetValue<string>() == "telegram.org") &&
            r["outbound"]?.GetValue<string>() == "proxy");
    }

    [Fact]
    public void DesktopPush_IncludesSignalAndApple()
    {
        Assert.Contains("signal.org", XrayConfigBuilder.DesktopPushDomainSuffixes);
        Assert.Contains("push.apple.com", XrayConfigBuilder.DesktopPushDomainSuffixes);
        Assert.Contains("slack-edge.com", XrayConfigBuilder.DesktopPushDomainSuffixes);
    }

    [Fact]
    public void AndroidPushRoute_IncludesSignalExactHosts()
    {
        var routes = SingBoxConfigBuilder.GetAndroidPushRouteHosts();
        Assert.Contains("chat.signal.org", routes);
        Assert.Contains("uds.signal.org", routes);
    }

    [Fact]
    public void AndroidPushRoute_IncludesSlackExactHosts()
    {
        var routes = SingBoxConfigBuilder.GetAndroidPushRouteHosts();
        Assert.Contains("hooks.slack.com", routes);
        Assert.Contains("wss-primary.slack.com", routes);
        Assert.Contains("hooks.slack.com", PushRoutingDomains.MessagingPushRouteHosts);
        Assert.Contains("wss-primary.slack.com", PushRoutingDomains.MessagingPushRouteHosts);
    }
}
