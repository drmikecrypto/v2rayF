using System;

namespace v2rayF.Services;

/// <summary>Maps raw Xray stdout/stderr into actionable connect-status messages.</summary>
public static class CoreStartupErrorFormatter
{
    public static string Format(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return "Xray core exited immediately after start.";

        if (output.Contains("10808", StringComparison.Ordinal) ||
            output.Contains("10809", StringComparison.Ordinal) ||
            output.Contains("bind:", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("Only one usage of each socket address", StringComparison.OrdinalIgnoreCase))
        {
            return "Local proxy ports 10808/10809 are already in use. Close v2rayN (or another proxy client) and try again.";
        }

        if (output.Contains("wintun.dll", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("Error loading wintun", StringComparison.OrdinalIgnoreCase))
        {
            return "TUN requires wintun.dll next to xray.exe in the cores folder. Reinstall the Windows package or copy wintun.dll from the Xray release zip.";
        }

        if (output.Contains("Access is denied", StringComparison.OrdinalIgnoreCase))
        {
            return "TUN mode was denied access. Run v2rayF as Administrator, or turn off TUN and use system proxy.";
        }

        if (output.Contains("bad file", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("SetNonblock", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("read Android Tun Fd", StringComparison.OrdinalIgnoreCase) ||
            (output.Contains("invalid argument", StringComparison.OrdinalIgnoreCase) &&
             output.Contains("tun", StringComparison.OrdinalIgnoreCase)))
        {
            return "Android VPN tunnel fd was lost. Disconnect, grant VPN permission again, then Connect.";
        }

        var lastLine = output;
        var newline = output.LastIndexOf('\n');
        if (newline >= 0 && newline < output.Length - 1)
            lastLine = output[(newline + 1)..].Trim();

        return StatusSanitizer.Scrub(lastLine);
    }
}
