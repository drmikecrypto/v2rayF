using v2rayF.Models;
using v2rayF.Services;

namespace v2rayF.Core.Tests;

/// <summary>
/// Guards the dual-core Connect vs Test All split so D13-class footguns cannot ship again.
/// Live Android classic → sing-box; classic speedtest → Xray; Android TUN → gvisor.
/// </summary>
public class DualCoreConnectRegressionTests
{
    [Fact]
    public void ClassicSpeedtest_NeverUsesPreferSingBoxOnAndroid()
    {
        var classic = new ProxyServer
        {
            Protocol = ProxyProtocol.VLESS,
            Address = "1.2.3.4",
            Port = 443,
            UserId = Guid.NewGuid().ToString()
        };

        var prev = AppServices.Platform;
        try
        {
            AppServices.Platform = new MobilePlatform();
            Assert.True(CoreRuntime.PreferSingBoxOnAndroid(classic));
            Assert.True(CoreRuntime.UseSingBox(classic));
            // D13 regression: UseSingBoxForSpeedtest must NOT follow PreferSingBoxOnAndroid.
            Assert.False(CoreRuntime.UseSingBoxForSpeedtest(classic));
            Assert.Equal(CoreRuntime.RequiresSingBox(classic), CoreRuntime.UseSingBoxForSpeedtest(classic));
        }
        finally
        {
            AppServices.Platform = prev!;
        }
    }

    [Fact]
    public void Hy2Speedtest_StillUsesSingBox()
    {
        var hy2 = new ProxyServer
        {
            Protocol = ProxyProtocol.Hysteria2,
            Address = "1.2.3.4",
            Port = 443,
            Password = "x"
        };
        Assert.True(CoreRuntime.RequiresSingBox(hy2));
        Assert.True(CoreRuntime.UseSingBoxForSpeedtest(hy2));
    }

    [Fact]
    public void AndroidTun_DefaultsToGvisor_EvenIfExperimentalStackSet()
    {
        var server = ShareLinkParser.Parse("vless://aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee@1.2.3.4:443?type=tcp#t")!;
        var settings = new AppSettings
        {
            ExperimentalAndroidTunStack = "system",
            AllowExperimentalAndroidTunStack = false
        };
        Assert.Equal("gvisor", SingBoxConfigBuilder.ResolveAndroidTunStack(settings));

        var root = System.Text.Json.Nodes.JsonNode.Parse(
            SingBoxConfigBuilder.Build(server, settings, tunFd: 3))!;
        var tun = root["inbounds"]!.AsArray()
            .First(i => i!["tag"]?.GetValue<string>() == "tun-in")!;
        Assert.Equal("gvisor", tun["stack"]!.GetValue<string>());
    }

    [Fact]
    public void AndroidTun_ExperimentalStack_RequiresExplicitGate()
    {
        var settings = new AppSettings
        {
            AllowExperimentalAndroidTunStack = true,
            ExperimentalAndroidTunStack = "mixed"
        };
        Assert.Equal("mixed", SingBoxConfigBuilder.ResolveAndroidTunStack(settings));
        settings.ExperimentalAndroidTunStack = "bogus";
        Assert.Equal("gvisor", SingBoxConfigBuilder.ResolveAndroidTunStack(settings));
    }

    [Fact]
    public void ChromiumAssist_DefaultOn_DailyOn_GamingOff()
    {
        Assert.True(new AppSettings().ChromiumHttpProxyAssist);
        var daily = new AppSettings { ChromiumHttpProxyAssist = false };
        NetworkProfiles.ApplyDaily(daily);
        Assert.True(daily.ChromiumHttpProxyAssist);
        var gaming = new AppSettings();
        NetworkProfiles.ApplyGaming(gaming);
        Assert.False(gaming.ChromiumHttpProxyAssist);
    }

    [Fact]
    public void PathTruthDiagnostics_DefaultOff()
    {
        Assert.False(new AppSettings().ShowPathTruthDiagnostics);
    }

    [Fact]
    public void TunOnlyAdvisory_WhenLocalhostOkButTunFailed()
    {
        Assert.True(ProxyCoreService.IsTunOnlyAdvisory(localhostOk: true, tunOk: false, tunRequired: true));
        Assert.False(ProxyCoreService.IsTunOnlyAdvisory(localhostOk: true, tunOk: true, tunRequired: true));
        Assert.False(ProxyCoreService.IsTunOnlyAdvisory(localhostOk: false, tunOk: false, tunRequired: true));
    }

    private sealed class MobilePlatform : IPlatformIntegration
    {
        public bool IsMobile => true;
        public bool CanUseTunMode => true;
        public string TunRequirementMessage => "";
        public string? LastProxyMethod => null;
        public string? LastEstablishError => null;
        public string? LastHttpProxyWarning => null;
        public Task<int?> EstablishVpnAsync(
            IReadOnlyList<string>? bypassPackages = null,
            bool blockIpv6 = true,
            CancellationToken cancellationToken = default,
            bool forceRebind = false,
            bool chromiumHttpProxyAssist = false) =>
            Task.FromResult<int?>(null);
        public Task EnableProxyAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisableProxyAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NotifyVpnReadyAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int?> ProbeTunAppPathAsync(
            CancellationToken cancellationToken = default,
            int timeoutMs = LatencyService.TunAppPathProbeMs) =>
            Task.FromResult<int?>(0);
        public Task PromptBatteryOptimizationIfNeededAsync(
            AppSettings settings,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public bool NeedsVpnReestablish(
            IReadOnlyList<string>? bypassPackages,
            bool blockIpv6,
            bool chromiumHttpProxyAssist = false) => false;
        public string? GetPrivateDnsConflictWarning() => null;
        public string? GetLanIPv4Address() => null;
        public Task<IReadOnlyList<InstalledAppInfo>> GetNetworkAppsAsync(
            bool forceRefresh = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InstalledAppInfo>>([]);
        public Task<IReadOnlyDictionary<string, AppTrafficSnapshot>> GetAppTrafficAsync(
            IReadOnlyList<string> ids,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, AppTrafficSnapshot>>(
                new Dictionary<string, AppTrafficSnapshot>());
        public Task<string?> TryGetClipboardTextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }
}
