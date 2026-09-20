using System.Threading.Tasks;
using v2rayF.Services;

namespace v2rayF.Core.Tests;

public class DesktopTunInterfaceTests
{
    [Fact]
    public void IsPresent_UnknownName_ReturnsFalse()
    {
        Assert.False(DesktopTunInterface.IsPresent("v2rayF-nonexistent-adapter-xyz"));
    }

    [Fact]
    public async Task WaitUntilPresent_ZeroBudget_ReturnsFalseForMissing()
    {
        Assert.False(await DesktopTunInterface.WaitUntilPresentAsync(
            timeoutMs: 0,
            name: "v2rayF-nonexistent-adapter-xyz"));
    }

    [Fact]
    public void FailClosed_MapsMissingDesktopAdapterLikeAndroidVpn()
    {
        // Desktop ProbeTunAppPath returns TunVpnMissingMs when adapter absent —
        // same gate as Android VpnService Network missing.
        Assert.True(ProxyCoreService.ShouldFailClosedOnWeakTun(
            localhostOk: true,
            tunMs: LatencyService.TunVpnMissingMs,
            tunRequired: true));
        Assert.True(ProxyCoreService.IsVpnNetworkMissing(
            LatencyService.TunVpnMissingMs, tunRequired: true));
        Assert.False(ProxyCoreService.ShouldFailClosedOnWeakTun(
            localhostOk: true,
            tunMs: -1,
            tunRequired: true));
    }

    [Fact]
    public void ConnectivityScorecard_IncludesSessionAndWindowsChecks()
    {
        Assert.Contains("Lock unlock Chrome without app", ConnectivityScorecard.AppChecks);
        Assert.Contains("Windows TUN adapter present", ConnectivityScorecard.AppChecks);
        Assert.Contains("Windows no Connected blackhole", ConnectivityScorecard.AppChecks);
        Assert.Contains("Free Worker shortlist", ConnectivityScorecard.AppChecks);
        Assert.Contains("Gaming Boost UDP vs V2Box", ConnectivityScorecard.AppChecks);
    }
}
