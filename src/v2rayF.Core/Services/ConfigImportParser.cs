using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using v2rayF.Models;

namespace v2rayF.Services;

/// <summary>
/// Imports servers from share links, base64 subscriptions, Xray JSON, and opaque app dumps
/// (.txt / .v2box / .npv / etc.) by scanning for known URI schemes.
/// </summary>
public static partial class ConfigImportParser
{
    private static readonly Regex ShareLinkRegex = ShareLinkPattern();

    /// <summary>Set when paste/subscription contained unsupported links (hy2/TUIC/wg/plugins/…).</summary>
    public static string? LastSkippedSingBoxHint { get; private set; }

    public static IReadOnlyList<ProxyServer> Parse(string input) =>
        ParseDetailed(input).Servers;

    public static ImportResult ParseDetailed(string input)
    {
        LastSkippedSingBoxHint = null;
        if (string.IsNullOrWhiteSpace(input))
            return ImportResult.Empty;

        var trimmed = input.Trim();

        // Subscription URL alone is handled by the caller (fetch), not here.
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https" &&
            LooksLikeBareSubscriptionUrl(trimmed))
        {
            return ImportResult.Empty;
        }

        var mergedSkips = new List<string>();
        var skipCount = 0;

        void MergeSkips(ImportResult part)
        {
            skipCount += part.SkippedCount;
            foreach (var s in part.SkipReasons)
            {
                if (!mergedSkips.Contains(s))
                    mergedSkips.Add(s);
            }
        }

        // Clash Meta YAML (proxies:)
        if (ClashMetaImportParser.LooksLikeClash(trimmed))
        {
            var clash = ClashMetaImportParser.Parse(trimmed);
            MergeSkips(clash);
            if (clash.Servers.Count > 0)
            {
                var result = ImportResult.FromServers(Deduplicate(clash.Servers), mergedSkips, skipCount);
                LastSkippedSingBoxHint = result.SummaryHint;
                return result;
            }
        }

        // sing-box JSON with outbounds
        if (SingBoxJsonImportParser.LooksLikeSingBox(trimmed))
        {
            var sb = SingBoxJsonImportParser.Parse(trimmed);
            MergeSkips(sb);
            if (sb.Servers.Count > 0 || sb.SkippedCount > 0)
            {
                // Prefer sing-box mapping even if only skips (so UI shows honesty).
                if (sb.Servers.Count > 0)
                {
                    var result = ImportResult.FromServers(Deduplicate(sb.Servers), mergedSkips, skipCount);
                    LastSkippedSingBoxHint = result.SummaryHint;
                    return result;
                }
            }
        }

        var fromJson = TryParseXrayJson(trimmed);
        if (fromJson.Count > 0)
        {
            var hint = DetectUnsupportedHint(trimmed);
            if (hint is not null)
                mergedSkips.Add(hint);
            var result = ImportResult.FromServers(Deduplicate(fromJson), mergedSkips, skipCount + (hint is null ? 0 : 1));
            LastSkippedSingBoxHint = result.SummaryHint;
            return result;
        }

        // After Xray JSON miss, try sing-box again if it looked empty due to only skips
        if (SingBoxJsonImportParser.LooksLikeSingBox(trimmed))
        {
            var sbOnly = SingBoxJsonImportParser.Parse(trimmed);
            if (sbOnly.SkippedCount > 0 && sbOnly.Servers.Count == 0)
            {
                LastSkippedSingBoxHint = sbOnly.SummaryHint;
                return sbOnly;
            }
        }

        var fromLinks = ShareLinkParser.ParseBulkDetailed(trimmed);
        MergeSkips(fromLinks);
        if (fromLinks.Servers.Count > 0)
        {
            var result = ImportResult.FromServers(Deduplicate(fromLinks.Servers), mergedSkips, skipCount);
            LastSkippedSingBoxHint = result.SummaryHint;
            return result;
        }

        var scanned = ScanShareLinksDetailed(trimmed);
        MergeSkips(scanned);
        if (scanned.Servers.Count > 0)
        {
            var result = ImportResult.FromServers(Deduplicate(scanned.Servers), mergedSkips, skipCount);
            LastSkippedSingBoxHint = result.SummaryHint;
            return result;
        }

        // Nested base64 / JSON string fields common in .v2box / .npv dumps.
        foreach (var candidate in ExtractQuotedOrBase64Blobs(trimmed))
        {
            var nested = ParseDetailed(candidate);
            if (nested.Servers.Count > 0)
            {
                LastSkippedSingBoxHint = nested.SummaryHint;
                return nested;
            }
        }

        if (mergedSkips.Count > 0)
        {
            var empty = ImportResult.FromServers([], mergedSkips, skipCount);
            LastSkippedSingBoxHint = empty.SummaryHint;
            return empty;
        }

        var fallbackHint = DetectUnsupportedHint(trimmed);
        if (fallbackHint is not null)
        {
            LastSkippedSingBoxHint = fallbackHint;
            return ImportResult.FromServers([], [fallbackHint], 1);
        }

        return ImportResult.Empty;
    }

    public static IReadOnlyList<ProxyServer> ParseBytes(byte[] bytes, string? fileNameHint = null) =>
        ParseBytesDetailed(bytes, fileNameHint).Servers;

    public static ImportResult ParseBytesDetailed(byte[] bytes, string? fileNameHint = null)
    {
        if (bytes.Length == 0)
            return ImportResult.Empty;

        // UTF-8 / UTF-16 text first.
        string text;
        try
        {
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                text = Encoding.Unicode.GetString(bytes);
            else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                text = Encoding.BigEndianUnicode.GetString(bytes);
            else
                text = Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            text = Encoding.Latin1.GetString(bytes);
        }

        var parsed = ParseDetailed(text);
        if (parsed.Servers.Count > 0 || parsed.SkippedCount > 0)
            return parsed;

        // Binary / encrypted vault-like: still scan for ASCII share links.
        var ascii = Encoding.ASCII.GetString(bytes);
        return ScanShareLinksDetailed(ascii);
    }

    public static IReadOnlyList<ProxyServer> TryParseXrayJson(string text)
    {
        text = text.Trim();
        if (!(text.StartsWith('{') || text.StartsWith('[')))
            return Array.Empty<ProxyServer>();

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var results = new List<ProxyServer>();

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                    CollectFromJsonElement(item, results);
            }
            else
            {
                CollectFromJsonElement(root, results);
            }

            return results;
        }
        catch (JsonException)
        {
            return Array.Empty<ProxyServer>();
        }
    }

    private static void CollectFromJsonElement(JsonElement root, List<ProxyServer> results)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return;

        if (root.TryGetProperty("outbounds", out var outbounds) && outbounds.ValueKind == JsonValueKind.Array)
        {
            foreach (var outbound in outbounds.EnumerateArray())
            {
                var server = TryMapOutbound(outbound);
                if (server is not null)
                    results.Add(server);
            }
        }

        // Single outbound object.
        if (root.TryGetProperty("protocol", out _) && root.TryGetProperty("settings", out _))
        {
            var single = TryMapOutbound(root);
            if (single is not null)
                results.Add(single);
        }

        // Nested common keys in app exports.
        foreach (var key in new[] { "config", "configs", "servers", "proxies", "data", "nodes" })
        {
            if (!root.TryGetProperty(key, out var nested))
                continue;

            if (nested.ValueKind == JsonValueKind.String)
            {
                results.AddRange(Parse(nested.GetString() ?? ""));
            }
            else if (nested.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in nested.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                        results.AddRange(Parse(item.GetString() ?? ""));
                    else if (item.ValueKind == JsonValueKind.Object)
                        CollectFromJsonElement(item, results);
                }
            }
            else if (nested.ValueKind == JsonValueKind.Object)
            {
                CollectFromJsonElement(nested, results);
            }
        }

        // Share-link fields.
        foreach (var key in new[] { "uri", "url", "link", "share", "raw", "vmess", "vless" })
        {
            if (root.TryGetProperty(key, out var linkEl) && linkEl.ValueKind == JsonValueKind.String)
                results.AddRange(Parse(linkEl.GetString() ?? ""));
        }
    }

    private static ProxyServer? TryMapOutbound(JsonElement outbound)
    {
        if (outbound.ValueKind != JsonValueKind.Object)
            return null;

        var protocol = outbound.TryGetProperty("protocol", out var p) ? p.GetString()?.ToLowerInvariant() : null;
        if (string.IsNullOrWhiteSpace(protocol) ||
            protocol is "freedom" or "blackhole" or "dns" or "loopback" or "http" or "dokodemo-door")
            return null;

        var tag = outbound.TryGetProperty("tag", out var t) ? t.GetString() : null;
        if (tag is "direct" or "block" or "api" or "dns-out" or "fragment")
            return null;

        if (!outbound.TryGetProperty("settings", out var settings))
            return null;

        ProxyServer? server = protocol switch
        {
            "vless" => MapVlessOutbound(settings, tag),
            "vmess" => MapVmessOutbound(settings, tag),
            "trojan" => MapTrojanOutbound(settings, tag),
            "shadowsocks" => MapShadowsocksOutbound(settings, tag),
            "socks" or "socks5" => MapSocksOutbound(settings, tag),
            _ => null
        };

        if (server is null)
            return null;

        if (outbound.TryGetProperty("streamSettings", out var stream) && stream.ValueKind == JsonValueKind.Object)
            ApplyStreamSettings(server, stream);

        server.RawLink = server.RawLink.Length > 0 ? server.RawLink : $"json://{server.Protocol}/{server.Address}:{server.Port}";
        return server;
    }

    private static ProxyServer? MapVlessOutbound(JsonElement settings, string? tag)
    {
        if (!TryGetVnext(settings, out var address, out var port, out var user))
            return null;

        return new ProxyServer
        {
            Protocol = ProxyProtocol.VLESS,
            Name = string.IsNullOrWhiteSpace(tag) ? "VLESS" : tag!,
            Address = address,
            Port = port,
            UserId = GetJsonString(user, "id") ?? "",
            Encryption = GetJsonString(user, "encryption") ?? "none",
            Flow = GetJsonString(user, "flow") ?? ""
        };
    }

    private static ProxyServer? MapVmessOutbound(JsonElement settings, string? tag)
    {
        if (!TryGetVnext(settings, out var address, out var port, out var user))
            return null;

        return new ProxyServer
        {
            Protocol = ProxyProtocol.VMess,
            Name = string.IsNullOrWhiteSpace(tag) ? "VMess" : tag!,
            Address = address,
            Port = port,
            UserId = GetJsonString(user, "id") ?? "",
            AlterId = GetJsonInt(user, "alterId"),
            Cipher = GetJsonString(user, "security") ?? "auto"
        };
    }

    private static ProxyServer? MapTrojanOutbound(JsonElement settings, string? tag)
    {
        if (!settings.TryGetProperty("servers", out var servers) || servers.GetArrayLength() == 0)
            return null;

        var first = servers[0];
        return new ProxyServer
        {
            Protocol = ProxyProtocol.Trojan,
            Name = string.IsNullOrWhiteSpace(tag) ? "Trojan" : tag!,
            Address = GetJsonString(first, "address") ?? "",
            Port = GetJsonInt(first, "port"),
            Password = GetJsonString(first, "password") ?? ""
        };
    }

    private static ProxyServer? MapShadowsocksOutbound(JsonElement settings, string? tag)
    {
        if (!settings.TryGetProperty("servers", out var servers) || servers.GetArrayLength() == 0)
            return null;

        var first = servers[0];
        return new ProxyServer
        {
            Protocol = ProxyProtocol.Shadowsocks,
            Name = string.IsNullOrWhiteSpace(tag) ? "Shadowsocks" : tag!,
            Address = GetJsonString(first, "address") ?? "",
            Port = GetJsonInt(first, "port"),
            Cipher = GetJsonString(first, "method") ?? "",
            Password = GetJsonString(first, "password") ?? ""
        };
    }

    private static ProxyServer? MapSocksOutbound(JsonElement settings, string? tag)
    {
        if (!settings.TryGetProperty("servers", out var servers) || servers.GetArrayLength() == 0)
            return null;

        var first = servers[0];
        var user = "";
        var pass = "";
        if (first.TryGetProperty("users", out var users) && users.ValueKind == JsonValueKind.Array && users.GetArrayLength() > 0)
        {
            user = GetJsonString(users[0], "user") ?? "";
            pass = GetJsonString(users[0], "pass") ?? "";
        }

        return new ProxyServer
        {
            Protocol = ProxyProtocol.Socks,
            Name = string.IsNullOrWhiteSpace(tag) ? "SOCKS" : tag!,
            Address = GetJsonString(first, "address") ?? "",
            Port = GetJsonInt(first, "port"),
            UserId = user,
            Password = pass
        };
    }

    private static bool TryGetVnext(JsonElement settings, out string address, out int port, out JsonElement user)
    {
        address = "";
        port = 0;
        user = default;
        if (!settings.TryGetProperty("vnext", out var vnext) || vnext.GetArrayLength() == 0)
            return false;

        var first = vnext[0];
        address = GetJsonString(first, "address") ?? "";
        port = GetJsonInt(first, "port");
        if (!first.TryGetProperty("users", out var users) || users.GetArrayLength() == 0)
            return false;

        user = users[0];
        return !string.IsNullOrWhiteSpace(address) && port > 0;
    }

    private static void ApplyStreamSettings(ProxyServer server, JsonElement stream)
    {
        server.Network = ShareLinkParser.NormalizeNetwork(GetJsonString(stream, "network") ?? "tcp");
        server.Security = ShareLinkParser.NormalizeSecurity(GetJsonString(stream, "security") ?? "none");

        if (stream.TryGetProperty("tlsSettings", out var tls))
        {
            server.Sni = GetJsonString(tls, "serverName") ?? server.Sni;
            server.Fingerprint = GetJsonString(tls, "fingerprint") ?? server.Fingerprint;
            server.AllowInsecure = tls.TryGetProperty("allowInsecure", out var ai) && ai.ValueKind == JsonValueKind.True;
            if (tls.TryGetProperty("alpn", out var alpn) && alpn.ValueKind == JsonValueKind.Array)
                server.Alpn = string.Join(",", alpn.EnumerateArray().Select(x => x.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        if (stream.TryGetProperty("realitySettings", out var reality))
        {
            server.Security = "reality";
            server.Sni = GetJsonString(reality, "serverName") ?? server.Sni;
            server.Fingerprint = GetJsonString(reality, "fingerprint") ?? server.Fingerprint;
            server.PublicKey = GetJsonString(reality, "publicKey") ?? "";
            server.ShortId = GetJsonString(reality, "shortId") ?? "";
            server.SpiderX = GetJsonString(reality, "spiderX") ?? "";
        }

        if (stream.TryGetProperty("wsSettings", out var ws))
        {
            server.Path = GetJsonString(ws, "path") ?? server.Path;
            if (ws.TryGetProperty("headers", out var headers) && headers.TryGetProperty("Host", out var host))
                server.Host = host.GetString() ?? server.Host;
        }

        if (stream.TryGetProperty("grpcSettings", out var grpc))
        {
            server.ServiceName = GetJsonString(grpc, "serviceName") ?? server.ServiceName;
            if (grpc.TryGetProperty("multiMode", out var multi) && multi.ValueKind == JsonValueKind.True)
                server.Mode = "multi";
            else
                server.Mode = GetJsonString(grpc, "mode") ?? server.Mode;
        }

        if (stream.TryGetProperty("httpSettings", out var h2))
        {
            server.Path = GetJsonString(h2, "path") ?? server.Path;
            if (h2.TryGetProperty("host", out var hosts))
            {
                if (hosts.ValueKind == JsonValueKind.Array && hosts.GetArrayLength() > 0)
                    server.Host = hosts[0].GetString() ?? server.Host;
                else if (hosts.ValueKind == JsonValueKind.String)
                    server.Host = hosts.GetString() ?? server.Host;
            }
        }

        if (stream.TryGetProperty("httpupgradeSettings", out var httpUp) ||
            stream.TryGetProperty("httpUpgradeSettings", out httpUp))
        {
            server.Path = GetJsonString(httpUp, "path") ?? server.Path;
            server.Host = GetJsonString(httpUp, "host") ?? server.Host;
        }

        if (stream.TryGetProperty("xhttpSettings", out var xhttp) ||
            stream.TryGetProperty("splithttpSettings", out xhttp))
        {
            server.Path = GetJsonString(xhttp, "path") ?? server.Path;
            server.Host = GetJsonString(xhttp, "host") ?? server.Host;
            server.Mode = GetJsonString(xhttp, "mode") ?? server.Mode;
            if (xhttp.TryGetProperty("extra", out var extra) && extra.ValueKind == JsonValueKind.Object)
                server.Extra = extra.GetRawText();
        }

        if (stream.TryGetProperty("quicSettings", out var quic))
        {
            server.QuicSecurity = GetJsonString(quic, "security") ?? server.QuicSecurity;
            server.QuicKey = GetJsonString(quic, "key") ?? server.QuicKey;
            if (quic.TryGetProperty("header", out var qh))
                server.HeaderType = GetJsonString(qh, "type") ?? server.HeaderType;
        }

        if (stream.TryGetProperty("tcpSettings", out var tcp) &&
            tcp.TryGetProperty("header", out var header))
        {
            server.HeaderType = GetJsonString(header, "type") ?? server.HeaderType;
        }

        if (stream.TryGetProperty("kcpSettings", out var kcp))
        {
            server.Seed = GetJsonString(kcp, "seed") ?? server.Seed;
            if (kcp.TryGetProperty("header", out var kh))
                server.HeaderType = GetJsonString(kh, "type") ?? server.HeaderType;
        }

        ShareLinkParser.NormalizeVisionFlow(server);
    }

    public static IReadOnlyList<ProxyServer> ScanShareLinks(string text) =>
        ScanShareLinksDetailed(text).Servers;

    public static ImportResult ScanShareLinksDetailed(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return ImportResult.Empty;

        var results = new List<ProxyServer>();
        var skips = new List<string>();
        var skipCount = 0;

        foreach (Match match in ShareLinkRegex.Matches(text))
        {
            try
            {
                var link = match.Value.Trim().TrimEnd(',', ';', '"', '\'', ')', ']', '}');
                var unsupported = ShareLinkParser.GetUnsupportedSchemeHint(link);
                if (unsupported is not null)
                {
                    skipCount++;
                    if (!skips.Contains(unsupported))
                        skips.Add(unsupported);
                    continue;
                }

                if (link.StartsWith("ss://", StringComparison.OrdinalIgnoreCase))
                {
                    var detailed = ShareLinkParser.ParseBulkDetailed(link);
                    if (detailed.SkippedCount > 0)
                    {
                        skipCount += detailed.SkippedCount;
                        foreach (var s in detailed.SkipReasons)
                        {
                            if (!skips.Contains(s))
                                skips.Add(s);
                        }

                        continue;
                    }
                }

                var server = ShareLinkParser.Parse(link);
                if (server is not null)
                    results.Add(server);
            }
            catch
            {
                // Skip bad match.
            }
        }

        return ImportResult.FromServers(results, skips, skipCount);
    }

    private static IEnumerable<string> ExtractQuotedOrBase64Blobs(string text)
    {
        foreach (Match m in QuotedStringRegex().Matches(text))
        {
            var value = m.Groups[1].Value;
            if (value.Length >= 16)
                yield return value;
        }
    }

    private static IReadOnlyList<ProxyServer> Deduplicate(IReadOnlyList<ProxyServer> servers)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<ProxyServer>();
        foreach (var server in servers)
        {
            if (string.IsNullOrWhiteSpace(server.Address) || server.Port <= 0)
                continue;

            var key = $"{server.Protocol}|{server.Address}|{server.Port}|{server.UserId}|{server.Password}|{server.RawLink}";
            if (!seen.Add(key))
                continue;

            list.Add(server);
        }

        return list;
    }

    private static string? DetectUnsupportedHint(string text)
    {
        var reasons = new List<string>();
        if (text.Contains("hysteria2://", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("hy2://", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("hysteria2", StringComparison.OrdinalIgnoreCase))
            reasons.Add("Hysteria2 needs sing-box (not in this build)");
        if (text.Contains("tuic://", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("\"tuic\"", StringComparison.OrdinalIgnoreCase))
            reasons.Add("TUIC needs sing-box (not in this build)");
        if (text.Contains("anytls://", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("anytls", StringComparison.OrdinalIgnoreCase))
            reasons.Add("anytls needs sing-box (not in this build)");
        if (text.Contains("wg://", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("wireguard://", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("wireguard", StringComparison.OrdinalIgnoreCase))
            reasons.Add("WireGuard needs sing-box (not in this build)");
        if (text.Contains("plugin=", StringComparison.OrdinalIgnoreCase))
            reasons.Add("Shadowsocks plugins not supported (plain SS only)");

        if (reasons.Count == 0)
            return null;
        return reasons.Count == 1
            ? $"Skipped: {reasons[0]}"
            : $"Skipped unsupported: {string.Join("; ", reasons)}";
    }

    private static bool LooksLikeBareSubscriptionUrl(string text) =>
        !text.Contains("vmess://", StringComparison.OrdinalIgnoreCase) &&
        !text.Contains("vless://", StringComparison.OrdinalIgnoreCase) &&
        !text.Contains("ss://", StringComparison.OrdinalIgnoreCase) &&
        !text.Contains("trojan://", StringComparison.OrdinalIgnoreCase) &&
        !text.Contains('{') &&
        !text.Contains('\n');

    private static string? GetJsonString(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) ? v.GetString() : null;

    private static int GetJsonInt(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(name, out var v))
            return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(v.GetString(), out var s) => s,
            _ => 0
        };
    }

    [GeneratedRegex(@"(?:vmess|vless|trojan|ss|socks5?|socks|hysteria2|hy2|tuic|anytls|wg|wireguard)://[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ShareLinkPattern();

    [GeneratedRegex("\"([^\"]{16,})\"", RegexOptions.CultureInvariant)]
    private static partial Regex QuotedStringRegex();
}
