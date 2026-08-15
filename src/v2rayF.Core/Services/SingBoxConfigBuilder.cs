using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using v2rayF.Models;

namespace v2rayF.Services;

/// <summary>Builds a minimal sing-box config with mixed SOCKS/HTTP on the same ports as Xray live mode.</summary>
public static class SingBoxConfigBuilder
{
    public static int SocksPort => XrayConfigBuilder.SocksPort;
    public static int HttpPort => XrayConfigBuilder.HttpPort;

    public static string Build(
        ProxyServer server,
        AppSettings settings,
        int? socksPort = null)
    {
        var listen = socksPort ?? SocksPort;
        var outbound = BuildOutbound(server);
        var root = new JsonObject
        {
            ["log"] = new JsonObject { ["level"] = "warn" },
            ["inbounds"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "mixed",
                    ["tag"] = "mixed-in",
                    ["listen"] = "127.0.0.1",
                    ["listen_port"] = listen
                }
            },
            ["outbounds"] = new JsonArray
            {
                outbound,
                new JsonObject { ["type"] = "direct", ["tag"] = "direct" },
                new JsonObject { ["type"] = "block", ["tag"] = "block" }
            },
            ["route"] = new JsonObject
            {
                ["final"] = "proxy",
                ["auto_detect_interface"] = true
            }
        };

        if (settings.BlockIpv6)
        {
            var rules = new JsonArray
            {
                new JsonObject
                {
                    ["ip_is_private"] = true,
                    ["outbound"] = "direct"
                }
            };
            root["route"]!["rules"] = rules;
        }

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public static string BuildSpeedtest(ProxyServer server, int socksPort) =>
        Build(server, new AppSettings { DnsThroughProxy = false }, socksPort);

    private static JsonObject BuildOutbound(ProxyServer server)
    {
        return server.Protocol switch
        {
            ProxyProtocol.Hysteria2 => BuildHysteria2(server),
            ProxyProtocol.Tuic => BuildTuic(server),
            ProxyProtocol.WireGuard => BuildWireGuard(server),
            ProxyProtocol.AnyTls => BuildAnyTls(server),
            _ => throw new InvalidOperationException(
                $"Protocol {server.Protocol} is not a sing-box outbound in this builder.")
        };
    }

    private static JsonObject BuildHysteria2(ProxyServer server)
    {
        var o = new JsonObject
        {
            ["type"] = "hysteria2",
            ["tag"] = "proxy",
            ["server"] = server.Address,
            ["server_port"] = server.Port,
            ["password"] = server.Password
        };
        if (!string.IsNullOrWhiteSpace(server.Path))
            o["obfs"] = new JsonObject
            {
                ["type"] = "salamander",
                ["password"] = server.Path
            };
        o["tls"] = BuildTls(server);
        return o;
    }

    private static JsonObject BuildTuic(ProxyServer server)
    {
        var o = new JsonObject
        {
            ["type"] = "tuic",
            ["tag"] = "proxy",
            ["server"] = server.Address,
            ["server_port"] = server.Port,
            ["uuid"] = string.IsNullOrWhiteSpace(server.UserId) ? server.Password : server.UserId,
            ["password"] = server.Password,
            ["congestion_control"] = string.IsNullOrWhiteSpace(server.Mode) ? "bbr" : server.Mode
        };
        o["tls"] = BuildTls(server);
        return o;
    }

    private static JsonObject BuildWireGuard(ProxyServer server)
    {
        // Private key in Password; peer public key in PublicKey; optional reserved in ShortId.
        var peer = new JsonObject
        {
            ["server"] = server.Address,
            ["server_port"] = server.Port,
            ["public_key"] = server.PublicKey,
            ["allowed_ips"] = new JsonArray { "0.0.0.0/0", "::/0" }
        };
        if (!string.IsNullOrWhiteSpace(server.ShortId))
        {
            try
            {
                var parts = server.ShortId.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var arr = new JsonArray();
                foreach (var p in parts)
                    if (int.TryParse(p, out var n))
                        arr.Add(n);
                if (arr.Count > 0)
                    peer["reserved"] = arr;
            }
            catch
            {
                // ignore
            }
        }

        return new JsonObject
        {
            ["type"] = "wireguard",
            ["tag"] = "proxy",
            ["private_key"] = server.Password,
            ["local_address"] = BuildLocalAddress(server),
            ["peers"] = new JsonArray { peer },
            ["mtu"] = 1400
        };
    }

    private static JsonArray BuildLocalAddress(ProxyServer server)
    {
        // Path may hold comma-separated local addresses; default common CGNAT-style.
        if (!string.IsNullOrWhiteSpace(server.Path) && server.Path.Contains('/'))
        {
            var arr = new JsonArray();
            foreach (var part in server.Path.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                arr.Add(part);
            return arr;
        }

        return new JsonArray { "10.0.0.2/32" };
    }

    private static JsonObject BuildAnyTls(ProxyServer server)
    {
        var o = new JsonObject
        {
            ["type"] = "anytls",
            ["tag"] = "proxy",
            ["server"] = server.Address,
            ["server_port"] = server.Port,
            ["password"] = server.Password
        };
        o["tls"] = BuildTls(server);
        return o;
    }

    private static JsonObject BuildTls(ProxyServer server)
    {
        var tls = new JsonObject
        {
            ["enabled"] = true,
            ["server_name"] = string.IsNullOrWhiteSpace(server.Sni) ? server.Address : server.Sni,
            ["insecure"] = server.AllowInsecure
        };
        if (!string.IsNullOrWhiteSpace(server.Alpn))
        {
            var alpn = new JsonArray();
            foreach (var a in server.Alpn.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                alpn.Add(a);
            tls["alpn"] = alpn;
        }

        if (!string.IsNullOrWhiteSpace(server.Fingerprint) &&
            !server.Fingerprint.Equals("chrome", StringComparison.OrdinalIgnoreCase))
        {
            tls["utls"] = new JsonObject
            {
                ["enabled"] = true,
                ["fingerprint"] = server.Fingerprint
            };
        }

        return tls;
    }
}
