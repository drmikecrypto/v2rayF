using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace v2rayF.Services;

/// <summary>
/// Resolves PulseConfigs Free shortlist URLs and soft-refresh endpoint.
/// Prefers Cloudflare Worker mirror, then raw GitHub, then jsDelivr.
/// </summary>
public static class PulseFreeServers
{
    public const string DefaultOwnerRepo = "drmikecrypto/PulseConfigs";

    public static string RawTop5 { get; set; } =
        $"https://raw.githubusercontent.com/{DefaultOwnerRepo}/main/top5.txt";

    public static string CdnTop5 { get; set; } =
        $"https://cdn.jsdelivr.net/gh/{DefaultOwnerRepo}@main/top5.txt";

    public static string RawCandidatesJson { get; set; } =
        $"https://raw.githubusercontent.com/{DefaultOwnerRepo}/main/candidates.json";

    public static string CdnCandidatesJson { get; set; } =
        $"https://cdn.jsdelivr.net/gh/{DefaultOwnerRepo}@main/candidates.json";

    public static string RawCandidatesTxt { get; set; } =
        $"https://raw.githubusercontent.com/{DefaultOwnerRepo}/main/candidates.txt";

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

    /// <summary>Prefer diverse shortlist for on-device ≤150ms filtering.</summary>
    public static IReadOnlyList<string> BuildShortlistCandidates(string? workerBase = null)
    {
        var list = new List<string>();
        var wb = (workerBase ?? WorkerBase)?.Trim().TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(wb))
        {
            list.Add($"{wb}/candidates.json");
            list.Add($"{wb}/candidates.txt");
            list.Add($"{wb}/top5.txt");
        }

        list.Add(RawCandidatesJson);
        list.Add(CdnCandidatesJson);
        list.Add(RawCandidatesTxt);
        list.Add(RawTop5);
        list.Add(CdnTop5);
        return list;
    }

    public static async Task<string?> ResolveTop5UrlAsync(
        HttpClient http,
        CancellationToken ct = default,
        string? workerBase = null)
    {
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

    /// <summary>
    /// Resolve best shortlist URL (candidates.json preferred). Also applies WorkerBase from index when present.
    /// </summary>
    public static async Task<string?> ResolveShortlistUrlAsync(
        HttpClient http,
        CancellationToken ct = default,
        string? workerBase = null)
    {
        var wb = (workerBase ?? WorkerBase)?.Trim().TrimEnd('/');

        foreach (var indexUrl in IndexCandidates(wb))
        {
            try
            {
                using var resp = await http.GetAsync(indexUrl, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    continue;
                await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

                if (doc.RootElement.TryGetProperty("v2rayF", out var v2) &&
                    v2.TryGetProperty("index_worker", out var iw) &&
                    iw.ValueKind == JsonValueKind.String)
                {
                    var workerIndex = iw.GetString();
                    if (!string.IsNullOrWhiteSpace(workerIndex) &&
                        Uri.TryCreate(workerIndex, UriKind.Absolute, out var wiUri))
                    {
                        var baseGuess = wiUri.GetLeftPart(UriPartial.Authority);
                        if (!string.IsNullOrWhiteSpace(baseGuess))
                            WorkerBase = baseGuess.TrimEnd('/');
                    }
                }

                if (doc.RootElement.TryGetProperty("urls", out var urls) &&
                    urls.TryGetProperty("candidates", out var cand) &&
                    cand.ValueKind == JsonValueKind.Object)
                {
                    foreach (var key in new[] { "worker", "raw", "cdn" })
                    {
                        if (cand.TryGetProperty(key, out var p) &&
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

        var list = BuildShortlistCandidates(WorkerBase ?? wb);
        return list.Count > 0 ? list[0] : RawCandidatesJson;
    }

    /// <summary>
    /// Normalize subscription body: if candidates.json, extract raw share links; else return as-is.
    /// </summary>
    public static string NormalizeSubscriptionBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "";

        var trimmed = body.TrimStart();
        if (!trimmed.StartsWith('{'))
            return body;

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("candidates", out var arr) ||
                arr.ValueKind != JsonValueKind.Array)
                return body;

            var sb = new StringBuilder();
            foreach (var row in arr.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object)
                    continue;
                if (row.TryGetProperty("raw", out var raw) && raw.ValueKind == JsonValueKind.String)
                {
                    var link = raw.GetString();
                    if (!string.IsNullOrWhiteSpace(link))
                        sb.AppendLine(link);
                }
            }

            return sb.Length > 0 ? sb.ToString() : body;
        }
        catch (JsonException)
        {
            return body;
        }
    }

    /// <summary>Fire-and-forget pool refresh (Worker rate-limits; no secrets in app).</summary>
    public static async Task TryRequestPoolRefreshAsync(
        HttpClient http,
        CancellationToken ct = default,
        string? workerBase = null)
    {
        var wb = (workerBase ?? WorkerBase)?.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(wb))
            return;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{wb}/refresh");
            req.Headers.TryAddWithoutValidation("User-Agent", "v2rayF-PulseFree/1.0");
            using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
            _ = resp;
        }
        catch
        {
            // Soft refresh is best-effort.
        }
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
