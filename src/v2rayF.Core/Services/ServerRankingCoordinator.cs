using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using v2rayF.Models;

namespace v2rayF.Services;

/// <summary>
/// Ranks all servers (TCP + proxy path) for startup and Test All — shared probe logic.
/// </summary>
public sealed class ServerRankingCoordinator
{
    public const int StartupRankThrottleMinutes = 10;

    private readonly LatencyService _latency;

    public ServerRankingCoordinator(LatencyService latency) => _latency = latency;

    public static bool ShouldRunStartupRank(AppSettings settings, DateTimeOffset nowUtc)
    {
        if (!settings.StartupRankServersEnabled)
            return false;

        if (string.IsNullOrWhiteSpace(settings.LastStartupRankUtc))
            return true;

        if (!DateTimeOffset.TryParse(settings.LastStartupRankUtc, out var last))
            return true;

        return (nowUtc - last).TotalMinutes >= StartupRankThrottleMinutes;
    }

    public async Task RankAllAsync(
        IList<ProxyServer> servers,
        bool enableFragment,
        CancellationToken cancellationToken = default)
    {
        if (servers.Count == 0)
            return;

        var tcpMs = new int?[servers.Count];
        await Task.WhenAll(servers.Select(async (server, i) =>
        {
            tcpMs[i] = await _latency.MeasureTcpOnlyAsync(server, cancellationToken).ConfigureAwait(false);
        })).ConfigureAwait(false);

        await Task.WhenAll(servers.Select(async (server, i) =>
        {
            if (LatencyService.ShouldSkipProxyPath(server, tcpMs[i]))
            {
                server.SetLatency(-1);
                return;
            }

            var proxyMs = await _latency
                .MeasureProxyPathAsync(server, cancellationToken, enableFragment)
                .ConfigureAwait(false);
            var display = proxyMs is >= 0
                ? (tcpMs[i] is > 0 ? tcpMs[i] : proxyMs)
                : -1;
            server.SetLatency(display);
        })).ConfigureAwait(false);
    }

    public static ProxyServer? PickFastest(IEnumerable<ProxyServer> servers) =>
        ServerLatencySort.Order(servers).FirstOrDefault(s => s.LatencyMs is > 0);
}
