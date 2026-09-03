using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using v2rayF.Models;
using v2rayF.Services;

namespace v2rayF.Core.Tests;

public class SessionReliabilityTests
{
    [Fact]
    public void ResumePathProbe_BudgetsMatchPlan()
    {
        var vision = new ProxyServer
        {
            Protocol = ProxyProtocol.VLESS,
            Security = "reality",
            Flow = "xtls-rprx-vision",
            PublicKey = "pk"
        };
        var plain = new ProxyServer { Address = "1.1.1.1", Port = 443, Protocol = ProxyProtocol.VLESS };
        Assert.Equal(4000, ProxyCoreService.GetResumePathProbeMs(plain));
        Assert.Equal(6000, ProxyCoreService.GetResumePathProbeMs(vision));
    }

    [Fact]
    public void ResetPathHealthState_ClearsCounters()
    {
        var core = new ProxyCoreService(new FakeEnv());
        var raised = false;
        core.PathHealthOk += (_, _) => raised = true;
        core.ResetPathHealthState();
        Assert.True(raised);
    }

    [Fact]
    public async Task VerifyLivePath_ReturnsFalseWhenNotRunning()
    {
        var core = new ProxyCoreService(new FakeEnv());
        Assert.False(await core.VerifyLivePathAsync());
    }

    [Fact]
    public void RequiresAndroidTunHttpProbe_OnlySingBoxWithTunFd()
    {
        Assert.True(ProxyCoreServiceRequiresAndroidTunHttpProbe(true, 3));
        Assert.False(ProxyCoreServiceRequiresAndroidTunHttpProbe(true, null));
        Assert.False(ProxyCoreServiceRequiresAndroidTunHttpProbe(false, 3));
    }

    private static bool ProxyCoreServiceRequiresAndroidTunHttpProbe(bool useSingBox, int? tunFd) =>
        useSingBox && tunFd is int fd && fd >= 0;

    [Fact]
    public void TunAppPathProbe_ConstantDefined()
    {
        Assert.Equal(4000, LatencyService.TunAppPathProbeMs);
        Assert.Equal(90, ProxyCoreService.ActivePathHealthIntervalMs / 1000);
        Assert.Equal(2, ProxyCoreService.TunOnlyFailThreshold);
    }

    [Fact]
    public void SoftRecoveryGate_BeginAndEnd()
    {
        var core = new ProxyCoreService(new FakeEnv());
        Assert.False(core.IsSoftRecoveryInFlight);
        core.BeginSoftRecovery();
        Assert.True(core.IsSoftRecoveryInFlight);
        core.EndSoftRecovery(success: true);
        Assert.False(core.IsSoftRecoveryInFlight);
    }

    [Fact]
    public void ConnectGate_IgnoresTunOnlyFailure()
    {
        Assert.Equal(50, ProxyCoreService.EvaluateConnectGateMs(50, 40, httpRequired: true));
        Assert.Null(ProxyCoreService.EvaluateConnectGateMs(null, 40, httpRequired: true));
        Assert.Equal(-1, ProxyCoreService.EvaluateConnectGateMs(-1, 40, httpRequired: true));
        Assert.Equal(-1, ProxyCoreService.EvaluateConnectGateMs(50, -1, httpRequired: true));
        Assert.Equal(50, ProxyCoreService.EvaluateConnectGateMs(50, null, httpRequired: false));
    }

    [Fact]
    public void ConnectGateFailure_NamesFailingComponent()
    {
        Assert.Contains(
            "SOCKS 10808",
            ProxyCoreService.DescribeConnectGateFailure(-1, 10, 10, httpRequired: true, tunRequired: true));
        Assert.Contains(
            "HTTP proxy 10809",
            ProxyCoreService.DescribeConnectGateFailure(10, -1, 10, httpRequired: true, tunRequired: true));
        Assert.Contains(
            "TUN app-path",
            ProxyCoreService.DescribeConnectGateFailure(10, 10, -1, httpRequired: true, tunRequired: true));
        Assert.DoesNotContain(
            "HTTP proxy 10809",
            ProxyCoreService.DescribeConnectGateFailure(10, 10, -1, httpRequired: true, tunRequired: true));
    }

    [Fact]
    public void ResolveCorePathFor_MatchesUseSingBox()
    {
        var dir = Path.Combine(Path.GetTempPath(), "v2rayf-ks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var xray = Path.Combine(dir, "xray.exe");
            var sing = Path.Combine(dir, "sing-box.exe");
            File.WriteAllText(xray, "");
            File.WriteAllText(sing, "");
            var core = new ProxyCoreService(new FakeEnv(dir, xray, sing));
            var hy2 = ShareLinkParser.Parse("hy2://secret@h.example:443#h")!;
            var vless = ShareLinkParser.Parse(
                "vless://aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee@x.com:443?type=tcp#v")!;

            Assert.True(CoreRuntime.UseSingBox(hy2));
            Assert.Equal(sing, core.ResolveCorePathFor(hy2));
            Assert.False(CoreRuntime.RequiresSingBox(vless));
            Assert.Equal(xray, core.ResolveCorePathFor(vless));
            Assert.Equal(xray, core.ResolveCorePath());
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* temp cleanup */ }
        }
    }

    private sealed class FakeEnv : ICoreEnvironment
    {
        private readonly string _data;
        private readonly string _xray;
        private readonly string _sing;

        public FakeEnv()
        {
            _data = Path.GetTempPath();
            _xray = Path.Combine(_data, "missing-xray");
            _sing = Path.Combine(_data, "missing-sing-box");
        }

        public FakeEnv(string data, string xray, string sing)
        {
            _data = data;
            _xray = xray;
            _sing = sing;
        }

        public string GetDataDirectory() => _data;
        public string GetCoresDirectory() => _data;
        public string GetCorePath() => _xray;
        public string GetSingBoxPath() => _sing;
        public Task EnsureCoreAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ICoreProcessHost CreateProcessHost() => new ManagedCoreProcessHost();
    }
}
