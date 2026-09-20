using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using v2rayF.Models;

namespace v2rayF.Services;

/// <summary>
/// Windows inbound firewall allow for Secure Share SOCKS/HTTP while Connected.
/// Removed on disconnect / share off. No-op on non-Windows.
/// </summary>
public static class ShareFirewall
{
    private const string RuleTcp = "v2rayF SecureShare Inbound TCP";
    private const string RuleUdp = "v2rayF SecureShare Inbound UDP";

    private static int _armed;

    public static bool IsArmed => Volatile.Read(ref _armed) == 1;

    public static string? LastError { get; private set; }

    public static async Task ApplyAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        LastError = null;

        if (!settings.SecureShareEnabled)
        {
            await ClearAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        var socks = SecureShareEndpoints.ResolveSocksPort(settings);
        var http = SecureShareEndpoints.ResolveHttpPort(settings);

        try
        {
            await ClearWindowsAsync(cancellationToken).ConfigureAwait(false);
            await RunAsync(
                "netsh",
                $"advfirewall firewall add rule name=\"{RuleTcp}\" dir=in action=allow protocol=TCP localport={socks},{http} profile=private,domain enable=yes",
                cancellationToken).ConfigureAwait(false);
            await RunAsync(
                "netsh",
                $"advfirewall firewall add rule name=\"{RuleUdp}\" dir=in action=allow protocol=UDP localport={socks} profile=private,domain enable=yes",
                cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _armed, 1);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            await ClearAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public static async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                await ClearWindowsAsync(cancellationToken).ConfigureAwait(false);
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
    private static async Task ClearWindowsAsync(CancellationToken cancellationToken)
    {
        await RunSoftAsync("netsh", $"advfirewall firewall delete rule name=\"{RuleTcp}\"", cancellationToken)
            .ConfigureAwait(false);
        await RunSoftAsync("netsh", $"advfirewall firewall delete rule name=\"{RuleUdp}\"", cancellationToken)
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
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        proc.Start();
        var err = await proc.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (throwOnError && proc.ExitCode != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(err)
                    ? $"netsh failed ({proc.ExitCode}) for Secure Share firewall."
                    : err.Trim());
        }
    }
}
