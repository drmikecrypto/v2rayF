using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using v2rayF.Models;

namespace v2rayF.Services;

/// <summary>
/// Minimal Clash Meta <c>proxies:</c> importer — no YAML NuGet.
/// Maps only Xray-runnable types; records skip reasons for hy2/tuic/wg/etc.
/// </summary>
public static partial class ClashMetaImportParser
{
    public static bool LooksLikeClash(string text) =>
        text.Contains("proxies:", StringComparison.OrdinalIgnoreCase) &&
        (text.Contains("proxy-groups:", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("type:", StringComparison.OrdinalIgnoreCase));

    public static ImportResult Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !LooksLikeClash(text))
            return ImportResult.Empty;

        var servers = new List<ProxyServer>();
        var skips = new List<string>();
        var skipCount = 0;

        foreach (var block in ExtractProxyBlocks(text))
        {
            var map = ParseKeyValues(block);
            if (!map.TryGetValue("type", out var type) || string.IsNullOrWhiteSpace(type))
                continue;

            type = type.Trim().ToLowerInvariant();
            switch (type)
            {
                case "ss":
                case "shadowsocks":
                {
                    if (!string.IsNullOrWhiteSpace(Get(map, "plugin")))
                    {
                        skipCount++;
                        AddUnique(skips, "Shadowsocks plugin not supported (plain SS only)");
                        break;
                    }

                    var ss = MapProxy(type, map);
                    if (ss is not null)
                        servers.Add(ss);
                    break;
                }
                case "vmess":
                case "vless":
                case "trojan":
                case "socks":
                case "socks5":
                {
                    var server = MapProxy(type, map);
                    if (server is not null)
                        servers.Add(server);
                    break;
                }
                case "hysteria2":
                case "hysteria":
                    skipCount++;
                    AddUnique(skips, "Hysteria2 needs sing-box (not in this build)");
                    break;
                case "tuic":
                    skipCount++;
                    AddUnique(skips, "TUIC needs sing-box (not in this build)");
                    break;
                case "wireguard":
                    skipCount++;
                    AddUnique(skips, "WireGuard needs sing-box (not in this build)");
                    break;
                case "anytls":
                    skipCount++;
                    AddUnique(skips, "anytls needs sing-box (not in this build)");
                    break;
                default:
                    skipCount++;
                    AddUnique(skips, $"Clash type '{type}' not supported in this Xray build");
                    break;
            }
        }

        return ImportResult.FromServers(servers, skips, skipCount);
    }

    private static ProxyServer? MapProxy(string type, Dictionary<string, string> map)
    {
        if (!map.TryGetValue("server", out var address) || string.IsNullOrWhiteSpace(address))
            return null;
        if (!map.TryGetValue("port", out var portRaw) ||
            !int.TryParse(portRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) ||
            port <= 0)
            return null;

        map.TryGetValue("name", out var name);
        var server = new ProxyServer
        {
            Name = string.IsNullOrWhiteSpace(name) ? type : name.Trim().Trim('"', '\''),
            Address = address.Trim().Trim('"', '\''),
            Port = port,
            RawLink = $"clash://{type}/{address}:{port}"
        };

        switch (type)
        {
            case "vmess":
                server.Protocol = ProxyProtocol.VMess;
                server.UserId = Get(map, "uuid") ?? Get(map, "id") ?? "";
                if (int.TryParse(Get(map, "alterId") ?? Get(map, "aid"), out var aid))
                    server.AlterId = aid;
                server.Cipher = Get(map, "cipher") ?? "auto";
                break;
            case "vless":
                server.Protocol = ProxyProtocol.VLESS;
                server.UserId = Get(map, "uuid") ?? "";
                server.Flow = Get(map, "flow") ?? "";
                server.Encryption = Get(map, "encryption") ?? "none";
                break;
            case "trojan":
                server.Protocol = ProxyProtocol.Trojan;
                server.Password = Get(map, "password") ?? "";
                break;
            case "ss":
            case "shadowsocks":
                server.Protocol = ProxyProtocol.Shadowsocks;
                server.Cipher = Get(map, "cipher") ?? "";
                server.Password = Get(map, "password") ?? "";
                break;
            case "socks":
            case "socks5":
                server.Protocol = ProxyProtocol.Socks;
                server.UserId = Get(map, "username") ?? Get(map, "user") ?? "";
                server.Password = Get(map, "password") ?? "";
                break;
        }

        ApplyClashStream(server, map);
        ShareLinkParser.NormalizeVisionFlow(server);
        return server;
    }

    private static void ApplyClashStream(ProxyServer server, Dictionary<string, string> map)
    {
        var network = Get(map, "network") ?? Get(map, "type") ?? "tcp";
        // Clash uses network: ws|grpc|h2|... separately from type.
        network = Get(map, "network") ?? "tcp";
        server.Network = ShareLinkParser.NormalizeNetwork(network);

        var tls = Get(map, "tls");
        var reality = Get(map, "reality-opts") is not null || map.Keys.Any(k => k.StartsWith("reality-", StringComparison.Ordinal));
        if (string.Equals(Get(map, "client-fingerprint"), "chrome", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(Get(map, "servername")) ||
            tls is "true" or "1")
            server.Security = reality || !string.IsNullOrWhiteSpace(Get(map, "public-key")) ? "reality" : "tls";
        else if (!string.IsNullOrWhiteSpace(Get(map, "public-key")))
            server.Security = "reality";
        else
            server.Security = ShareLinkParser.NormalizeSecurity(Get(map, "security") ?? "none");

        server.Sni = Get(map, "servername") ?? Get(map, "sni") ?? "";
        server.Fingerprint = Get(map, "client-fingerprint") ?? Get(map, "fingerprint") ?? "chrome";
        server.PublicKey = Get(map, "public-key") ?? Get(map, "publicKey") ?? "";
        server.ShortId = Get(map, "short-id") ?? Get(map, "shortId") ?? "";
        server.Path = Get(map, "ws-opts.path") ?? Get(map, "path") ?? "";
        server.Host = Get(map, "ws-opts.headers.Host") ?? Get(map, "host") ?? "";
        server.ServiceName = Get(map, "grpc-opts.grpc-service-name") ?? Get(map, "serviceName") ?? "";
        server.Alpn = Get(map, "alpn") ?? "";
        server.PacketEncoding = Get(map, "packet-encoding") ?? Get(map, "packetEncoding") ?? "";
        if (int.TryParse(Get(map, "max-early-data") ?? Get(map, "ed"), out var ed) && ed > 0)
            server.MaxEarlyData = ed;
        if (string.IsNullOrWhiteSpace(server.Sni) && !string.IsNullOrWhiteSpace(server.Host))
            server.Sni = server.Host;
    }

    private static IEnumerable<string> ExtractProxyBlocks(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var inProxies = false;
        var current = new List<string>();
        var blocks = new List<string>();
        foreach (var raw in lines)
        {
            var line = raw;
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("proxies:", StringComparison.OrdinalIgnoreCase))
            {
                inProxies = true;
                continue;
            }

            if (!inProxies)
                continue;

            if (trimmed.Length == 0)
                continue;

            // Next top-level key ends proxies section.
            if (!char.IsWhiteSpace(line, 0) && !trimmed.StartsWith('-') && trimmed.Contains(':'))
            {
                if (current.Count > 0)
                {
                    blocks.Add(string.Join('\n', current));
                    current.Clear();
                }

                inProxies = false;
                continue;
            }

            if (trimmed.StartsWith("- "))
            {
                if (current.Count > 0)
                {
                    blocks.Add(string.Join('\n', current));
                    current.Clear();
                }

                // Inline map: - { name: x, type: vmess, ... }
                if (trimmed.Contains('{'))
                {
                    blocks.Add(trimmed[2..].Trim());
                    continue;
                }

                current.Add(trimmed[2..]);
                continue;
            }

            if (current.Count > 0 || trimmed.Contains(':'))
                current.Add(trimmed);
        }

        if (current.Count > 0)
            blocks.Add(string.Join('\n', current));

        return blocks;
    }

    private static Dictionary<string, string> ParseKeyValues(string block)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Inline JSON-ish / flow style: { name: a, type: vmess, server: x, port: 443 }
        if (block.TrimStart().StartsWith('{'))
        {
            foreach (Match m in InlineKvRegex().Matches(block))
            {
                var key = m.Groups[1].Value.Trim();
                var val = m.Groups[2].Value.Trim().Trim('"', '\'', '}', ',');
                if (!string.IsNullOrEmpty(key))
                    map[key] = val;
            }

            return map;
        }

        foreach (var line in block.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('#'))
                continue;
            var colon = trimmed.IndexOf(':');
            if (colon <= 0)
                continue;
            var key = trimmed[..colon].Trim();
            var val = trimmed[(colon + 1)..].Trim().Trim('"', '\'');
            if (key.Length == 0)
                continue;
            map[key] = val;

            // Nested opts flattened lightly: capture "path: /x" under ws-opts as ws-opts.path when previous key was ws-opts
        }

        // Second pass for nested one-level maps in multi-line form is best-effort via dotted keys if present as "ws-opts.path".
        return map;
    }

    private static string? Get(Dictionary<string, string> map, string key) =>
        map.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    private static void AddUnique(List<string> list, string item)
    {
        if (!list.Contains(item))
            list.Add(item);
    }

    [GeneratedRegex(@"([A-Za-z0-9_\-]+)\s*:\s*([^,\{\}]+)", RegexOptions.CultureInvariant)]
    private static partial Regex InlineKvRegex();
}
