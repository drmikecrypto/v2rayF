using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Net;
using v2rayF.Models;
using v2rayF.Services;

namespace v2rayF.Android.Services;

public sealed class AndroidPlatformIntegration : IPlatformIntegration
{
    private readonly AndroidAppNetworkCatalog _appNetwork = new();

    public bool IsMobile => true;

    public bool CanUseTunMode => true;

    public string TunRequirementMessage => "Grant VPN permission when prompted.";

    public string? LastProxyMethod { get; private set; }

    public string? LastEstablishError { get; private set; }

    public string? LastHttpProxyWarning { get; private set; }

    internal static void ReportEstablishError(string? message)
    {
        if (AppServices.Platform is AndroidPlatformIntegration platform)
            platform.LastEstablishError = message;
    }

    internal static void ReportHttpProxyWarning(string? message)
    {
        if (AppServices.Platform is AndroidPlatformIntegration platform)
            platform.LastHttpProxyWarning = message;
    }

    public Task<int?> EstablishVpnAsync(
        IReadOnlyList<string>? bypassPackages = null,
        bool blockIpv6 = true,
        CancellationToken cancellationToken = default,
        bool forceRebind = false,
        bool chromiumHttpProxyAssist = false) =>
        AndroidUiThread.InvokeAsync(() =>
            EstablishVpnOnUiThreadAsync(
                bypassPackages, blockIpv6, cancellationToken, forceRebind, chromiumHttpProxyAssist));

    private async Task<int?> EstablishVpnOnUiThreadAsync(
        IReadOnlyList<string>? bypassPackages,
        bool blockIpv6,
        CancellationToken cancellationToken,
        bool forceRebind,
        bool chromiumHttpProxyAssist)
    {
        LastEstablishError = null;
        LastHttpProxyWarning = null;
        var activity = MainActivity.Instance;
        if (activity is null)
            throw new InvalidOperationException("Activity not ready.");

        var prepare = VpnService.Prepare(activity);
        if (prepare is not null)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            MainActivity.VpnPermissionTcs = tcs;
            activity.StartActivityForResult(prepare, MainActivity.VpnRequestCode);
            if (!await tcs.Task.ConfigureAwait(false))
                return null;
        }

        var context = activity.ApplicationContext ?? activity;
        return await V2rayVpnService.EstablishAsync(
                context,
                bypassPackages,
                blockIpv6,
                cancellationToken,
                forceRebind,
                chromiumHttpProxyAssist)
            .ConfigureAwait(false);
    }

    public Task NotifyVpnReadyAsync(CancellationToken cancellationToken = default) =>
        AndroidUiThread.InvokeAsync(() =>
        {
            V2rayVpnService.ReportVpnReady();
            return Task.CompletedTask;
        });

    public Task<int?> ProbeTunAppPathAsync(
        CancellationToken cancellationToken = default,
        int timeoutMs = LatencyService.TunAppPathProbeMs) =>
        VpnTunPathProbe.ProbeAsync(cancellationToken, timeoutMs);

    public Task PromptBatteryOptimizationIfNeededAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default) =>
        AndroidUiThread.InvokeAsync(() =>
        {
            BatteryOptimizationHelper.TryPromptIfNeeded(MainActivity.Instance, settings);
            return Task.CompletedTask;
        });

    public bool NeedsVpnReestablish(
        IReadOnlyList<string>? bypassPackages,
        bool blockIpv6,
        bool chromiumHttpProxyAssist = false) =>
        V2rayVpnService.NeedsReestablish(bypassPackages, blockIpv6, chromiumHttpProxyAssist);

    public Task EnableProxyAsync(CancellationToken cancellationToken = default)
    {
        LastProxyMethod = "Android VPN";
        return Task.CompletedTask;
    }

    public Task DisableProxyAsync(CancellationToken cancellationToken = default) =>
        AndroidUiThread.InvokeAsync(async () =>
        {
            var context = Application.Context!;
            V2rayVpnService.Disconnect(context);
            LastProxyMethod = null;
            await Task.CompletedTask;
        });

    public string? GetPrivateDnsConflictWarning()
    {
        try
        {
            var context = Application.Context;
            if (context?.ContentResolver is null)
                return null;

            // Settings.Global.PRIVATE_DNS_MODE: off | opportunistic | hostname
            // Use global:: — this assembly's root namespace is v2rayF.Android.
            var mode = global::Android.Provider.Settings.Global.GetString(
                context.ContentResolver,
                "private_dns_mode");
            if (string.IsNullOrWhiteSpace(mode) ||
                mode.Equals("off", StringComparison.OrdinalIgnoreCase))
                return null;

            return "Private DNS is On — set Settings → Network → Private DNS to Off or VPN DNS will break.";
        }
        catch
        {
            return null;
        }
    }

    public string? GetLanIPv4Address()
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                    continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback)
                    continue;

                foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;
                    var ip = addr.Address.ToString();
                    if (ip.StartsWith("127.", StringComparison.Ordinal))
                        continue;
                    return ip;
                }
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    public Task<IReadOnlyList<InstalledAppInfo>> GetNetworkAppsAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default) =>
        _appNetwork.GetNetworkAppsAsync(forceRefresh, cancellationToken);

    public Task<IReadOnlyDictionary<string, AppTrafficSnapshot>> GetAppTrafficAsync(
        IReadOnlyList<string> ids,
        CancellationToken cancellationToken = default) =>
        _appNetwork.GetAppTrafficAsync(ids, cancellationToken);

    public Task<string?> TryGetClipboardTextAsync(CancellationToken cancellationToken = default) =>
        AndroidUiThread.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var context = Application.Context;
                if (context is null)
                    return Task.FromResult<string?>(null);

                var cm = context.GetSystemService(Context.ClipboardService) as ClipboardManager;
                if (cm?.HasPrimaryClip != true)
                    return Task.FromResult<string?>(null);

                var clip = cm.PrimaryClip;
                if (clip is null || clip.ItemCount < 1)
                    return Task.FromResult<string?>(null);

                var item = clip.GetItemAt(0);
                var text = item?.CoerceToText(context)?.ToString();
                return Task.FromResult(string.IsNullOrWhiteSpace(text) ? null : text);
            }
            catch
            {
                return Task.FromResult<string?>(null);
            }
        });
}
