using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Net;
using Java.Net;
using v2rayF.Services;

namespace v2rayF.Android.Services;

/// <summary>
/// Health probe through the active VPN Network (v2rayF is VPN-disallowed — default HttpClient uses clearnet).
/// Returns <see cref="LatencyService.TunVpnMissingMs"/> when no VPN Network exists (hard fail).
/// HTTPS miss is advisory (-1): gen204/FCM flap must not block Connect (2.6.3.1 false-negative).
/// </summary>
internal static class VpnTunPathProbe
{
    private static readonly string[] PushProbeUrls =
    [
        "https://mtalk.google.com/",
        "https://fcm.googleapis.com/"
    ];

    public static Task<int?> ProbeAsync(CancellationToken cancellationToken, int timeoutMs) =>
        Task.Run(() => ProbeOnBackground(cancellationToken, timeoutMs), cancellationToken);

    private static int? ProbeOnBackground(CancellationToken cancellationToken, int timeoutMs)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Captive / Chrome validation — call before HTTPS so the VPN Network is usable.
        V2rayVpnService.ReportVpnReady();

        var context = Application.Context;
        if (context?.GetSystemService(Context.ConnectivityService) is not ConnectivityManager cm)
            return LatencyService.TunVpnMissingMs;

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
            return LatencyService.TunVpnMissingMs;

        var deadline = Stopwatch.StartNew();
        foreach (var url in LatencyService.PingUrls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = RemainingMs(timeoutMs, deadline);
            if (remaining <= 0)
                break;
            var ms = ProbeUrl(vpnNetwork, url, remaining, headOnly: false);
            if (ms is >= 0)
                return ms;
        }

        foreach (var url in PushProbeUrls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = RemainingMs(timeoutMs, deadline);
            if (remaining <= 0)
                break;
            var ms = ProbeUrl(vpnNetwork, url, remaining, headOnly: true);
            if (ms is >= 0)
                return ms;
        }

        // VPN present — probe URLs failed (censorship / cold dial / DNS). Advisory only.
        return -1;
    }

    private static int RemainingMs(int timeoutMs, Stopwatch deadline) =>
        Math.Max(0, timeoutMs - (int)deadline.ElapsedMilliseconds);

    private static int? ProbeUrl(Network network, string url, int timeoutMs, bool headOnly)
    {
        if (timeoutMs <= 0)
            return -1;

        HttpURLConnection? conn = null;
        try
        {
            var sw = Stopwatch.StartNew();
            conn = (HttpURLConnection)network.OpenConnection(new URL(url));
            conn.ConnectTimeout = timeoutMs;
            conn.ReadTimeout = timeoutMs;
            conn.InstanceFollowRedirects = false;
            conn.RequestMethod = headOnly ? "HEAD" : "GET";
            var code = (int)conn.ResponseCode;
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
