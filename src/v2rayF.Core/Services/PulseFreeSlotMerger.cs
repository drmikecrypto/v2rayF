using System;
using System.Collections.Generic;
using System.Linq;
using v2rayF.Models;

namespace v2rayF.Services;

/// <summary>
/// Pure merge rules for Free slots: keep ≤150ms free servers, replace slow/failed free only,
/// never touch user-imported servers, never exceed <see cref="PulseFreeConstants.MaxSlots"/>.
/// </summary>
public static class PulseFreeSlotMerger
{
    public sealed record Result(
        IReadOnlyList<ProxyServer> ToRemove,
        IReadOnlyList<ProxyServer> ToAdd,
        int Kept,
        int Replaced,
        int Added,
        int OpenSlots);

    public static Result Merge(
        IEnumerable<ProxyServer> existing,
        IEnumerable<ProxyServer> probedCandidates,
        int maxSlots = PulseFreeConstants.MaxSlots,
        int maxLatencyMs = PulseFreeConstants.MaxLatencyMs)
    {
        var all = existing?.ToList() ?? [];
        var inventory = all
            .Where(s => s.IsPulseFree)
            .OrderBy(s => s.LatencyMs is > 0 ? s.LatencyMs : int.MaxValue)
            .ToList();

        // If somehow over cap, mark excess as replaceable (worst first).
        var keepers = inventory
            .Where(IsGoodFree)
            .Take(maxSlots)
            .ToList();

        var replaceable = inventory
            .Where(s => !keepers.Contains(s))
            .ToList();

        // Also replace keepers beyond maxSlots (shouldn't happen after Take)
        if (keepers.Count > maxSlots)
        {
            replaceable.AddRange(keepers.Skip(maxSlots));
            keepers = keepers.Take(maxSlots).ToList();
        }

        var occupiedEndpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in all.Where(s => !replaceable.Contains(s)))
            occupiedEndpoints.Add(EndpointKey(s));

        foreach (var k in keepers)
            occupiedEndpoints.Add(EndpointKey(k));

        var open = Math.Max(0, maxSlots - keepers.Count);
        var toAdd = new List<ProxyServer>();

        foreach (var c in probedCandidates
                     .Where(IsGoodFree)
                     .OrderBy(s => s.LatencyMs ?? int.MaxValue))
        {
            if (toAdd.Count >= open)
                break;

            var key = EndpointKey(c);
            if (occupiedEndpoints.Contains(key))
                continue;

            // Tag as free for persistence
            c.Source = PulseFreeConstants.SourceId;
            toAdd.Add(c);
            occupiedEndpoints.Add(key);
        }

        var openAfter = Math.Max(0, maxSlots - keepers.Count - toAdd.Count);
        return new Result(
            ToRemove: replaceable,
            ToAdd: toAdd,
            Kept: keepers.Count,
            Replaced: Math.Min(replaceable.Count, toAdd.Count),
            Added: toAdd.Count,
            OpenSlots: openAfter);

        bool IsGoodFree(ProxyServer s) =>
            s.LatencyMs is int ms && ms >= 1 && ms <= maxLatencyMs;
    }

    public static string EndpointKey(ProxyServer s)
    {
        if (!string.IsNullOrWhiteSpace(s.RawLink))
            return "raw:" + s.RawLink.Trim();
        return $"ep:{(s.Protocol)}|{(s.Address ?? "").Trim().ToLowerInvariant()}|{s.Port}|{(s.UserId ?? "")}";
    }

    public static bool SameEndpoint(ProxyServer a, ProxyServer b) =>
        EndpointKey(a).Equals(EndpointKey(b), StringComparison.OrdinalIgnoreCase);
}
