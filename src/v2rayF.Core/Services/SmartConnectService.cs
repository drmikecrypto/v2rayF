using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using v2rayF.Models;

namespace v2rayF.Services;

public sealed class SmartConnectService
{
    public const int MaxFailoverCandidates = 5;
    public const int MaxMultipathCandidates = 3;
    public const int TcpPrefilterLimit = 8;

    private readonly LatencyService _latency;

    public SmartConnectService(LatencyService latency)
    {
        _latency = latency;
    }

    public sealed record RankedServer(ProxyServer Server, int Score, int LatencyMs, bool ProxyPathOk);

    /// <summary>
    /// Two-phase: concurrent TCP prefilter, then proxy-path probe on the top candidates only.
    /// </summary>
    public async Task<IReadOnlyList<RankedServer>> RankAsync(
        IReadOnlyList<ProxyServer> servers,
        CancellationToken cancellationToken = default)
    {
        if (servers.Count == 0)
            return [];

        // Phase 1 — cheap TCP RTT for all (parallel).
        var tcpResults = new (ProxyServer Server, int TcpMs)[servers.Count];
        await Task.WhenAll(servers.Select(async (server, index) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ms = await _latency.MeasureTcpOnlyAsync(server, cancellationToken).ConfigureAwait(false);
            tcpResults[index] = (server, ms is > 0 ? ms.Value : int.MaxValue);
        })).ConfigureAwait(false);

        var shortlist = tcpResults
            .OrderBy(t => t.TcpMs)
            .Take(Math.Min(TcpPrefilterLimit, servers.Count))
            .Select(t => t.Server)
            .ToList();

        // Always include preferred-looking REALITY nodes that TCP could reach.
        foreach (var server in servers)
        {
            if (shortlist.Count >= TcpPrefilterLimit)
                break;
            if (shortlist.Any(s => s.Id == server.Id))
                continue;
            if (string.Equals(server.Security, "reality", StringComparison.OrdinalIgnoreCase))
                shortlist.Add(server);
        }

        // Phase 2 — proxy path (serialized inside LatencyService) on shortlist only.
        var ranked = new List<RankedServer>(shortlist.Count);
        foreach (var server in shortlist)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _latency.MeasureDetailedAsync(server, cancellationToken).ConfigureAwait(false);
            var latency = result.LatencyMs is > 0 ? result.LatencyMs.Value : int.MaxValue;
            var realityBonus = string.Equals(server.Security, "reality", StringComparison.OrdinalIgnoreCase) ? 5 : 0;
            var score = result.ProxyPathOk ? Math.Max(0, latency - realityBonus) : int.MaxValue - 1;
            ranked.Add(new RankedServer(
                server,
                score,
                latency == int.MaxValue ? -1 : latency,
                result.ProxyPathOk));
        }

        // Append TCP-only leftovers so failover still has options if all proxy probes fail.
        foreach (var (server, tcpMs) in tcpResults.OrderBy(t => t.TcpMs))
        {
            if (ranked.Any(r => r.Server.Id == server.Id))
                continue;
            ranked.Add(new RankedServer(server, int.MaxValue - 1, tcpMs == int.MaxValue ? -1 : tcpMs, false));
        }

        return ranked
            .OrderBy(r => r.ProxyPathOk ? 0 : 1)
            .ThenBy(r => r.Score)
            .ThenBy(r => r.Server.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<ProxyServer> SelectConnectOrder(
        IReadOnlyList<RankedServer> ranked,
        ProxyServer? preferred,
        string? lastGoodServerId)
    {
        var live = ranked.Where(r => r.ProxyPathOk).Select(r => r.Server).Take(MaxFailoverCandidates).ToList();
        var ordered = live.Count > 0
            ? live
            : ranked.Where(r => r.LatencyMs >= 0).Select(r => r.Server).Take(MaxFailoverCandidates).ToList();

        if (ordered.Count == 0)
            ordered = ranked.Select(r => r.Server).Take(MaxFailoverCandidates).ToList();

        ProxyServer? boost = null;
        if (!string.IsNullOrWhiteSpace(lastGoodServerId))
            boost = ordered.FirstOrDefault(s => s.Id.ToString() == lastGoodServerId)
                    ?? ranked.FirstOrDefault(r => r.Server.Id.ToString() == lastGoodServerId && r.ProxyPathOk)?.Server;
        boost ??= preferred is not null
            ? ordered.FirstOrDefault(s => s.Id == preferred.Id)
            : null;

        if (boost is not null)
        {
            ordered.RemoveAll(s => s.Id == boost.Id);
            ordered.Insert(0, boost);
        }

        return ordered;
    }

    public IReadOnlyList<ProxyServer> PickMultipathPeers(
        IReadOnlyList<RankedServer> ranked,
        ProxyServer primary)
    {
        var peers = new List<ProxyServer> { primary };
        foreach (var item in ranked.Where(r => r.ProxyPathOk))
        {
            if (peers.Count >= MaxMultipathCandidates)
                break;
            if (item.Server.Id == primary.Id)
                continue;
            peers.Add(item.Server);
        }

        return peers;
    }
}
