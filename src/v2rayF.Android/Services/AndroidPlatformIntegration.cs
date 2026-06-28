using System;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Net;
using Android.OS;
using v2rayF.Services;

namespace v2rayF.Android.Services;

public sealed class AndroidPlatformIntegration : IPlatformIntegration
{
    public bool IsMobile => true;

    public bool CanUseTunMode => true;

    public string TunRequirementMessage => "Grant VPN permission when prompted.";

    public string? LastProxyMethod { get; private set; }

    public async Task<int?> EstablishVpnAsync(CancellationToken cancellationToken = default)
    {
        var activity = MainActivity.Instance;
        if (activity is null)
            throw new InvalidOperationException("Activity not ready.");

        var prepare = VpnService.Prepare(activity);
        if (prepare is not null)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            MainActivity.VpnPermissionTcs = tcs;
            activity.StartActivityForResult(prepare, MainActivity.VpnRequestCode);
            var granted = await tcs.Task.ConfigureAwait(false);
            if (!granted)
                return null;
        }

        var context = activity.ApplicationContext ?? activity;
        return await V2rayVpnService.EstablishAsync(context, cancellationToken).ConfigureAwait(false);
    }

    public Task EnableProxyAsync(CancellationToken cancellationToken = default)
    {
        LastProxyMethod = "Android VPN";
        return Task.CompletedTask;
    }

    public Task DisableProxyAsync(CancellationToken cancellationToken = default)
    {
        var context = Application.Context!;
        V2rayVpnService.Disconnect(context);
        context.StopService(new Intent(context, typeof(V2rayForegroundService)));
        LastProxyMethod = null;
        return Task.CompletedTask;
    }
}
