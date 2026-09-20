using v2rayF.Models;
using v2rayF.Services;

namespace v2rayF.Core.Tests;

/// <summary>Automated golden-matrix lab contracts for v2.6.4.1 (does not replace field soak).</summary>
public class GoldenMatrixLabTests
{
    [Fact]
    public void Scorecard_IncludesFreeAndGamingChecks()
    {
        Assert.Contains("Free Worker shortlist", ConnectivityScorecard.AppChecks);
        Assert.Contains("Free slots ≤5 pulse-free", ConnectivityScorecard.AppChecks);
        Assert.Contains("Free prefer 150 fill 450", ConnectivityScorecard.AppChecks);
        Assert.Contains("Gaming Boost UDP vs V2Box", ConnectivityScorecard.AppChecks);
        Assert.Contains("Lock unlock Chrome without app", ConnectivityScorecard.AppChecks);
        Assert.Contains("Windows TUN adapter present", ConnectivityScorecard.AppChecks);
    }

    [Fact]
    public void PulseFree_IranRealisticLatencyContract()
    {
        Assert.Equal(150, PulseFreeConstants.MaxLatencyMs);
        Assert.Equal(450, PulseFreeConstants.AcceptLatencyMs);
        Assert.Equal(5, PulseFreeConstants.MaxSlots);
        Assert.Equal(
            "https://pulseconfigs-mirror.drmikecrypto.workers.dev",
            PulseFreeConstants.DefaultWorkerBase);
        Assert.Equal(PulseFreeConstants.DefaultWorkerBase, new AppSettings().PulseWorkerBase);
    }

    [Fact]
    public void PulseFree_ShortlistPrefersWorkerCandidates()
    {
        var list = PulseFreeServers.BuildShortlistCandidates(PulseFreeConstants.DefaultWorkerBase);
        Assert.Contains(list, u => u.Contains("candidates.json", StringComparison.OrdinalIgnoreCase));
        Assert.StartsWith(PulseFreeConstants.DefaultWorkerBase, list[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PhaseC_IsOpen_AfterFieldSoak()
    {
        // Phase C unlocked after 2.6.4.1 Android + Windows field soak.
        // Gaming Boost remains the honest multipath/gaming slice (Survive stays off).
        var gaming = new AppSettings();
        NetworkProfiles.ApplyGaming(gaming);
        Assert.True(gaming.GamingBoostActive);
        Assert.False(gaming.ChromiumHttpProxyAssist);
        Assert.True(gaming.SmartMultipathEnabled);
        Assert.False(gaming.AdaptiveSurviveEnabled);
    }
}
