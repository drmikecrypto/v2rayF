using System.Text.RegularExpressions;

namespace v2rayF.Services;

public static partial class StatusSanitizer
{
    public static string Scrub(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return "";

        var scrubbed = UuidPattern().Replace(message, "[id]");
        scrubbed = ShareLinkPattern().Replace(scrubbed, "$1://[redacted]");
        scrubbed = LogcatDumpPattern().Replace(scrubbed, "[logcat]");
        return scrubbed;
    }

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex UuidPattern();

    [GeneratedRegex(
        @"(vless|vmess|trojan|ss|socks|hy2|hysteria2|tuic|anytls|wireguard|wg)://[^\s]+",
        RegexOptions.IgnoreCase)]
    private static partial Regex ShareLinkPattern();

    /// <summary>Collapse pasted logcat / adb dump blobs that leak into StatusText.</summary>
    [GeneratedRegex(@"(?is)--------- beginning of \w+.*")]
    private static partial Regex LogcatDumpPattern();
}
