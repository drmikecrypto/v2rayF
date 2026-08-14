using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using v2rayF.Models;

namespace v2rayF.Services;

public sealed class SmartConnectService
{
    public const int MaxFailoverCandidates = 5;
    public const int MaxMultipathCandidates = 3;
    public const int TcpPrefilterLimit = 8;
    /// <summary>Reserve Reality peers in the shortlist even when TCP top-N is full.</summary>
    public const int ReservedRealitySlots = 2;
    /// <summary>Stop proxy-path probes once this many working paths are found.</summary>
    public const int EarlyExitGoodPeers = 3;
    /// <summary>Hard cap on expensive proxy-path probes per rank pass (small lists).</summary>
    public const int MaxProxyPathProbes = 6;
    /// <summary>Raised probe cap for deep_fix-sized subscriptions (≥10 servers).</summary>
    public const int MaxProxyPathProbesLargeList = 10;

    private readonly LatencyService _latency;

    public SmartConnectService(LatencyService latency)
    {
        _latency = latency;
    }

    public sealed record RankedServer(
        ProxyServer Server,
        int Score,
        int LatencyMs,
        bool ProxyPathOk,
        int TcpMs = -1)
    {
        /// <summary>List column: TCP RTT when the tunnel works; timeout otherwise.</summary>
        public int UiLatencyMs => ProxyPathOk && TcpMs > 0 ? TcpMs : ProxyPathOk ? LatencyMs : -1;
    }

    /// <summary>
    /// Two-phase: concurrent TCP prefilter, then proxy-path probe on a transport-diverse shortlist.
    /// When <paramref name="preferred"/> is set, that row is always shortlisted and probed first.
    /// </summary>
    public async Task<IReadOnlyList<RankedServer>> RankAsync(
        IReadOnlyList<ProxyServer> servers,
        CancellationToken cancellationToken = default,
        bool enableFragment = false,
        ProxyServer? preferred = null)
    {
        if (servers.Count == 0)
            return [];

        var tcpResults = new (ProxyServer Server, int TcpMs)[servers.Count];
        await Task.WhenAll(servers.Select(async (server, index) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ms = await ProbeTcpPrefilterAsync(_latency, server, cancellationToken).ConfigureAwait(false);
            tcpResults[index] = (server, ms is > 0 ? ms.Value : int.MaxValue);
        })).ConfigureAwait(false);

        var reachable = tcpResults
            .Where(t => t.TcpMs < int.MaxValue)
            .OrderBy(t => t.TcpMs)
            .ToList();

        var shortlist = BuildShortlist(tcpResults, reachable, preferred);
        var maxProbes = servers.Count >= 10 ? MaxProxyPathProbesLargeList : MaxProxyPathProbes;

        var ranked = new List<RankedServer>(shortlist.Count);
        var goodCount = 0;
        var probed = 0;
        foreach (var server in shortlist)
        {
            if (probed >= maxProbes || goodCount >= EarlyExitGoodPeers)
                break;

            cancellationToken.ThrowIfCancellationRequested();
            probed++;

            LatencyService.LatencyResult result;
            try
            {
                using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                probeCts.CancelAfter(LatencyService.RankProbeTimeoutMs);
                result = await _latency.MeasureDetailedAsync(server, probeCts.Token, enableFragment)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                result = new LatencyService.LatencyResult(null, -1, false);
            }

            var proxyMs = result.ProxyPathMs is > 0 ? result.ProxyPathMs.Value : int.MaxValue;
            var tcpMs = result.TcpMs is > 0 ? result.TcpMs.Value : -1;
            var realityBonus = string.Equals(server.Security, "reality", StringComparison.OrdinalIgnoreCase) ? 5 : 0;
            var score = result.ProxyPathOk ? Math.Max(0, proxyMs - realityBonus) : int.MaxValue - 1;
            ranked.Add(new RankedServer(
                server,
                score,
                proxyMs == int.MaxValue ? -1 : proxyMs,
                result.ProxyPathOk,
                tcpMs));

            if (result.ProxyPathOk)
                goodCount++;
        }

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

    /// <summary>
    /// Transport-diverse shortlist: best TCP per protocol/network/security family, Reality reserve,
    /// domain hosts that failed system TCP, and user-selected row always included + probed first.
    /// </summary>
    public static List<ProxyServer> BuildShortlist(
        (ProxyServer Server, int TcpMs)[] tcpResults,
        List<(ProxyServer Server, int TcpMs)> reachable,
        ProxyServer? preferred = null)
    {
        var byTcp = reachable.OrderBy(t => t.TcpMs).ToList();
        var shortlist = new List<ProxyServer>();
        var seenIds = new HashSet<Guid>();

        void TryAdd(ProxyServer server)
        {
            if (seenIds.Add(server.Id))
                shortlist.Add(server);
        }

        if (preferred is not null)
            TryAdd(preferred);

        foreach (var entry in byTcp
                     .GroupBy(x => TransportFamilyKey(x.Server))
                     .Select(g => g.OrderBy(x => x.TcpMs).First())
                     .OrderBy(x => x.TcpMs))
            TryAdd(entry.Server);

        foreach (var entry in byTcp
                     .Where(x => string.Equals(x.Server.Security, "reality", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(x => x.TcpMs)
                     .Take(ReservedRealitySlots))
            TryAdd(entry.Server);

        foreach (var entry in byTcp)
        {
            if (shortlist.Count >= TcpPrefilterLimit)
                break;
            TryAdd(entry.Server);
        }

        foreach (var (server, tcpMs) in tcpResults)
        {
            if (tcpMs < int.MaxValue)
                continue;
            if (!IsDomainAddress(server.Address))
                continue;
            if (shortlist.Any(s => s.Id == server.Id))
                continue;
            if (shortlist.Count >= TcpPrefilterLimit)
            {
                var idx = shortlist.FindLastIndex(s =>
                    !string.Equals(s.Security, "reality", StringComparison.OrdinalIgnoreCase) &&
                    !IsDomainAddress(s.Address) &&
                    (preferred is null || s.Id != preferred.Id));
                if (idx < 0)
                    break;
                shortlist.RemoveAt(idx);
            }

            shortlist.Add(server);
            seenIds.Add(server.Id);
        }

        if (preferred is not null)
        {
            var idx = shortlist.FindIndex(s => s.Id == preferred.Id);
            if (idx > 0)
            {
                var item = shortlist[idx];
                shortlist.RemoveAt(idx);
                shortlist.Insert(0, item);
            }
        }

        return shortlist;
    }

    internal static string TransportFamilyKey(ProxyServer server)
    {
        var protocol = server.Protocol.ToString().ToLowerInvariant();
        var network = ShareLinkParser.NormalizeNetwork(server.Network);
        var security = ShareLinkParser.NormalizeSecurity(server.Security);
        if (string.IsNullOrEmpty(security))
            security = "none";
        return $"{protocol}/{network}/{security}";
    }

    internal static async Task<int?> ProbeTcpPrefilterAsync(
        LatencyService latency,
        ProxyServer server,
        CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(server.Address, out _))
            return await latency.MeasureTcpOnlyAsync(server, cancellationToken).ConfigureAwait(false);

        var network = ShareLinkParser.NormalizeNetwork(server.Network);
        if (network is "ws" or "grpc" or "httpupgrade" or "h2" or "xhttp")
            return null;

        return await latency.MeasureTcpOnlyAsync(server, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsDomainAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return false;
        return !IPAddress.TryParse(address, out _);
    }

    public IReadOnlyList<ProxyServer> SelectConnectOrder(
        IReadOnlyList<RankedServer> ranked,
        ProxyServer? preferred,
        string? lastGoodServerId)
    {
        var ordered = ranked
            .Where(r => r.ProxyPathOk)
            .Select(r => r.Server)
            .Take(MaxFailoverCandidates)
            .ToList();

        if (ordered.Count == 0)
            return [];

        return BoostPreferred(ordered, preferred, lastGoodServerId);
    }

    public IReadOnlyList<ProxyServer> SelectSurviveConnectOrder(
        IReadOnlyList<RankedServer> ranked,
        ProxyServer? preferred,
        string? lastGoodServerId)
    {
        var ordered = ranked
            .OrderBy(r => r.ProxyPathOk ? 0 : 1)
            .ThenBy(r => string.Equals(r.Server.Security, "reality", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(r => IsDomainAddress(r.Server.Address) ? 0 : 1)
            .ThenBy(r => r.Score)
            .Select(r => r.Server)
            .DistinctBy(s => s.Id)
            .Take(MaxFailoverCandidates)
            .ToList();

        if (ordered.Count == 0)
            return [];

        return BoostPreferred(ordered, preferred, lastGoodServerId);
    }

    private static List<ProxyServer> BoostPreferred(
        List<ProxyServer> ordered,
        ProxyServer? preferred,
        string? lastGoodServerId)
    {
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
