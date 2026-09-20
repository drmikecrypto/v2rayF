using v2rayF.Services;

namespace v2rayF.Core.Tests;

public class SessionDiagnosticsTests
{
    [Fact]
    public void Record_And_Export_IncludesTimeline()
    {
        var d = new SessionDiagnostics();
        d.Record("connect SOCKS ok (12 ms)");
        d.Record("TUN weak ×6 — soft recovery");
        var blob = d.Export(
            productVersion: "2.6.4.2",
            serverLabel: "lab",
            socksOk: true,
            httpWeak: false,
            tunWeak: true,
            socksProbeMs: 12,
            tunMs: LatencyService.TunVpnMissingMs,
            gamingBoost: true,
            multipath: true,
            consecutivePathFails: 1,
            multipathHint: SessionDiagnostics.FormatMultipathFlakeHint(true, true, 1));

        Assert.Contains("connect SOCKS ok", blob, StringComparison.Ordinal);
        Assert.Contains("TunVpnMissingMs", blob, StringComparison.Ordinal);
        Assert.Contains("Gaming Boost", blob, StringComparison.Ordinal);
        Assert.Contains("Survive stays off", blob, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatRecoverCta_TunWeak_Gaming()
    {
        var cta = SessionDiagnostics.FormatRecoverCta(tunWeak: true, httpWeak: false, gamingBoost: true);
        Assert.Contains("assist off", cta, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormatRecoverCta_HttpWeak_Daily()
    {
        var cta = SessionDiagnostics.FormatRecoverCta(tunWeak: false, httpWeak: true, gamingBoost: false);
        Assert.Contains("Gaming Boost", cta, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatMultipathFlakeHint_RequiresMultipathAndFails()
    {
        Assert.Null(SessionDiagnostics.FormatMultipathFlakeHint(false, true, 2));
        Assert.Null(SessionDiagnostics.FormatMultipathFlakeHint(true, true, 0));
        var hint = SessionDiagnostics.FormatMultipathFlakeHint(true, true, 2);
        Assert.Contains("Survive stays off", hint, StringComparison.Ordinal);
        Assert.DoesNotContain("Survive on", hint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MaxEvents_TrimsOldest()
    {
        var d = new SessionDiagnostics();
        for (var i = 0; i < SessionDiagnostics.MaxEvents + 5; i++)
            d.Record($"e{i}");
        var snap = d.Snapshot();
        Assert.Equal(SessionDiagnostics.MaxEvents, snap.Count);
        Assert.Equal("e5", snap[0].Text);
    }
}
