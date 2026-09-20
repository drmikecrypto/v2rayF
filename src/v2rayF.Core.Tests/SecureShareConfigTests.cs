using System;
using System.Linq;
using System.Text.Json.Nodes;
using v2rayF.Models;
using v2rayF.Services;

namespace v2rayF.Core.Tests;

public class SecureShareConfigTests
{
    private static ProxyServer ClassicServer() => new()
    {
        Name = "lab",
        Protocol = ProxyProtocol.VMess,
        Address = "1.2.3.4",
        Port = 443,
        UserId = Guid.NewGuid().ToString("D"),
        AlterId = 0,
        Security = "auto",
        Network = "tcp",
        Sni = "www.example.com"
    };

    private static ProxyServer Hy2Server() => new()
    {
        Name = "hy2",
        Protocol = ProxyProtocol.Hysteria2,
        Address = "5.6.7.8",
        Port = 443,
        Password = "secret",
        Sni = "www.example.com"
    };

    [Fact]
    public void EnsureCredentials_FillsDefaults()
    {
        var s = new AppSettings { SecureShareEnabled = true };
        SecureShareEndpoints.EnsureShareCredentials(s);
        Assert.Equal("v2rayf", s.ShareAuthUser);
        Assert.False(string.IsNullOrWhiteSpace(s.ShareAuthPass));
        var first = s.ShareAuthPass;
        SecureShareEndpoints.EnsureShareCredentials(s);
        Assert.Equal(first, s.ShareAuthPass);
    }

    [Fact]
    public void RotatePassword_ChangesPass()
    {
        var s = new AppSettings();
        SecureShareEndpoints.EnsureShareCredentials(s);
        var first = s.ShareAuthPass;
        SecureShareEndpoints.RotateSharePassword(s);
        Assert.NotEqual(first, s.ShareAuthPass);
    }

    [Fact]
    public void ResolveListen_AllInterfaces()
    {
        var s = new AppSettings { ShareListenAllInterfaces = true };
        Assert.Equal("0.0.0.0", SecureShareEndpoints.ResolveShareListenAddress(s));
    }

    [Fact]
    public void RankAdvertise_PrefersSoftAp_OverWifi()
    {
        var ranked = SecureShareEndpoints.RankAdvertiseAddresses(
        [
            ("WLAN", "Intel Wi-Fi", "192.168.1.10"),
            ("Local Area Connection*", "Microsoft Wi-Fi Direct Virtual Adapter", "192.168.137.1")
        ]);
        Assert.Equal("192.168.137.1", ranked[0]);
    }

    [Fact]
    public void RankAdvertise_SkipsTunAndLinkLocal()
    {
        var ranked = SecureShareEndpoints.RankAdvertiseAddresses(
        [
            ("v2rayF", "WinTun", "172.19.0.1"),
            ("eth0", "Ethernet", "169.254.1.1"),
            ("wlan0", "Wi-Fi", "10.0.0.5")
        ]);
        Assert.Single(ranked);
        Assert.Equal("10.0.0.5", ranked[0]);
    }

    [Fact]
    public void Xray_EmitsShareInbounds_WhenEnabled()
    {
        var settings = new AppSettings
        {
            SecureShareEnabled = true,
            ShareListenAllInterfaces = true,
            ShareAuthUser = "u",
            ShareAuthPass = "p"
        };
        var json = XrayConfigBuilder.Build(ClassicServer(), settings);
        var root = JsonNode.Parse(json)!;
        var tags = root["inbounds"]!.AsArray().Select(n => n!["tag"]!.GetValue<string>()).ToList();
        Assert.Contains("share-socks", tags);
        Assert.Contains("share-http", tags);
        var socks = root["inbounds"]!.AsArray().First(n => n!["tag"]!.GetValue<string>() == "share-socks")!;
        Assert.Equal(10880, socks["port"]!.GetValue<int>());
        Assert.Equal("0.0.0.0", socks["listen"]!.GetValue<string>());
        Assert.Equal("password", socks["settings"]!["auth"]!.GetValue<string>());
    }

    [Fact]
    public void Xray_OmitsShare_WhenDisabled()
    {
        var settings = new AppSettings { SecureShareEnabled = false };
        var json = XrayConfigBuilder.Build(ClassicServer(), settings);
        Assert.DoesNotContain("share-socks", json, StringComparison.Ordinal);
    }

    [Fact]
    public void SingBox_EmitsShareInbounds_WhenEnabled()
    {
        var settings = new AppSettings
        {
            SecureShareEnabled = true,
            ShareListenAllInterfaces = true,
            ShareAuthUser = "u",
            ShareAuthPass = "p"
        };
        var json = SingBoxConfigBuilder.Build(Hy2Server(), settings, tunFd: 3);
        var root = JsonNode.Parse(json)!;
        var tags = root["inbounds"]!.AsArray().Select(n => n!["tag"]!.GetValue<string>()).ToList();
        Assert.Contains("share-socks", tags);
        Assert.Contains("share-http", tags);
        var socks = root["inbounds"]!.AsArray().First(n => n!["tag"]!.GetValue<string>() == "share-socks")!;
        Assert.Equal("socks", socks["type"]!.GetValue<string>());
        Assert.Equal(10880, socks["listen_port"]!.GetValue<int>());
        Assert.NotNull(socks["users"]);
    }

    [Fact]
    public void SingBox_OmitsShare_WhenDisabled()
    {
        var settings = new AppSettings { SecureShareEnabled = false };
        var json = SingBoxConfigBuilder.Build(Hy2Server(), settings, tunFd: 3);
        Assert.DoesNotContain("share-socks", json, StringComparison.Ordinal);
    }

    [Fact]
    public void SetupTip_MentionsProxyNotTransparent()
    {
        var s = new AppSettings { ShareAuthUser = "v2rayf", ShareAuthPass = "secret" };
        var tip = SecureShareEndpoints.FormatSetupTipMarkdown(s, "192.168.137.1", includePassword: true);
        Assert.Contains("proxy", tip, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not transparent", tip, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secret", tip, StringComparison.Ordinal);
    }
}
