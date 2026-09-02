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

    private sealed class FakeEnv : ICoreEnvironment
    {
        public string GetDataDirectory() => Path.GetTempPath();
        public string GetCoresDirectory() => Path.GetTempPath();
        public string GetCorePath() => Path.Combine(Path.GetTempPath(), "missing-xray");
        public string GetSingBoxPath() => Path.Combine(Path.GetTempPath(), "missing-sing-box");
        public Task EnsureCoreAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ICoreProcessHost CreateProcessHost() => new ManagedCoreProcessHost();
    }
}
