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
}
