using System;

namespace v2rayF.Services;

/// <summary>Maps raw proxy-core stdout/stderr into actionable connect-status messages.</summary>
public static class CoreStartupErrorFormatter
{
    public static string Format(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return "Proxy core exited immediately after start.";

        if (output.Contains("decode config", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("invalid config", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("unknown field", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("unmarshal", StringComparison.OrdinalIgnoreCase))
        {
            return "Proxy core rejected the config. Update the app or check the server link.";
        }

        if (ContainsLegacyDnsWarning(output))
        {
            return "sing-box DNS config is outdated. Update the app to the latest release.";
        }

        if (output.Contains("FATAL", StringComparison.Ordinal) &&
            output.Contains("tun", StringComparison.OrdinalIgnoreCase))
        {
            return "Android VPN tunnel fd was lost. Disconnect, grant VPN permission again, then Connect.";
        }

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

        return StatusSanitizer.Scrub(ExtractActionableLine(output));
    }

    /// <summary>Prefer FATAL/ERROR lines over trailing URL fragments from sing-box stderr.</summary>
    public static string ExtractActionableLine(string output, int maxChars = 200)
    {
        if (string.IsNullOrWhiteSpace(output))
            return "";

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i];
            if (line.Contains("FATAL", StringComparison.Ordinal) ||
                line.Contains("ERROR", StringComparison.Ordinal))
                return TrimToMax(StatusSanitizer.Scrub(line), maxChars);
        }

        if (ContainsLegacyDnsWarning(output))
            return "Legacy DNS config warning (update app if Connect keeps failing).";

        var lastLine = lines.Length > 0 ? lines[^1] : output.Trim();
        return TrimToMax(StatusSanitizer.Scrub(lastLine), maxChars);
    }

    private static bool ContainsLegacyDnsWarning(string output) =>
        output.Contains("legacy DNS", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("migrate-to-new-dns-server-formats", StringComparison.OrdinalIgnoreCase);

    private static string TrimToMax(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
            return text;

        return text[^maxChars..].TrimStart();
    }
}
