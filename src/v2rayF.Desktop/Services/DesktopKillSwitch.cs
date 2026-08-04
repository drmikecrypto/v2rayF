using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace v2rayF.Services;

/// <summary>
/// Windows Firewall clearnet block while connected (allow Xray + loopback only).
/// Linux/macOS: rely on TUN <c>strict_route</c> / VpnService — iptables/pf path matching is too fragile.
/// </summary>
public sealed class DesktopKillSwitch : IKillSwitch
{
    private const string WindowsRuleOut = "v2rayF KillSwitch Block Out";
    private const string WindowsRuleAllow = "v2rayF KillSwitch Allow Core";
    private const string WindowsRuleLoopback = "v2rayF KillSwitch Allow Loopback";

    private int _armed;

    public bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public bool IsArmed => Volatile.Read(ref _armed) == 1;

    public string? LastError { get; private set; }

    public async Task EnableAsync(string coreExecutablePath, CancellationToken cancellationToken = default)
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
            await EnableWindowsAsync(coreExecutablePath, cancellationToken).ConfigureAwait(false);
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
    private static async Task EnableWindowsAsync(string corePath, CancellationToken cancellationToken)
    {
        await DisableWindowsAsync(cancellationToken).ConfigureAwait(false);

        await RunAsync("netsh",
            $"advfirewall firewall add rule name=\"{WindowsRuleAllow}\" dir=out action=allow program=\"{corePath}\" enable=yes",
            cancellationToken).ConfigureAwait(false);

        await RunAsync("netsh",
            $"advfirewall firewall add rule name=\"{WindowsRuleLoopback}\" dir=out action=allow remoteip=127.0.0.1,::1 enable=yes",
            cancellationToken).ConfigureAwait(false);

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
        {
            if (throwOnError)
                throw new InvalidOperationException($"Failed to start {file}.");
            return;
        }

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (throwOnError && process.ExitCode != 0)
        {
            var err = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(err)
                    ? $"{file} exited with code {process.ExitCode}"
                    : err.Trim());
        }
    }
}
