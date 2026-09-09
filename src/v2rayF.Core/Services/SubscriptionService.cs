using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using v2rayF.Models;

namespace v2rayF.Services;

/// <summary>
/// Subscription fetch with CN/IR-friendly failure hints and GitHub/raw mirror retries.
/// </summary>
public sealed class SubscriptionService
{
    public async Task<IReadOnlyList<ProxyServer>> FetchAsync(
        string url,
        bool viaLocalProxy = false,
        CancellationToken cancellationToken = default)
    {
        var result = await FetchDetailedAsync(url, viaLocalProxy, cancellationToken).ConfigureAwait(false);
        return result.Servers;
    }

    public async Task<SubscriptionFetchResult> FetchDetailedAsync(
        string url,
        bool viaLocalProxy = false,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("Subscription URL must be http or https.");

        var candidates = SubscriptionMirrorHints.BuildFetchCandidates(uri);
        Exception? lastError = null;

        foreach (var candidate in candidates)
        {
            try
            {
                var body = await DownloadBodyAsync(candidate, viaLocalProxy, cancellationToken)
                    .ConfigureAwait(false);
                var servers = ConfigImportParser.Parse(body);
                var mirrorNote = !string.Equals(candidate.ToString(), uri.ToString(), StringComparison.Ordinal)
                    ? $"Used mirror: {candidate.Host}"
                    : null;
                return new SubscriptionFetchResult(servers, mirrorNote, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
            }
        }

        var hint = SubscriptionMirrorHints.FormatFailureHint(uri, lastError);
        throw new InvalidOperationException(hint, lastError);
    }

    private static async Task<string> DownloadBodyAsync(
        Uri uri,
        bool viaLocalProxy,
        CancellationToken cancellationToken)
    {
        using var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(15)
        };

        if (viaLocalProxy)
        {
            handler.Proxy = new System.Net.WebProxy($"http://127.0.0.1:{XrayConfigBuilder.HttpPort}");
            handler.UseProxy = true;
        }

        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("User-Agent", "v2rayF/1.2");

        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed record SubscriptionFetchResult(
    IReadOnlyList<ProxyServer> Servers,
    string? MirrorNote,
    string? Hint);
