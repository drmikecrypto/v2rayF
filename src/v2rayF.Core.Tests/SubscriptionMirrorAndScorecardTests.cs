using System;
using System.Collections.Generic;
using v2rayF.Services;

namespace v2rayF.Core.Tests;

public class SubscriptionMirrorAndScorecardTests
{
    [Fact]
    public void BuildFetchCandidates_Github_AddsMirrors()
    {
        var uri = new Uri("https://raw.githubusercontent.com/org/repo/main/sub.txt");
        var candidates = SubscriptionMirrorHints.BuildFetchCandidates(uri);
        Assert.True(candidates.Count > 1);
        Assert.Equal(uri, candidates[0]);
        Assert.Contains(candidates, c => c.Host.Contains("ghproxy", StringComparison.OrdinalIgnoreCase) ||
                                         c.AbsoluteUri.Contains("ghproxy", StringComparison.OrdinalIgnoreCase) ||
                                         c.Host.Contains("ddlc", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildFetchCandidates_NonGithub_Single()
    {
        var uri = new Uri("https://example.com/sub");
        Assert.Single(SubscriptionMirrorHints.BuildFetchCandidates(uri));
    }

    [Fact]
    public void FormatFailureHint_MentionsMirrorsForGithub()
    {
        var hint = SubscriptionMirrorHints.FormatFailureHint(
            new Uri("https://github.com/a/b"),
            new InvalidOperationException("timeout"));
        Assert.Contains("mirror", hint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Scorecard_ExportContainsAllChecks()
    {
        var md = ConnectivityScorecard.ExportMarkdown("v2rayF", new Dictionary<string, bool?>());
        foreach (var check in ConnectivityScorecard.AppChecks)
            Assert.Contains(check, md);
    }
}
