using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using v2rayF.Models;

namespace v2rayF.Services;

/// <summary>
/// Imports Xray-runnable outbounds from sing-box JSON configs; skips hy2/tuic/wg/anytls with reasons.
/// </summary>
public static class SingBoxJsonImportParser
{
    public static bool LooksLikeSingBox(string text)
    {
        text = text.TrimStart();
        if (!text.StartsWith('{'))
            return false;
        return text.Contains("\"outbounds\"", StringComparison.Ordinal) &&
               (text.Contains("\"route\"", StringComparison.Ordinal) ||
                text.Contains("\"inbounds\"", StringComparison.Ordinal) ||
                text.Contains("\"type\": \"hysteria2\"", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("\"type\":\"hysteria2\"", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("\"type\": \"vless\"", StringComparison.OrdinalIgnoreCase));
    }

    public static ImportResult Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !LooksLikeSingBox(text))
            return ImportResult.Empty;

        try
        {
            using var doc = JsonDocument.Parse(text);
            if (!doc.RootElement.TryGetProperty("outbounds", out var outbounds) ||
                outbounds.ValueKind != JsonValueKind.Array)
                return ImportResult.Empty;

            var servers = new List<ProxyServer>();
            var skips = new List<string>();
            var skipCount = 0;

            foreach (var item in outbounds.EnumerateArray())
            {
                var type = GetString(item, "type")?.ToLowerInvariant() ?? "";
                switch (type)
                {
                    case "direct":
                    case "block":
                    case "dns":
                    case "selector":
                    case "urltest":
                    case "compatible":
                        continue;
                    case "hysteria2":
                    case "hysteria":
                    {
                        var mapped = MapOutbound(item, "hysteria2");
                        if (mapped is not null)
                            servers.Add(mapped);
                        break;
                    }
                    case "tuic":
                    {
                        var mapped = MapOutbound(item, "tuic");
                        if (mapped is not null)
                            servers.Add(mapped);
                        break;
                    }
                    case "wireguard":
                    {
                        var mapped = MapOutbound(item, "wireguard");
                        if (mapped is not null)
                            servers.Add(mapped);
                        break;
                    }
                    case "anytls":
                    {
                        var mapped = MapOutbound(item, "anytls");
                        if (mapped is not null)
                            servers.Add(mapped);
                        break;
                    }
                    case "vless":
                    case "vmess":
                    case "trojan":
                    case "shadowsocks":
                    case "socks":
                    {
                        var mapped = MapOutbound(item, type);
                        if (mapped is not null)
                            servers.Add(mapped);
                        break;
                    }
                    default:
                        if (!string.IsNullOrEmpty(type))
                        {
                            skipCount++;
                            AddUnique(skips, $"sing-box type '{type}' not mapped to Xray");
                        }

                        break;
                }
            }

            return ImportResult.FromServers(servers, skips, skipCount);
        }
        catch (JsonException)
        {
            return ImportResult.Empty;
        }
    }

    private static ProxyServer? MapOutbound(JsonElement item, string type)
    {
        var serverName = GetString(item, "tag") ?? type;
        var address = GetString(item, "server");
        var port = GetInt(item, "server_port");
        if (string.IsNullOrWhiteSpace(address) || port <= 0)
            return null;

        var server = new ProxyServer
        {
            Name = serverName,
            Address = address,
            Port = port,
            RawLink = $"singbox://{type}/{address}:{port}"
        };

        switch (type)
        {
            case "vless":
                server.Protocol = ProxyProtocol.VLESS;
                server.UserId = GetString(item, "uuid") ?? "";
                server.Flow = GetString(item, "flow") ?? "";
                server.PacketEncoding = GetString(item, "packet_encoding") ?? "";
                break;
            case "vmess":
                server.Protocol = ProxyProtocol.VMess;
                server.UserId = GetString(item, "uuid") ?? "";
                server.AlterId = GetInt(item, "alter_id");
                server.Cipher = GetString(item, "security") ?? "auto";
                server.PacketEncoding = GetString(item, "packet_encoding") ?? "";
                break;
            case "trojan":
                server.Protocol = ProxyProtocol.Trojan;
                server.Password = GetString(item, "password") ?? "";
                break;
            case "shadowsocks":
                server.Protocol = ProxyProtocol.Shadowsocks;
                server.Cipher = GetString(item, "method") ?? "";
                server.Password = GetString(item, "password") ?? "";
                if (item.TryGetProperty("plugin", out _))
                    return null;
                break;
            case "socks":
                server.Protocol = ProxyProtocol.Socks;
                server.UserId = GetString(item, "username") ?? "";
                server.Password = GetString(item, "password") ?? "";
                break;
            case "hysteria2":
                server.Protocol = ProxyProtocol.Hysteria2;
                server.Password = GetString(item, "password") ?? "";
                break;
            case "tuic":
                server.Protocol = ProxyProtocol.Tuic;
                server.UserId = GetString(item, "uuid") ?? "";
                server.Password = GetString(item, "password") ?? "";
                server.Mode = GetString(item, "congestion_control") ?? "bbr";
                break;
            case "wireguard":
                server.Protocol = ProxyProtocol.WireGuard;
                server.Password = GetString(item, "private_key") ?? "";
                if (item.TryGetProperty("peers", out var peers) && peers.ValueKind == JsonValueKind.Array &&
                    peers.GetArrayLength() > 0)
                {
                    server.PublicKey = GetString(peers[0], "public_key") ?? "";
                    server.Address = GetString(peers[0], "server") ?? server.Address;
                    server.Port = GetInt(peers[0], "server_port") is > 0 and var p ? p : server.Port;
                }
                break;
            case "anytls":
                server.Protocol = ProxyProtocol.AnyTls;
                server.Password = GetString(item, "password") ?? "";
                break;
        }

        if (server.Protocol is ProxyProtocol.Hysteria2 or ProxyProtocol.Tuic or ProxyProtocol.AnyTls)
            ApplyTransport(server, item);
        else if (server.Protocol is not ProxyProtocol.WireGuard)
            ApplyTransport(server, item);

        ShareLinkParser.NormalizeVisionFlow(server);
        return server;
    }

    private static void ApplyTransport(ProxyServer server, JsonElement item)
    {
        if (item.TryGetProperty("transport", out var transport) && transport.ValueKind == JsonValueKind.Object)
        {
            var t = GetString(transport, "type") ?? "tcp";
            server.Network = ShareLinkParser.NormalizeNetwork(t);
            server.Path = GetString(transport, "path") ?? "";
            server.Host = GetString(transport, "host") ?? FirstHostFromArray(transport) ?? "";
            server.ServiceName = GetString(transport, "service_name") ?? "";
            if (GetInt(transport, "max_early_data") > 0)
                server.MaxEarlyData = GetInt(transport, "max_early_data");
            server.EarlyDataHeaderName = GetString(transport, "early_data_header_name") ?? "";
        }

        if (item.TryGetProperty("tls", out var tls) && tls.ValueKind == JsonValueKind.Object)
        {
            server.Security = "tls";
            server.Sni = GetString(tls, "server_name") ?? "";
            server.Alpn = JoinAlpn(tls);
            if (tls.TryGetProperty("utls", out var utls) && utls.ValueKind == JsonValueKind.Object)
                server.Fingerprint = GetString(utls, "fingerprint") ?? "chrome";
            if (tls.TryGetProperty("reality", out var reality) && reality.ValueKind == JsonValueKind.Object)
            {
                server.Security = "reality";
                server.PublicKey = GetString(reality, "public_key") ?? "";
                server.ShortId = GetString(reality, "short_id") ?? "";
            }

            if (tls.TryGetProperty("insecure", out var insecure) &&
                insecure.ValueKind == JsonValueKind.True)
                server.AllowInsecure = true;
        }
    }

    private static string? FirstHostFromArray(JsonElement transport)
    {
        if (!transport.TryGetProperty("headers", out var headers) || headers.ValueKind != JsonValueKind.Object)
            return null;
        if (headers.TryGetProperty("Host", out var host))
        {
            if (host.ValueKind == JsonValueKind.String)
                return host.GetString();
            if (host.ValueKind == JsonValueKind.Array && host.GetArrayLength() > 0)
                return host[0].GetString();
        }

        return null;
    }

    private static string JoinAlpn(JsonElement tls)
    {
        if (!tls.TryGetProperty("alpn", out var alpn) || alpn.ValueKind != JsonValueKind.Array)
            return "";
        return string.Join(",", alpn.EnumerateArray().Select(e => e.GetString()).Where(s => !string.IsNullOrEmpty(s)));
    }

    private static string? GetString(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static int GetInt(JsonElement el, string name)
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

    private static void AddUnique(List<string> list, string item)
    {
        if (!list.Contains(item))
            list.Add(item);
    }
}
