using System;
using System.Collections.Generic;
using System.Linq;
using v2rayF.Models;

namespace v2rayF.Services;

public static class ServerLatencySort
{
    public static List<ProxyServer> Order(IEnumerable<ProxyServer> servers) =>
        servers
            .OrderBy(s => s.LatencyMs is > 0 ? 0 : 1)
            .ThenBy(s => s.LatencyMs is > 0 ? s.LatencyMs!.Value : int.MaxValue)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
