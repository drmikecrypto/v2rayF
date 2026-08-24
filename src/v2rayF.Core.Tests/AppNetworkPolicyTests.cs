using System.Text.Json.Nodes;
using v2rayF.Models;
using v2rayF.Services;

namespace v2rayF.Core.Tests;

public class AppNetworkPolicyTests
{
    [Fact]
    public void ParseIdList_SplitsAndDedupes()
    {
        var list = AppNetworkPolicy.ParseIdList("com.a\ncom.b;com.a, com.c");
        Assert.Equal(3, list.Count);
        Assert.Contains("com.a", list);
        Assert.Contains("com.b", list);
        Assert.Contains("com.c", list);
    }

    [Fact]
    public void DirectWinsOverBlock()
    {
        var settings = new AppSettings
        {
            AndroidBypassPackages = "com.direct.app",
            AndroidBlockPackages = "com.direct.app\ncom.blocked.app"
        };

        Assert.Equal(AppNetworkMode.Direct, AppNetworkPolicy.GetMode(settings, "com.direct.app", mobile: true));
        Assert.Equal(AppNetworkMode.Block, AppNetworkPolicy.GetMode(settings, "com.blocked.app", mobile: true));
        Assert.DoesNotContain("com.direct.app", AppNetworkPolicy.GetBlockIds(settings, mobile: true));
        Assert.Contains("com.blocked.app", AppNetworkPolicy.GetBlockIds(settings, mobile: true));
    }

    [Fact]
    public void SetMode_UpdatesListsAndClearsConflict()
    {
        var settings = new AppSettings { AndroidBypassPackages = "com.a" };
        AppNetworkPolicy.SetMode(settings, "com.a", AppNetworkMode.Block, mobile: true);
        Assert.Equal(AppNetworkMode.Block, AppNetworkPolicy.GetMode(settings, "com.a", mobile: true));
        Assert.DoesNotContain("com.a", AppNetworkPolicy.GetDirectIds(settings, mobile: true));

        AppNetworkPolicy.SetMode(settings, "com.a", AppNetworkMode.Vpn, mobile: true);
        Assert.Equal(AppNetworkMode.Vpn, AppNetworkPolicy.GetMode(settings, "com.a", mobile: true));
    }

    [Fact]
    public void SelfPackage_CannotBeBlockedOrDirectListed()
    {
        var settings = new AppSettings();
        AppNetworkPolicy.SetMode(settings, AppNetworkPolicy.AndroidSelfPackage, AppNetworkMode.Block, mobile: true);
        Assert.Empty(AppNetworkPolicy.GetBlockIds(settings, mobile: true));
        Assert.Equal(AppNetworkMode.Vpn, AppNetworkPolicy.GetMode(settings, AppNetworkPolicy.AndroidSelfPackage, mobile: true));
    }

    [Fact]
    public void SingBoxTun_EmitsPackageNameBlockRules()
    {
        var server = ShareLinkParser.Parse("vless://aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee@x.com:443?type=tcp#v")!;
        var settings = new AppSettings
        {
            AndroidBlockPackages = "com.blocked.app\ncom.other.app",
            AndroidBypassPackages = "com.bypass.app"
        };
        var json = JsonNode.Parse(SingBoxConfigBuilder.Build(server, settings, tunFd: 3))!;
        var rules = json["route"]!["rules"]!.AsArray();
        var blockRule = rules.FirstOrDefault(r =>
            r?["package_name"] is JsonArray &&
            r["outbound"]?.GetValue<string>() == "block");
        Assert.NotNull(blockRule);
        var names = blockRule!["package_name"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
        Assert.Contains("com.blocked.app", names);
        Assert.Contains("com.other.app", names);
        Assert.DoesNotContain("com.bypass.app", names);
    }

    [Fact]
    public void SingBoxWithoutTun_OmitsPackageNameBlockRules()
    {
        var server = ShareLinkParser.Parse("vless://aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee@x.com:443?type=tcp#v")!;
        var settings = new AppSettings { AndroidBlockPackages = "com.blocked.app" };
        var json = JsonNode.Parse(SingBoxConfigBuilder.Build(server, settings))!;
        var rules = json["route"]!["rules"]!.AsArray();
        Assert.DoesNotContain(rules, r => r?["package_name"] is not null);
    }

    [Fact]
    public void XrayTun_EmitsProcessDirectAndBlockRules()
    {
        var server = ShareLinkParser.Parse("vless://aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee@x.com:443?type=tcp#v")!;
        var settings = new AppSettings
        {
            EnableTunMode = true,
            DesktopDirectProcesses = "chrome.exe",
            DesktopBlockProcesses = "malware.exe\nchrome.exe"
        };
        var json = JsonNode.Parse(XrayConfigBuilder.Build(server, settings))!;
        var rules = json["routing"]!["rules"]!.AsArray();

        var block = rules.FirstOrDefault(r =>
            r?["process"] is JsonArray &&
            r["outboundTag"]?.GetValue<string>() == "block");
        Assert.NotNull(block);
        var blockNames = block!["process"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
        Assert.Contains("malware.exe", blockNames);
        Assert.DoesNotContain("chrome.exe", blockNames);

        var direct = rules.FirstOrDefault(r =>
            r?["process"] is JsonArray &&
            r["outboundTag"]?.GetValue<string>() == "direct" &&
            r["process"]!.AsArray().Any(n => n!.GetValue<string>() == "chrome.exe"));
        Assert.NotNull(direct);
    }
}
