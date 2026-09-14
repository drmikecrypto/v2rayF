using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace v2rayF.Services;

/// <summary>
/// Resolves PulseConfigs Free servers (top5) URLs for the in-app button.
/// Prefers Cloudflare Worker mirror, then raw GitHub, then jsDelivr.
/// </summary>
public static class PulseFreeServers
{
    public const string DefaultOwnerRepo = "drmikecrypto/PulseConfigs";

    public static string RawTop5 { get; set; } =
        $"https://raw.githubusercontent.com/{DefaultOwnerRepo}/main/top5.txt";

    public static string CdnTop5 { get; set; } =
        $"https://cdn.jsdelivr.net/gh/{DefaultOwnerRepo}@main/top5.txt";

    /// <summary>Optional Worker base, e.g. https://pulseconfigs-mirror.example.workers.dev</summary>
    public static string? WorkerBase { get; set; }

    public static string IndexRaw { get; set; } =
        $"https://raw.githubusercontent.com/{DefaultOwnerRepo}/main/index.json";

    public static IReadOnlyList<string> BuildTop5Candidates(string? workerBase = null)
    {
        var list = new List<string>();
        var wb = (workerBase ?? WorkerBase)?.Trim().TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(wb))
            list.Add($"{wb}/top5.txt");

        list.Add(RawTop5);
        list.Add(CdnTop5);
        return list;
    }

    public static async Task<string?> ResolveTop5UrlAsync(
        HttpClient http,
        CancellationToken ct = default,
        string? workerBase = null)
    {
        // Try index.json for machine contract first
        foreach (var indexUrl in IndexCandidates(workerBase))
        {
            try
            {
                using var resp = await http.GetAsync(indexUrl, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    continue;
                await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
                if (doc.RootElement.TryGetProperty("v2rayF", out var v2))
                {
                    foreach (var key in new[] { "top5_button", "top5_button_worker", "top5_button_raw" })
                    {
                        if (v2.TryGetProperty(key, out var p) &&
                            p.ValueKind == JsonValueKind.String)
                        {
                            var s = p.GetString();
                            if (!string.IsNullOrWhiteSpace(s))
                                return s;
                        }
                    }
                }
            }
            catch
            {
                // try next
            }
        }

        var candidates = BuildTop5Candidates(workerBase);
        return candidates.Count > 0 ? candidates[0] : RawTop5;
    }

    static IEnumerable<string> IndexCandidates(string? workerBase)
    {
        var wb = (workerBase ?? WorkerBase)?.Trim().TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(wb))
            yield return $"{wb}/index.json";
        yield return IndexRaw;
        yield return $"https://cdn.jsdelivr.net/gh/{DefaultOwnerRepo}@main/index.json";
    }
}
