using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using v2rayF.Models;
using v2rayF.Services;

namespace v2rayF.Core.Tests;

public class PulseFreeSlotMergerTests
{
    static ProxyServer Free(string host, int? latency, string idSuffix = "") => new()
    {
        Id = Guid.NewGuid(),
        Name = $"F-{host}",
        Protocol = ProxyProtocol.VLESS,
        Address = host,
        Port = 443,
        UserId = "11111111-1111-1111-1111-111111111111" + idSuffix,
        Source = PulseFreeConstants.SourceId,
        LatencyMs = latency,
        RawLink = $"vless://u@{host}:443"
    };

    static ProxyServer User(string host) => new()
    {
        Id = Guid.NewGuid(),
        Name = "mine",
        Protocol = ProxyProtocol.VLESS,
        Address = host,
        Port = 443,
        UserId = "22222222-2222-2222-2222-222222222222",
        Source = "",
        LatencyMs = 40,
        RawLink = $"vless://u@{host}:443#mine"
    };

    static ProxyServer Cand(string host, int latency) => new()
    {
        Id = Guid.NewGuid(),
        Name = $"C-{host}",
        Protocol = ProxyProtocol.VLESS,
        Address = host,
        Port = 443,
        UserId = "33333333-3333-3333-3333-333333333333",
        LatencyMs = latency,
        RawLink = $"vless://c@{host}:443"
    };

    [Fact]
    public void Keeps_good_free_and_fills_open_slots()
    {
        var existing = new List<ProxyServer>
        {
            Free("1.1.1.1", 80),
            Free("2.2.2.2", 200), // replaceable
            User("9.9.9.9"),
        };
        var candidates = new[]
        {
            Cand("3.3.3.3", 50),
            Cand("4.4.4.4", 60),
            Cand("5.5.5.5", 70),
            Cand("6.6.6.6", 90),
        };

        var r = PulseFreeSlotMerger.Merge(existing, candidates);
        Assert.Single(r.ToRemove);
        Assert.Equal("2.2.2.2", r.ToRemove[0].Address);
        Assert.Equal(1, r.Kept);
        Assert.Equal(4, r.ToAdd.Count); // 1 kept + 4 add = 5
        Assert.All(r.ToAdd, s => Assert.Equal(PulseFreeConstants.SourceId, s.Source));
        Assert.Contains(existing, s => s.Address == "9.9.9.9" && !s.IsPulseFree);
    }

    [Fact]
    public void Never_exceeds_five_free_slots()
    {
        var existing = Enumerable.Range(0, 5)
            .Select(i => Free($"10.0.0.{i}", 40 + i))
            .ToList();
        var candidates = Enumerable.Range(0, 10)
            .Select(i => Cand($"20.0.0.{i}", 30 + i))
            .ToArray();

        var r = PulseFreeSlotMerger.Merge(existing, candidates);
        Assert.Empty(r.ToRemove); // all good
        Assert.Empty(r.ToAdd);
        Assert.Equal(5, r.Kept);
        Assert.Equal(0, r.OpenSlots);
    }

    [Fact]
    public void Rejects_candidates_over_150ms()
    {
        var r = PulseFreeSlotMerger.Merge(
            Array.Empty<ProxyServer>(),
            new[] { Cand("1.2.3.4", 151), Cand("5.6.7.8", 149) });
        Assert.Single(r.ToAdd);
        Assert.Equal("5.6.7.8", r.ToAdd[0].Address);
    }

    [Fact]
    public void Accept_threshold_allows_iran_realistic_rtt()
    {
        var r = PulseFreeSlotMerger.Merge(
            Array.Empty<ProxyServer>(),
            new[] { Cand("1.2.3.4", 300), Cand("5.6.7.8", 500) },
            maxLatencyMs: PulseFreeConstants.AcceptLatencyMs);
        Assert.Single(r.ToAdd);
        Assert.Equal("1.2.3.4", r.ToAdd[0].Address);
    }

    [Fact]
    public void Dedupes_against_existing_endpoints()
    {
        var existing = new List<ProxyServer> { Free("1.1.1.1", 40) };
        var dup = Cand("1.1.1.1", 20);
        dup.RawLink = existing[0].RawLink;
        var r = PulseFreeSlotMerger.Merge(existing, new[] { dup, Cand("8.8.8.8", 30) });
        Assert.DoesNotContain(r.ToAdd, s => s.Address == "1.1.1.1");
        Assert.Contains(r.ToAdd, s => s.Address == "8.8.8.8");
    }

    [Fact]
    public void Never_lists_user_servers_in_to_remove()
    {
        var user = User("7.7.7.7");
        user.LatencyMs = 999;
        var r = PulseFreeSlotMerger.Merge(
            new[] { user, Free("1.1.1.1", 400) },
            new[] { Cand("2.2.2.2", 40) });
        Assert.DoesNotContain(r.ToRemove, s => s.Id == user.Id);
        Assert.Contains(r.ToRemove, s => s.Address == "1.1.1.1");
    }
}
