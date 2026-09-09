using System;
using v2rayF.Services;

namespace v2rayF.Core.Tests;

public class AndroidTunRebindPolicyTests
{
    [Fact]
    public void TunPathFailed_AlwaysForceRebind()
    {
        var last = DateTimeOffset.UtcNow;
        Assert.True(AndroidTunRebindPolicy.ShouldForceRebind(
            AndroidTunRebindPolicy.Reason.TunPathFailed,
            last,
            last,
            minIntervalSeconds: 90));
    }

    [Fact]
    public void SessionResume_RespectsThrottle()
    {
        var last = DateTimeOffset.Parse("2026-09-09T12:00:00Z");
        Assert.False(AndroidTunRebindPolicy.ShouldForceRebind(
            AndroidTunRebindPolicy.Reason.SessionResume,
            last,
            last.AddSeconds(30),
            minIntervalSeconds: 90));
        Assert.True(AndroidTunRebindPolicy.ShouldForceRebind(
            AndroidTunRebindPolicy.Reason.SessionResume,
            last,
            last.AddSeconds(90),
            minIntervalSeconds: 90));
    }
}
