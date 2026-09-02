using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Net;
using Java.Net;
using v2rayF.Services;

namespace v2rayF.Android.Services;

/// <summary>
/// Health probe through the active VPN Network (v2rayF is VPN-disallowed — default HttpClient uses clearnet).
/// </summary>
internal static class VpnTunPathProbe
{
    public static Task<int?> ProbeAsync(CancellationToken cancellationToken, int timeoutMs) =>
        Task.Run(() => ProbeOnBackground(cancellationToken, timeoutMs), cancellationToken);

    private static int? ProbeOnBackground(CancellationToken cancellationToken, int timeoutMs)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var context = Application.Context;
        if (context?.GetSystemService(Context.ConnectivityService) is not ConnectivityManager cm)
            return -1;

        Network? vpnNetwork = null;
        var networks = cm.GetAllNetworks();
        if (networks is not null)
        {
            foreach (var network in networks)
            {
                var caps = cm.GetNetworkCapabilities(network);
                if (caps?.HasTransport(TransportType.Vpn) == true)
                {
                    vpnNetwork = network;
                    break;
                }
            }
        }

        if (vpnNetwork is null)
            return -1;

        foreach (var url in LatencyService.PingUrls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ms = ProbeUrl(vpnNetwork, url, timeoutMs, headOnly: false);
            if (ms is >= 0)
            {
                var pushMs = ProbeUrl(vpnNetwork, "https://mtalk.google.com/", timeoutMs, headOnly: true);
                if (pushMs is >= 0)
                    return Math.Max(ms.Value, pushMs.Value);
                return ms;
            }
        }

        return -1;
    }

    private static int? ProbeUrl(Network network, string url, int timeoutMs, bool headOnly)
    {
        HttpURLConnection? conn = null;
        try
        {
            var sw = Stopwatch.StartNew();
            conn = (HttpURLConnection)network.OpenConnection(new URL(url));
            conn.ConnectTimeout = timeoutMs;
            conn.ReadTimeout = timeoutMs;
            conn.InstanceFollowRedirects = false;
            conn.RequestMethod = headOnly ? "HEAD" : "GET";
            var code = conn.ResponseCode;
            sw.Stop();
            if (code is >= 200 and < 400 or 204)
                return (int)sw.ElapsedMilliseconds;
        }
        catch
        {
            // try next URL
        }
        finally
        {
            try
            {
                conn?.Disconnect();
            }
            catch
            {
                // ignore
            }
        }

        return -1;
    }
}
