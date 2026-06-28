using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using v2rayF.Models;

namespace v2rayF.Services;

public static class ShareLinkParser
{
    public static IReadOnlyList<ProxyServer> ParseBulk(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Array.Empty<ProxyServer>();

        var trimmed = input.Trim();

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            (uri.Scheme is "http" or "https"))
        {
            return Array.Empty<ProxyServer>();
        }

        if (!trimmed.Contains("://", StringComparison.Ordinal) && LooksLikeBase64(trimmed))
        {
            try
            {
                trimmed = Encoding.UTF8.GetString(Convert.FromBase64String(NormalizeBase64(trimmed)));
            }
            catch
            {
                // Not base64 subscription payload; continue as plain text.
            }
        }

        return trimmed
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(line =>
            {
                try
                {
                    var server = Parse(line);
                    return server is null ? Array.Empty<ProxyServer>() : [server];
                }
                catch
                {
                    return Array.Empty<ProxyServer>();
                }
            })
            .ToList();
    }

    public static ProxyServer? Parse(string link)
    {
        if (string.IsNullOrWhiteSpace(link))
            return null;

        link = link.Trim();

        return link switch
        {
            _ when link.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase) => ParseVmess(link),
            _ when link.StartsWith("vless://", StringComparison.OrdinalIgnoreCase) => ParseVless(link),
            _ when link.StartsWith("ss://", StringComparison.OrdinalIgnoreCase) => ParseShadowsocks(link),
            _ when link.StartsWith("trojan://", StringComparison.OrdinalIgnoreCase) => ParseTrojan(link),
            _ when link.StartsWith("socks://", StringComparison.OrdinalIgnoreCase) ||
                   link.StartsWith("socks5://", StringComparison.OrdinalIgnoreCase) => ParseSocks(link),
            _ => null
        };
    }

    private static ProxyServer ParseVmess(string link)
    {
        var payload = link["vmess://".Length..];
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(NormalizeBase64(payload)));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        return new ProxyServer
        {
            Protocol = ProxyProtocol.VMess,
            Name = GetString(root, "ps") ?? "VMess",
            Address = GetString(root, "add") ?? "",
            Port = GetInt(root, "port"),
            UserId = GetString(root, "id") ?? "",
            AlterId = GetInt(root, "aid"),
            Network = GetString(root, "net") ?? "tcp",
            Security = MapTls(GetString(root, "tls")),
            Host = GetString(root, "host") ?? "",
            Path = GetString(root, "path") ?? "",
            Sni = GetString(root, "sni") ?? GetString(root, "host") ?? "",
            RawLink = link
        };
    }

    private static ProxyServer ParseVless(string link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri))
            throw new FormatException("Invalid VLESS link.");

        var query = ParseQuery(uri.Query);
        var name = Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));
        if (string.IsNullOrWhiteSpace(name))
            name = "VLESS";

        return new ProxyServer
        {
            Protocol = ProxyProtocol.VLESS,
            Name = name,
            Address = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 443,
            UserId = Uri.UnescapeDataString(uri.UserInfo),
            Network = GetQuery(query, "type") ?? "tcp",
            Security = GetQuery(query, "security") ?? "none",
            Flow = GetQuery(query, "flow") ?? "",
            Sni = GetQuery(query, "sni") ?? GetQuery(query, "peer") ?? "",
            Host = GetQuery(query, "host") ?? "",
            Path = GetQuery(query, "path") ?? "",
            Fingerprint = GetQuery(query, "fp") ?? "chrome",
            PublicKey = GetQuery(query, "pbk") ?? "",
            ShortId = GetQuery(query, "sid") ?? "",
            AllowInsecure = GetQuery(query, "allowInsecure") == "1",
            RawLink = link
        };
    }

    private static ProxyServer ParseShadowsocks(string link)
    {
        var originalLink = link;
        var name = "";
        var hashIndex = link.IndexOf('#');
        if (hashIndex >= 0)
        {
            name = Uri.UnescapeDataString(link[(hashIndex + 1)..]);
            link = link[..hashIndex];
        }

        link = link["ss://".Length..];
        string method;
        string password;
        string host;
        int port;

        if (link.Contains('@'))
        {
            var atIndex = link.LastIndexOf('@');
            var userInfo = link[..atIndex];
            var hostPart = link[(atIndex + 1)..];

            if (userInfo.Contains(':'))
            {
                var colon = userInfo.IndexOf(':');
                method = userInfo[..colon];
                password = userInfo[(colon + 1)..];
            }
            else
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(NormalizeBase64(userInfo)));
                var colon = decoded.IndexOf(':');
                method = decoded[..colon];
                password = decoded[(colon + 1)..];
            }

            ParseHostPort(hostPart, out host, out port);
        }
        else
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(NormalizeBase64(link)));
            var at = decoded.LastIndexOf('@');
            if (at < 0)
                throw new FormatException("Invalid Shadowsocks link.");

            var creds = decoded[..at];
            var hostPart = decoded[(at + 1)..];
            var colon = creds.IndexOf(':');
            method = creds[..colon];
            password = creds[(colon + 1)..];
            ParseHostPort(hostPart, out host, out port);
        }

        return new ProxyServer
        {
            Protocol = ProxyProtocol.Shadowsocks,
            Name = string.IsNullOrWhiteSpace(name) ? "Shadowsocks" : name,
            Address = host,
            Port = port,
            Cipher = method,
            Password = password,
            RawLink = originalLink
        };
    }

    private static ProxyServer ParseTrojan(string link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri))
            throw new FormatException("Invalid Trojan link.");

        var query = ParseQuery(uri.Query);
        var name = Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));
        if (string.IsNullOrWhiteSpace(name))
            name = "Trojan";

        return new ProxyServer
        {
            Protocol = ProxyProtocol.Trojan,
            Name = name,
            Address = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 443,
            Password = Uri.UnescapeDataString(uri.UserInfo),
            Sni = GetQuery(query, "sni") ?? GetQuery(query, "peer") ?? uri.Host,
            AllowInsecure = GetQuery(query, "allowInsecure") == "1",
            Network = GetQuery(query, "type") ?? "tcp",
            Security = "tls",
            RawLink = link
        };
    }

    private static ProxyServer ParseSocks(string link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri))
            throw new FormatException("Invalid SOCKS link.");

        var name = Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));
        var parts = uri.UserInfo.Split(':', 2);

        return new ProxyServer
        {
            Protocol = ProxyProtocol.Socks,
            Name = string.IsNullOrWhiteSpace(name) ? "SOCKS" : name,
            Address = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 1080,
            UserId = parts.Length > 0 ? Uri.UnescapeDataString(parts[0]) : "",
            Password = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "",
            RawLink = link
        };
    }

    private static void ParseHostPort(string hostPart, out string host, out int port)
    {
        if (hostPart.StartsWith('['))
        {
            var end = hostPart.IndexOf(']');
            host = hostPart[1..end];
            port = int.Parse(hostPart[(end + 2)..]);
            return;
        }

        var colon = hostPart.LastIndexOf(':');
        if (colon < 0)
            throw new FormatException("Missing port in Shadowsocks link.");

        host = hostPart[..colon];
        port = int.Parse(hostPart[(colon + 1)..]);
    }

    private static string MapTls(string? tls) =>
        string.IsNullOrWhiteSpace(tls) || tls == "none" ? "none" : "tls";

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetString() : null;

    private static int GetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && int.TryParse(value.ToString(), out var number)
            ? number
            : 0;

    private static string NormalizeBase64(string value)
    {
        value = value.Trim().Replace('-', '+').Replace('_', '/');
        var padding = value.Length % 4;
        if (padding > 0)
            value = value.PadRight(value.Length + (4 - padding), '=');
        return value;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query))
            return result;

        if (query.StartsWith('?'))
            query = query[1..];

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0)
            {
                result[Uri.UnescapeDataString(pair)] = "";
                continue;
            }

            var key = Uri.UnescapeDataString(pair[..eq]);
            var value = Uri.UnescapeDataString(pair[(eq + 1)..]);
            result[key] = value;
        }

        return result;
    }

    private static string? GetQuery(Dictionary<string, string> query, string key) =>
        query.TryGetValue(key, out var value) ? value : null;

    private static bool LooksLikeBase64(string value)
    {
        if (value.Length < 16 || value.Contains("://", StringComparison.Ordinal))
            return false;

        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch) || ch is '+' or '/' or '=' or '-' or '_')
                continue;
            return false;
        }

        return true;
    }
}
