using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using v2rayF.Models;

namespace v2rayF.Services;

public static class ShareLinkParser
{
    public static IReadOnlyList<ProxyServer> ParseBulk(string input) =>
        ParseBulkDetailed(input).Servers;

    public static ImportResult ParseBulkDetailed(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return ImportResult.Empty;

        var trimmed = input.Trim();

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            (uri.Scheme is "http" or "https"))
        {
            return ImportResult.Empty;
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

        var servers = new List<ProxyServer>();
        var skips = new List<string>();
        var skipCount = 0;

        foreach (var line in trimmed.Split(
                     ['\r', '\n'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var unsupported = GetUnsupportedSchemeHint(line);
                if (unsupported is not null)
                {
                    skipCount++;
                    if (!skips.Contains(unsupported))
                        skips.Add(unsupported);
                    continue;
                }

                if (line.StartsWith("ss://", StringComparison.OrdinalIgnoreCase) &&
                    TryGetSsPluginSkipReason(line, out var ssSkip))
                {
                    skipCount++;
                    if (!skips.Contains(ssSkip!))
                        skips.Add(ssSkip!);
                    continue;
                }

                var server = Parse(line);
                if (server is not null)
                    servers.Add(server);
            }
            catch
            {
                // Skip malformed lines.
            }
        }

        return ImportResult.FromServers(servers, skips, skipCount);
    }

    private static bool TryGetSsPluginSkipReason(string link, out string? reason)
    {
        reason = null;
        try
        {
            var hashIndex = link.IndexOf('#');
            var body = hashIndex >= 0 ? link[..hashIndex] : link;
            body = body["ss://".Length..];
            if (!body.Contains('@'))
                return false;
            var atIndex = body.LastIndexOf('@');
            var hostPart = body[(atIndex + 1)..];
            ParseHostPort(hostPart, out _, out _, out var query);
            if (!HasUnsupportedSsPlugin(query))
                return false;
            var plugin = GetQuery(query, "plugin") ?? "plugin";
            reason = $"Shadowsocks plugin '{plugin.Split(';')[0]}' not supported (plain SS only)";
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static ProxyServer? Parse(string link)
    {
        if (string.IsNullOrWhiteSpace(link))
            return null;

        link = link.Trim();

        if (IsSingBoxOnlyScheme(link))
            return null;

        return link switch
        {
            _ when link.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase) => ParseVmess(link),
            _ when link.StartsWith("vless://", StringComparison.OrdinalIgnoreCase) => ParseVless(link),
            _ when link.StartsWith("ss://", StringComparison.OrdinalIgnoreCase) => ParseShadowsocks(link),
            _ when link.StartsWith("trojan://", StringComparison.OrdinalIgnoreCase) => ParseTrojan(link),
            _ when link.StartsWith("socks://", StringComparison.OrdinalIgnoreCase) ||
                   link.StartsWith("socks5://", StringComparison.OrdinalIgnoreCase) => ParseSocks(link),
            _ when link.StartsWith("hysteria2://", StringComparison.OrdinalIgnoreCase) ||
                   link.StartsWith("hy2://", StringComparison.OrdinalIgnoreCase) => ParseHysteria2(link),
            _ when link.StartsWith("tuic://", StringComparison.OrdinalIgnoreCase) => ParseTuic(link),
            _ when link.StartsWith("anytls://", StringComparison.OrdinalIgnoreCase) => ParseAnyTls(link),
            _ when link.StartsWith("wg://", StringComparison.OrdinalIgnoreCase) ||
                   link.StartsWith("wireguard://", StringComparison.OrdinalIgnoreCase) => ParseWireGuard(link),
            _ => null
        };
    }

    /// <summary>Legacy name: schemes that previously could not run. Dual-core 2.0 parses these.</summary>
    internal static bool IsSingBoxOnlyScheme(string link) => false;

    /// <summary>Human-readable skip reason — null when the link can be imported (Xray or sing-box).</summary>
    public static string? GetUnsupportedSchemeHint(string link)
    {
        // All known schemes are importable in 2.0 when the matching core binary is present.
        return null;
    }

    /// <summary>True when SIP003 plugin query would produce a non-working plain SS node.</summary>
    public static bool HasUnsupportedSsPlugin(IReadOnlyDictionary<string, string> query)
    {
        if (!query.TryGetValue("plugin", out var plugin) && !query.TryGetValue("Plugin", out plugin))
            return false;
        return !string.IsNullOrWhiteSpace(plugin);
    }

    private static ProxyServer ParseVmess(string link)
    {
        var payload = link["vmess://".Length..];
        var hash = payload.IndexOf('#');
        var body = hash >= 0 ? payload[..hash] : payload;
        if (body.Contains('@') && !LooksLikeBase64(body.Split('?', 2)[0].Replace("-", "").Replace("_", "")))
            return ParseVmessQueryUri(link);

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(NormalizeBase64(body)));
            return ParseVmessJson(link, json, hash >= 0 ? payload[(hash + 1)..] : null);
        }
        catch (FormatException)
        {
            return ParseVmessQueryUri(link);
        }
        catch (JsonException)
        {
            return ParseVmessQueryUri(link);
        }
    }

    private static ProxyServer ParseVmessJson(string link, string json, string? fragment)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var network = NormalizeNetwork(GetString(root, "net") ?? "tcp");
        var security = MapTls(GetString(root, "tls") ?? GetString(root, "security"));
        var path = GetString(root, "path") ?? "";
        var host = GetString(root, "host") ?? "";
        var serviceName = GetString(root, "serviceName") ?? "";
        if (string.IsNullOrWhiteSpace(serviceName) &&
            network.Equals("grpc", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(path))
            serviceName = path;

        var server = new ProxyServer
        {
            Protocol = ProxyProtocol.VMess,
            Name = GetString(root, "ps") ?? "VMess",
            Address = GetString(root, "add") ?? "",
            Port = GetInt(root, "port"),
            UserId = GetString(root, "id") ?? "",
            AlterId = GetInt(root, "aid"),
            Cipher = GetString(root, "scy") ?? "auto",
            Network = network,
            Security = security,
            Flow = GetString(root, "flow") ?? "",
            Host = host,
            Path = path,
            Sni = GetString(root, "sni") ?? host,
            Fingerprint = FirstNonEmpty(GetString(root, "fp"), GetString(root, "fingerprint"), "chrome")!,
            PublicKey = GetString(root, "pbk") ?? "",
            ShortId = GetString(root, "sid") ?? "",
            SpiderX = GetString(root, "spx") ?? "",
            Alpn = NormalizeAlpn(GetString(root, "alpn")),
            HeaderType = GetString(root, "type") ?? "",
            ServiceName = serviceName,
            Mode = GetString(root, "mode") ?? "",
            Seed = GetString(root, "seed") ?? "",
            AllowInsecure = GetString(root, "allowInsecure") is "1" or "true",
            RawLink = link
        };

        if (!string.IsNullOrWhiteSpace(fragment) &&
            (string.IsNullOrWhiteSpace(server.Name) || server.Name == "VMess"))
            server.Name = Uri.UnescapeDataString(fragment);
        return server;
    }

    private static ProxyServer ParseVmessQueryUri(string link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri))
            throw new FormatException("Invalid VMess link.");

        var query = ParseQuery(uri.Query);
        var name = Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));
        var server = new ProxyServer
        {
            Protocol = ProxyProtocol.VMess,
            Name = string.IsNullOrWhiteSpace(name) ? "VMess" : name,
            Address = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 443,
            UserId = Uri.UnescapeDataString(uri.UserInfo),
            Cipher = GetQuery(query, "encryption") ?? GetQuery(query, "scy") ?? "auto",
            RawLink = link
        };
        ApplyStreamFromQuery(server, query, defaultSecurity: "none");
        return server;
    }

    private static ProxyServer ParseVless(string link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri))
            throw new FormatException("Invalid VLESS link.");

        var query = ParseQuery(uri.Query);
        var name = Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));
        if (string.IsNullOrWhiteSpace(name))
            name = "VLESS";

        var server = new ProxyServer
        {
            Protocol = ProxyProtocol.VLESS,
            Name = name,
            Address = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 443,
            UserId = Uri.UnescapeDataString(uri.UserInfo),
            Encryption = GetQuery(query, "encryption") ?? "none",
            RawLink = link
        };

        ApplyStreamFromQuery(server, query, defaultSecurity: "none");
        NormalizeVisionFlow(server);
        return server;
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

            ParseHostPort(hostPart, out host, out port, out var query);
            if (HasUnsupportedSsPlugin(query))
                throw new FormatException(
                    $"Shadowsocks plugin not supported (plain SS only): {GetQuery(query, "plugin")}");
            var server = new ProxyServer
            {
                Protocol = ProxyProtocol.Shadowsocks,
                Name = string.IsNullOrWhiteSpace(name) ? "Shadowsocks" : name,
                Address = host,
                Port = port,
                Cipher = method,
                Password = password,
                RawLink = originalLink
            };
            if (query.Count > 0)
                ApplyStreamFromQuery(server, query, defaultSecurity: "none");
            return server;
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
            ParseHostPort(hostPart, out host, out port, out var query);
            if (HasUnsupportedSsPlugin(query))
                throw new FormatException(
                    $"Shadowsocks plugin not supported (plain SS only): {GetQuery(query, "plugin")}");
            var server = new ProxyServer
            {
                Protocol = ProxyProtocol.Shadowsocks,
                Name = string.IsNullOrWhiteSpace(name) ? "Shadowsocks" : name,
                Address = host,
                Port = port,
                Cipher = method,
                Password = password,
                RawLink = originalLink
            };
            if (query.Count > 0)
                ApplyStreamFromQuery(server, query, defaultSecurity: "none");
            return server;
        }
    }

    private static ProxyServer ParseTrojan(string link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri))
            throw new FormatException("Invalid Trojan link.");

        var query = ParseQuery(uri.Query);
        var name = Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));
        if (string.IsNullOrWhiteSpace(name))
            name = "Trojan";

        var server = new ProxyServer
        {
            Protocol = ProxyProtocol.Trojan,
            Name = name,
            Address = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 443,
            Password = Uri.UnescapeDataString(uri.UserInfo),
            RawLink = link
        };

        ApplyStreamFromQuery(server, query, defaultSecurity: "tls");
        return server;
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

    private static ProxyServer ParseHysteria2(string link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri))
            throw new FormatException("Invalid Hysteria2 link.");

        var query = ParseQuery(uri.Query);
        var name = Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));
        return new ProxyServer
        {
            Protocol = ProxyProtocol.Hysteria2,
            Name = string.IsNullOrWhiteSpace(name) ? "Hysteria2" : name,
            Address = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 443,
            Password = Uri.UnescapeDataString(uri.UserInfo),
            Sni = GetQuery(query, "sni") ?? GetQuery(query, "peer") ?? "",
            Path = GetQuery(query, "obfs-password") ?? GetQuery(query, "obfsPassword") ?? "",
            AllowInsecure = GetQuery(query, "insecure") is "1" or "true",
            Fingerprint = GetQuery(query, "pinSHA256") ?? GetQuery(query, "fp") ?? "",
            UpMbps = ParseMbps(
                GetQuery(query, "upmbps") ?? GetQuery(query, "up")),
            DownMbps = ParseMbps(
                GetQuery(query, "downmbps") ?? GetQuery(query, "down")),
            RawLink = link
        };
    }

    private static ProxyServer ParseTuic(string link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri))
            throw new FormatException("Invalid TUIC link.");

        var query = ParseQuery(uri.Query);
        var name = Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));
        var userInfo = Uri.UnescapeDataString(uri.UserInfo);
        var colon = userInfo.IndexOf(':');
        var uuid = colon >= 0 ? userInfo[..colon] : userInfo;
        var password = colon >= 0 ? userInfo[(colon + 1)..] : (GetQuery(query, "password") ?? "");

        return new ProxyServer
        {
            Protocol = ProxyProtocol.Tuic,
            Name = string.IsNullOrWhiteSpace(name) ? "TUIC" : name,
            Address = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 443,
            UserId = uuid,
            Password = password,
            Sni = GetQuery(query, "sni") ?? "",
            Mode = GetQuery(query, "congestion_control") ?? GetQuery(query, "congestion") ?? "bbr",
            UdpRelayMode = GetQuery(query, "udp_relay_mode") ?? GetQuery(query, "udp-relay-mode") ?? "",
            AllowInsecure = GetQuery(query, "allow_insecure") is "1" or "true" ||
                            GetQuery(query, "insecure") is "1" or "true",
            Alpn = GetQuery(query, "alpn") ?? "",
            RawLink = link
        };
    }

    private static ProxyServer ParseAnyTls(string link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri))
            throw new FormatException("Invalid anytls link.");

        var query = ParseQuery(uri.Query);
        var name = Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));
        return new ProxyServer
        {
            Protocol = ProxyProtocol.AnyTls,
            Name = string.IsNullOrWhiteSpace(name) ? "anytls" : name,
            Address = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 443,
            Password = Uri.UnescapeDataString(uri.UserInfo),
            Sni = GetQuery(query, "sni") ?? "",
            AllowInsecure = GetQuery(query, "insecure") is "1" or "true",
            RawLink = link
        };
    }

    private static ProxyServer ParseWireGuard(string link)
    {
        // wireguard://PRIVATEKEY@server:port?publickey=PEERPK&address=10.0.0.2/32&reserved=0,0,0#name
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri))
            throw new FormatException("Invalid WireGuard link.");

        var query = ParseQuery(uri.Query);
        var name = Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));
        var mtu = 0;
        _ = int.TryParse(GetQuery(query, "mtu"), out mtu);
        return new ProxyServer
        {
            Protocol = ProxyProtocol.WireGuard,
            Name = string.IsNullOrWhiteSpace(name) ? "WireGuard" : name,
            Address = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 51820,
            Password = Uri.UnescapeDataString(uri.UserInfo),
            PublicKey = GetQuery(query, "publickey") ?? GetQuery(query, "publicKey") ?? GetQuery(query, "peer") ?? "",
            Path = GetQuery(query, "address") ?? GetQuery(query, "local") ?? "10.0.0.2/32",
            ShortId = GetQuery(query, "reserved") ?? "",
            Mtu = mtu > 0 ? mtu : 0,
            RawLink = link
        };
    }

    /// <summary>Parses Mbps from share/Clash values like "100", "100 Mbps", or "100Mbps".</summary>
    internal static int ParseMbps(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return 0;
        var s = raw.Trim();
        var end = 0;
        while (end < s.Length && (char.IsDigit(s[end]) || s[end] == '.'))
            end++;
        if (end == 0)
            return 0;
        if (!double.TryParse(s[..end], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value) ||
            value <= 0)
            return 0;
        return (int)Math.Round(value);
    }

    /// <summary>Maps common share-link query keys onto stream fields (VLESS / Trojan / compatible).</summary>
    private static void ApplyStreamFromQuery(
        ProxyServer server,
        Dictionary<string, string> query,
        string defaultSecurity)
    {
        server.Network = NormalizeNetwork(GetQuery(query, "type") ?? GetQuery(query, "net") ?? "tcp");
        server.Security = NormalizeSecurity(GetQuery(query, "security") ?? defaultSecurity);
        server.Flow = GetQuery(query, "flow") ?? "";
        server.Sni = GetQuery(query, "sni") ?? GetQuery(query, "peer") ?? "";
        server.Host = GetQuery(query, "host") ?? "";
        server.Path = GetQuery(query, "path") ?? "";
        server.Fingerprint = FirstNonEmpty(GetQuery(query, "fp"), GetQuery(query, "fingerprint"), "chrome")!;
        server.PublicKey = GetQuery(query, "pbk") ?? "";
        server.ShortId = GetQuery(query, "sid") ?? "";
        server.SpiderX = GetQuery(query, "spx") ?? "";
        server.Alpn = NormalizeAlpn(GetQuery(query, "alpn"));
        server.HeaderType = GetQuery(query, "headerType") ?? GetQuery(query, "header") ?? "";
        server.ServiceName = GetQuery(query, "serviceName") ?? GetQuery(query, "servicename") ?? "";
        server.Mode = GetQuery(query, "mode") ?? "";
        server.Seed = GetQuery(query, "seed") ?? "";
        server.Extra = GetQuery(query, "extra") ?? "";
        server.QuicSecurity = GetQuery(query, "quicSecurity") ?? "";
        if (server.Network.Equals("quic", StringComparison.OrdinalIgnoreCase))
            server.QuicKey = GetQuery(query, "key") ?? server.QuicKey;
        server.PacketEncoding = GetQuery(query, "packetEncoding") ?? GetQuery(query, "packetencoding") ?? "";
        if (int.TryParse(GetQuery(query, "ed") ?? GetQuery(query, "maxEarlyData"), out var ed) && ed > 0)
            server.MaxEarlyData = ed;
        server.EarlyDataHeaderName = GetQuery(query, "eh") ?? GetQuery(query, "earlyDataHeaderName") ?? "";
        server.AllowInsecure =
            GetQuery(query, "allowInsecure") is "1" or "true" ||
            GetQuery(query, "insecure") is "1" or "true";

        if (string.IsNullOrWhiteSpace(server.Sni) && !string.IsNullOrWhiteSpace(server.Host))
            server.Sni = server.Host;

        // gRPC often puts service name in path when serviceName is absent.
        if (string.IsNullOrWhiteSpace(server.ServiceName) &&
            server.Network.Equals("grpc", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(server.Path))
            server.ServiceName = server.Path;
    }

    /// <summary>Vision only applies on TCP. Infers tls/reality when the link omitted security.</summary>
    internal static void NormalizeVisionFlow(ProxyServer server)
    {
        if (string.IsNullOrWhiteSpace(server.Flow))
            return;

        if (!server.Network.Equals("tcp", StringComparison.OrdinalIgnoreCase) &&
            !server.Network.Equals("raw", StringComparison.OrdinalIgnoreCase))
        {
            server.Flow = "";
            return;
        }

        if (server.Flow.Equals("vision", StringComparison.OrdinalIgnoreCase) ||
            server.Flow.Equals("xtls-rprx-vision", StringComparison.OrdinalIgnoreCase))
            server.Flow = "xtls-rprx-vision";
        else if (server.Flow.Equals("xtls-rprx-vision-udp443", StringComparison.OrdinalIgnoreCase))
            server.Flow = "xtls-rprx-vision-udp443";

        if (!IsVisionFlow(server))
            return;

        var security = NormalizeSecurity(server.Security);
        if (security is "none" or "")
            server.Security = string.IsNullOrWhiteSpace(server.PublicKey) ? "tls" : "reality";
    }

    internal static bool IsVisionFlow(ProxyServer server) =>
        !string.IsNullOrWhiteSpace(server.Flow) &&
        server.Flow.Contains("vision", StringComparison.OrdinalIgnoreCase);

    internal static string NormalizeNetwork(string? network)
    {
        if (string.IsNullOrWhiteSpace(network))
            return "tcp";

        return network.Trim().ToLowerInvariant() switch
        {
            "tcp" or "raw" => "tcp",
            "ws" or "websocket" => "ws",
            "grpc" or "gun" => "grpc",
            "h2" or "http" => "h2",
            "httpupgrade" or "http_upgrade" => "httpupgrade",
            "xhttp" or "splithttp" => "xhttp",
            "kcp" or "mkcp" => "kcp",
            "quic" => "quic",
            _ => network.Trim().ToLowerInvariant()
        };
    }

    internal static string NormalizeSecurity(string? security)
    {
        if (string.IsNullOrWhiteSpace(security) || security.Equals("none", StringComparison.OrdinalIgnoreCase))
            return "none";
        if (security.Equals("reality", StringComparison.OrdinalIgnoreCase))
            return "reality";
        if (security.Equals("tls", StringComparison.OrdinalIgnoreCase) ||
            security.Equals("xtls", StringComparison.OrdinalIgnoreCase))
            return "tls";
        return security.Trim().ToLowerInvariant();
    }

    private static string NormalizeAlpn(string? alpn)
    {
        if (string.IsNullOrWhiteSpace(alpn))
            return "";
        return alpn.Replace('|', ',').Trim();
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static void ParseHostPort(string hostPart, out string host, out int port, out Dictionary<string, string> query)
    {
        query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var q = hostPart.IndexOf('?');
        if (q >= 0)
        {
            query = ParseQuery(hostPart[q..]);
            hostPart = hostPart[..q];
        }

        hostPart = hostPart.TrimEnd('/');

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

    private static string MapTls(string? tls)
    {
        if (string.IsNullOrWhiteSpace(tls) || tls.Equals("none", StringComparison.OrdinalIgnoreCase))
            return "none";
        if (tls.Equals("reality", StringComparison.OrdinalIgnoreCase))
            return "reality";
        return "tls";
    }

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
