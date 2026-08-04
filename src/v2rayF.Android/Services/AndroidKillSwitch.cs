using System.Threading;
using System.Threading.Tasks;
using v2rayF.Services;

namespace v2rayF.Android.Services;

/// <summary>
/// On Android, VpnService itself is the kill switch when held until teardown.
/// </summary>
public sealed class AndroidKillSwitch : IKillSwitch
{
    public bool IsSupported => true;

    /// <summary>Armed while the Android VPN interface is expected to be held.</summary>
    public bool IsArmed { get; private set; }

    public string? LastError => null;

    public Task EnableAsync(string coreExecutablePath, CancellationToken cancellationToken = default)
    {
        IsArmed = true;
        return Task.CompletedTask;
    }

    public Task DisableAsync(CancellationToken cancellationToken = default)
    {
        IsArmed = false;
        return Task.CompletedTask;
    }
}
