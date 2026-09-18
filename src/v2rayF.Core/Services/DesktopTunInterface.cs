using System;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace v2rayF.Services;

/// <summary>
/// WinTun / sing-box desktop adapter presence (same name as <see cref="TunConstants.InterfaceName"/>).
/// Missing adapter while TUN mode is on is the desktop analogue of Android VpnService Network missing.
/// </summary>
public static class DesktopTunInterface
{
    /// <summary>True when a NIC matching the v2rayF TUN name exists and is not down.</summary>
    public static bool IsPresent(string name = TunConstants.InterfaceName)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!NameMatches(nic, name))
                    continue;
                return nic.OperationalStatus is OperationalStatus.Up
                    or OperationalStatus.Unknown
                    or OperationalStatus.Dormant;
            }
        }
        catch
        {
            // Ignore — treat as missing so Connect fails closed rather than false-green.
        }

        return false;
    }

    /// <summary>Poll until the adapter appears or <paramref name="timeoutMs"/> elapses.</summary>
    public static async Task<bool> WaitUntilPresentAsync(
        int timeoutMs,
        CancellationToken cancellationToken = default,
        string name = TunConstants.InterfaceName)
    {
        if (IsPresent(name))
            return true;

        var budget = Math.Max(0, timeoutMs);
        if (budget == 0)
            return false;

        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < budget)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            if (IsPresent(name))
                return true;
        }

        return IsPresent(name);
    }

    private static bool NameMatches(NetworkInterface nic, string name) =>
        string.Equals(nic.Name, name, StringComparison.OrdinalIgnoreCase) ||
        nic.Description.Contains(name, StringComparison.OrdinalIgnoreCase);
}
