using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace v2rayF.Services;

public sealed class SubscriptionService
{
    public async Task<IReadOnlyList<Models.ProxyServer>> FetchAsync(
        string url,
        bool viaLocalProxy = false,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("Subscription URL must be http or https.");

        using var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(15)
        };

        if (viaLocalProxy)
        {
            handler.Proxy = new WebProxy($"http://127.0.0.1:{XrayConfigBuilder.HttpPort}");
            handler.UseProxy = true;
        }

        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("User-Agent", "v2rayF/1.2");

        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ShareLinkParser.ParseBulk(body);
    }
}
