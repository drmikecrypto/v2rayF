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
    /// <summary>Stop proxy-path probes once this many working paths are found.</summary>
    public const int EarlyExitGoodPeers = 3;
    /// <summary>Hard cap on expensive proxy-path probes per rank pass.</summary>
    public const int MaxProxyPathProbes = 6;

    private readonly LatencyService _latency;

    public SmartConnectService(LatencyService latency)
    {
        _latency = latency;
    }

    public sealed record RankedServer(ProxyServer Server, int Score, int LatencyMs, bool ProxyPathOk);

    /// <summary>
    /// Two-phase: concurrent TCP prefilter, then proxy-path probe on the top candidates only.
    /// Early-exits once <see cref="EarlyExitGoodPeers"/> proxy-path OK results are collected.
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

        var reachable = tcpResults
            .Where(t => t.TcpMs < int.MaxValue)
            .OrderBy(t => t.TcpMs)
            .ToList();

        var shortlist = reachable
            .Take(Math.Min(TcpPrefilterLimit, reachable.Count))
            .Select(t => t.Server)
            .ToList();

        // Prefer REALITY among reachable nodes that did not make the TCP top-N.
        foreach (var (server, _) in reachable)
        {
            if (shortlist.Count >= TcpPrefilterLimit)
                break;
            if (shortlist.Any(s => s.Id == server.Id))
                continue;
            if (string.Equals(server.Security, "reality", StringComparison.OrdinalIgnoreCase))
                shortlist.Add(server);
        }

        // Phase 2 — proxy path on shortlist; early-exit when enough good peers found.
        var ranked = new List<RankedServer>(shortlist.Count);
        var goodCount = 0;
        var probed = 0;
        foreach (var server in shortlist)
        {
            if (probed >= MaxProxyPathProbes || goodCount >= EarlyExitGoodPeers)
                break;

            cancellationToken.ThrowIfCancellationRequested();
            probed++;

            LatencyService.LatencyResult result;
            try
            {
                using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                probeCts.CancelAfter(LatencyService.RankProbeTimeoutMs);
                result = await _latency.MeasureDetailedAsync(server, probeCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                result = new LatencyService.LatencyResult(-1, false);
            }

            var latency = result.LatencyMs is > 0 ? result.LatencyMs.Value : int.MaxValue;
            var realityBonus = string.Equals(server.Security, "reality", StringComparison.OrdinalIgnoreCase) ? 5 : 0;
            var score = result.ProxyPathOk ? Math.Max(0, latency - realityBonus) : int.MaxValue - 1;
            ranked.Add(new RankedServer(
                server,
                score,
                latency == int.MaxValue ? -1 : latency,
                result.ProxyPathOk));

            if (result.ProxyPathOk)
                goodCount++;
        }

        // Append remaining shortlist / TCP leftovers as TCP-only ranks (no more core boots).
        foreach (var server in shortlist)
        {
            if (ranked.Any(r => r.Server.Id == server.Id))
                continue;
            var tcpMs = tcpResults.First(t => t.Server.Id == server.Id).TcpMs;
            ranked.Add(new RankedServer(server, int.MaxValue - 1, tcpMs == int.MaxValue ? -1 : tcpMs, false));
        }

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
        // Only proxy-path OK peers are connectable winners (TCP-only is not a working tunnel).
        var ordered = ranked
            .Where(r => r.ProxyPathOk)
            .Select(r => r.Server)
            .Take(MaxFailoverCandidates)
            .ToList();

        if (ordered.Count == 0)
            return [];

        ProxyServer? boost = null;
        if (preferred is not null && ordered.Any(s => s.Id == preferred.Id))
            boost = preferred;
        else if (!string.IsNullOrWhiteSpace(lastGoodServerId))
            boost = ordered.FirstOrDefault(s => s.Id.ToString() == lastGoodServerId);

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
