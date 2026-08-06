using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace v2rayF.Services;

/// <summary>
/// Windows Firewall clearnet block while connected (allow Xray + loopback + optional TUN).
/// Linux/macOS: rely on TUN <c>strict_route</c> / VpnService — iptables/pf path matching is too fragile.
/// </summary>
public sealed class DesktopKillSwitch : IKillSwitch
{
    private const string WindowsRuleOut = "v2rayF KillSwitch Block Out";
    private const string WindowsRuleAllow = "v2rayF KillSwitch Allow Core";
    private const string WindowsRuleLoopback = "v2rayF KillSwitch Allow Loopback";
    private const string WindowsRuleTun = "v2rayF KillSwitch Allow TUN";

    private int _armed;

    public bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public bool IsArmed => Volatile.Read(ref _armed) == 1;

    public string? LastError { get; private set; }

    public async Task EnableAsync(
        string coreExecutablePath,
        bool allowTunInterface = false,
        CancellationToken cancellationToken = default)
    {
        LastError = null;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            LastError = "Desktop kill switch firewall is Windows-only; use TUN mode on macOS/Linux.";
            return;
        }

        if (string.IsNullOrWhiteSpace(coreExecutablePath) || !File.Exists(coreExecutablePath))
        {
            LastError = "Kill switch needs a valid Xray path.";
            return;
        }

        try
        {
            await EnableWindowsAsync(coreExecutablePath, allowTunInterface, cancellationToken)
                .ConfigureAwait(false);
            Volatile.Write(ref _armed, 1);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            await DisableAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task DisableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                await DisableWindowsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
        finally
        {
            Volatile.Write(ref _armed, 0);
        }
    }

    [SupportedOSPlatform("windows")]
    private static async Task EnableWindowsAsync(
        string corePath,
        bool allowTunInterface,
        CancellationToken cancellationToken)
    {
        await DisableWindowsAsync(cancellationToken).ConfigureAwait(false);

        await RunAsync("netsh",
            $"advfirewall firewall add rule name=\"{WindowsRuleAllow}\" dir=out action=allow program=\"{corePath}\" enable=yes",
            cancellationToken).ConfigureAwait(false);

        await RunAsync("netsh",
            $"advfirewall firewall add rule name=\"{WindowsRuleLoopback}\" dir=out action=allow remoteip=127.0.0.1,::1 enable=yes",
            cancellationToken).ConfigureAwait(false);

        if (allowTunInterface)
        {
            await WaitForTunInterfaceAsync(cancellationToken).ConfigureAwait(false);
            await AddTunAllowRuleAsync(cancellationToken).ConfigureAwait(false);
        }

        await RunAsync("netsh",
            $"advfirewall firewall add rule name=\"{WindowsRuleOut}\" dir=out action=block enable=yes",
            cancellationToken).ConfigureAwait(false);
    }

    [SupportedOSPlatform("windows")]
    private static async Task DisableWindowsAsync(CancellationToken cancellationToken)
    {
        await RunSoftAsync("netsh", $"advfirewall firewall delete rule name=\"{WindowsRuleOut}\"", cancellationToken)
            .ConfigureAwait(false);
        await RunSoftAsync("netsh", $"advfirewall firewall delete rule name=\"{WindowsRuleAllow}\"", cancellationToken)
            .ConfigureAwait(false);
        await RunSoftAsync("netsh", $"advfirewall firewall delete rule name=\"{WindowsRuleLoopback}\"", cancellationToken)
            .ConfigureAwait(false);
        await RemoveTunAllowRuleAsync(cancellationToken).ConfigureAwait(false);
    }

    [SupportedOSPlatform("windows")]
    private static async Task WaitForTunInterfaceAsync(CancellationToken cancellationToken)
    {
        var iface = TunConstants.InterfaceName.Replace("'", "''", StringComparison.Ordinal);
        var probe =
            $"if (Get-NetAdapter -Name '{iface}' -ErrorAction SilentlyContinue) {{ exit 0 }} else {{ exit 1 }}";

        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (exit, _) = await RunCaptureAsync("powershell", $"-NoProfile -Command \"{probe}\"", cancellationToken)
                .ConfigureAwait(false);
            if (exit == 0)
                return;

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            $"TUN interface '{TunConstants.InterfaceName}' not found; kill switch not armed to avoid blackhole.");
    }

    [SupportedOSPlatform("windows")]
    private static async Task AddTunAllowRuleAsync(CancellationToken cancellationToken)
    {
        var iface = TunConstants.InterfaceName.Replace("'", "''", StringComparison.Ordinal);
        var name = WindowsRuleTun.Replace("'", "''", StringComparison.Ordinal);
        var script =
            $"Remove-NetFirewallRule -DisplayName '{name}' -ErrorAction SilentlyContinue; " +
            $"New-NetFirewallRule -DisplayName '{name}' -Direction Outbound -Action Allow " +
            $"-InterfaceAlias '{iface}' -Profile Any -ErrorAction Stop | Out-Null";

        var (exit, err) = await RunCaptureAsync("powershell", $"-NoProfile -Command \"{script}\"", cancellationToken)
            .ConfigureAwait(false);
        if (exit != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(err)
                    ? "Failed to add TUN kill-switch allow rule."
                    : err.Trim());
        }
    }

    [SupportedOSPlatform("windows")]
    private static async Task RemoveTunAllowRuleAsync(CancellationToken cancellationToken)
    {
        var name = WindowsRuleTun.Replace("'", "''", StringComparison.Ordinal);
        await RunSoftAsync("powershell",
                $"-NoProfile -Command \"Remove-NetFirewallRule -DisplayName '{name}' -ErrorAction SilentlyContinue\"",
                cancellationToken)
            .ConfigureAwait(false);
        // Legacy cleanup if an older netsh-named rule exists under the same display string.
        await RunSoftAsync("netsh", $"advfirewall firewall delete rule name=\"{WindowsRuleTun}\"", cancellationToken)
            .ConfigureAwait(false);
    }

    private static Task RunAsync(string file, string args, CancellationToken cancellationToken) =>
        RunInternalAsync(file, args, throwOnError: true, cancellationToken);

    private static Task RunSoftAsync(string file, string args, CancellationToken cancellationToken) =>
        RunInternalAsync(file, args, throwOnError: false, cancellationToken);

    private static async Task RunInternalAsync(
        string file,
        string args,
        bool throwOnError,
        CancellationToken cancellationToken)
    {
        var (exit, err) = await RunCaptureAsync(file, args, cancellationToken).ConfigureAwait(false);
        if (throwOnError && exit != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(err)
                    ? $"{file} exited with code {exit}"
                    : err.Trim());
        }
    }

    private static async Task<(int ExitCode, string Error)> RunCaptureAsync(
        string file,
        string args,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            }
        };

        if (!process.Start())
            return (-1, $"Failed to start {file}.");

        var errTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var outTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var err = await errTask.ConfigureAwait(false);
        _ = await outTask.ConfigureAwait(false);
        return (process.ExitCode, err);
    }
}
