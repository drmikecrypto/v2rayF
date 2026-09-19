using Xunit;
using v2rayF.Services;

namespace v2rayF.Core.Tests;

public class PulseFreeServersTests
{
    [Fact]
    public void BuildTop5Candidates_PrefersWorkerThenRawThenCdn()
    {
        PulseFreeServers.WorkerBase = null;
        var list = PulseFreeServers.BuildTop5Candidates("https://pulse.example.workers.dev");
        Assert.Equal(3, list.Count);
        Assert.StartsWith("https://pulse.example.workers.dev/", list[0]);
        Assert.Contains("raw.githubusercontent.com", list[1]);
        Assert.Contains("jsdelivr", list[2]);
    }

    [Fact]
    public void BuildTop5Candidates_WithoutWorker_HasRawAndCdn()
    {
        var list = PulseFreeServers.BuildTop5Candidates(null);
        Assert.Equal(2, list.Count);
        Assert.Contains("raw.githubusercontent.com", list[0]);
    }

    [Fact]
    public void BuildShortlistCandidates_PrefersCandidatesJson()
    {
        PulseFreeServers.WorkerBase = null;
        var list = PulseFreeServers.BuildShortlistCandidates("https://pulse.example.workers.dev");
        Assert.Contains(list, u => u.EndsWith("/candidates.json"));
        Assert.Equal("https://pulse.example.workers.dev/candidates.json", list[0]);
    }

    [Fact]
    public void NormalizeSubscriptionBody_ExtractsRawLinks()
    {
        var json = """
            {"candidates":[{"raw":"vless://a@1.1.1.1:443","fingerprint":"x"},{"raw":"trojan://b@2.2.2.2:443"}]}
            """;
        var body = PulseFreeServers.NormalizeSubscriptionBody(json);
        Assert.Contains("vless://a@1.1.1.1:443", body);
        Assert.Contains("trojan://b@2.2.2.2:443", body);
    }

    [Fact]
    public void NormalizeSubscriptionBody_PassthroughPlainText()
    {
        const string plain = "vless://a@1.1.1.1:443\n";
        Assert.Equal(plain, PulseFreeServers.NormalizeSubscriptionBody(plain));
    }
}
