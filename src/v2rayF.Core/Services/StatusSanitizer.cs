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
        return scrubbed;
    }

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex UuidPattern();

    [GeneratedRegex(@"(vless|vmess|trojan|ss|socks)://[^\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex ShareLinkPattern();
}
