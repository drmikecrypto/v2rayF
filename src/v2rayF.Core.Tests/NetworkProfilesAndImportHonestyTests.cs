using v2rayF.Models;
using v2rayF.Services;

namespace v2rayF.Core.Tests;

public class NetworkProfilesAndImportHonestyTests
{
    [Fact]
    public void IranProfile_IsGlobalFailClosed()
    {
        var s = new AppSettings { RoutingMode = RoutingMode.BypassLan, DnsThroughProxy = false };
        NetworkProfiles.ApplyIran(s);
        Assert.Equal(RoutingMode.Global, s.RoutingMode);
        Assert.True(s.DnsThroughProxy);
        Assert.True(s.BlockIpv6);
        Assert.True(s.KillSwitchEnabled);
        Assert.True(s.EnableTunMode);
        Assert.True(s.ChromiumHttpProxyAssist);
    }

    [Fact]
    public void ChinaProfile_UsesBypassChina()
    {
        var s = new AppSettings();
        NetworkProfiles.ApplyChina(s);
        Assert.Equal(RoutingMode.BypassChina, s.RoutingMode);
        Assert.True(s.DnsThroughProxy);
        Assert.True(s.BlockIpv6);
        Assert.True(s.ChromiumHttpProxyAssist);
    }

    [Fact]
    public void SsPlugin_IsHardRefuse_NotSilentImport()
    {
        var link =
            "ss://YWVzLTI1Ni1nY206cGFzc3dvcmQ@1.2.3.4:8388/?plugin=v2ray-plugin%3Btls#plug";
        var result = ConfigImportParser.ParseDetailed(link);
        Assert.Empty(result.Servers);
        Assert.True(result.IsHardRefuse);
        Assert.StartsWith("Refused", result.SummaryHint);
        Assert.Contains(result.SkipReasons, r => r.Contains("plugin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MessagingPushRouteHosts_StayMinimal()
    {
        // Shrink policy: no FCM duplicates; no full Telegram/Discord host tables.
        Assert.DoesNotContain(PushRoutingDomains.MessagingPushRouteHosts, h =>
            h.Contains("mtalk.google.com", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(PushRoutingDomains.MessagingPushRouteHosts, h =>
            h.Contains("telegram.org", StringComparison.OrdinalIgnoreCase) ||
            h.Contains("discord", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(PushRoutingDomains.FcmDnsExactHosts, h => h == "mtalk.google.com");
    }
}
