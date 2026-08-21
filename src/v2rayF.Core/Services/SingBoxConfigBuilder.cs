using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using v2rayF.Models;

namespace v2rayF.Services;

/// <summary>
/// Builds sing-box JSON: mixed SOCKS/HTTP (same ports as Xray live) and optional Android TUN via inherited fd 3.
/// </summary>
public static class SingBoxConfigBuilder
{
    public static int SocksPort => XrayConfigBuilder.SocksPort;
    public static int HttpPort => XrayConfigBuilder.HttpPort;
    /// <summary>Matches AndroidJavaCoreProcessHost InheritedTunFd (posix_spawn dup2).</summary>
    public const int InheritedTunFd = 3;

    public static string Build(
        ProxyServer server,
        AppSettings settings,
        int? socksPort = null,
        int? tunFd = null)
    {
        var listen = socksPort ?? SocksPort;
        var outbound = BuildOutbound(server);
        var inbounds = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "mixed",
                ["tag"] = "mixed-in",
                ["listen"] = "127.0.0.1",
                ["listen_port"] = listen
            }
        };

        // Chromium SetHttpProxy targets 10809; keep a second mixed listener for parity with Xray.
        if (listen != HttpPort)
        {
            inbounds.Add(new JsonObject
            {
                ["type"] = "mixed",
                ["tag"] = "mixed-http",
                ["listen"] = "127.0.0.1",
                ["listen_port"] = HttpPort
            });
        }

        if (tunFd is int fd && fd >= 0)
        {
            inbounds.Add(new JsonObject
            {
                ["type"] = "tun",
                ["tag"] = "tun-in",
                ["interface_name"] = TunConstants.InterfaceName,
                ["mtu"] = XrayConfigBuilder.AndroidTunMtu,
                ["inet4_address"] = new JsonArray { "172.19.0.1/30" },
                ["auto_route"] = false,
                ["strict_route"] = false,
                ["stack"] = "system",
                ["file_descriptor"] = InheritedTunFd,
                ["sniff"] = true,
                ["sniff_override_destination"] = false
            });
        }

        var root = new JsonObject
        {
            ["log"] = new JsonObject { ["level"] = "warn" },
            ["inbounds"] = inbounds,
            ["outbounds"] = new JsonArray
            {
                outbound,
                new JsonObject { ["type"] = "direct", ["tag"] = "direct" },
                new JsonObject { ["type"] = "block", ["tag"] = "block" }
            },
            ["route"] = BuildRoute(settings),
            ["dns"] = BuildDns(settings, server)
        };

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public static string BuildSpeedtest(ProxyServer server, int socksPort) =>
        Build(server, new AppSettings(), socksPort);

    private static JsonObject BuildRoute(AppSettings settings)
    {
        var rules = new JsonArray();
        if (settings.BlockIpv6)
        {
            rules.Add(new JsonObject
            {
                ["ip_version"] = 6,
                ["outbound"] = "block"
            });
        }

        rules.Add(new JsonObject
        {
            ["ip_is_private"] = true,
            ["outbound"] = "direct"
        });

        return new JsonObject
        {
            ["rules"] = rules,
            ["final"] = "proxy",
            ["auto_detect_interface"] = true
        };
    }

    private static JsonObject BuildDns(AppSettings settings, ProxyServer server)
    {
        var servers = new JsonArray();
        // Bootstrap: resolve outbound host via UDP (avoids DoH chicken-and-egg).
        if (!string.IsNullOrWhiteSpace(server.Address) &&
            !System.Net.IPAddress.TryParse(server.Address, out _))
        {
            servers.Add(new JsonObject
            {
                ["tag"] = "bootstrap",
                ["address"] = "1.1.1.1",
                ["detour"] = "direct",
                ["domain"] = new JsonArray { server.Address }
            });
        }

        if (settings.DnsThroughProxy)
        {
            servers.Add(new JsonObject
            {
                ["tag"] = "doh",
                ["address"] = "https://1.1.1.1/dns-query",
                ["detour"] = "direct"
            });
            servers.Add(new JsonObject
            {
                ["tag"] = "doh-google",
                ["address"] = "https://8.8.8.8/dns-query",
                ["detour"] = "direct"
            });
        }
        else
        {
            servers.Add(new JsonObject
            {
                ["tag"] = "udp",
                ["address"] = "1.1.1.1",
                ["detour"] = "direct"
            });
        }

        var dns = new JsonObject
        {
            ["servers"] = servers,
            ["strategy"] = settings.BlockIpv6 ? "ipv4_only" : "prefer_ipv4",
            ["final"] = settings.DnsThroughProxy ? "doh" : "udp"
        };
        return dns;
    }

    private static JsonObject BuildOutbound(ProxyServer server)
    {
        return server.Protocol switch
        {
            ProxyProtocol.Hysteria2 => BuildHysteria2(server),
            ProxyProtocol.Tuic => BuildTuic(server),
            ProxyProtocol.WireGuard => BuildWireGuard(server),
            ProxyProtocol.AnyTls => BuildAnyTls(server),
            ProxyProtocol.VLESS => BuildVless(server),
            ProxyProtocol.VMess => BuildVmess(server),
            ProxyProtocol.Trojan => BuildTrojan(server),
            ProxyProtocol.Shadowsocks => BuildShadowsocks(server),
            _ => throw new InvalidOperationException(
                $"Protocol {server.Protocol} is not a sing-box outbound in this builder.")
        };
    }

    private static JsonObject BuildVless(ProxyServer server)
    {
        ShareLinkParser.NormalizeVisionFlow(server);
        var o = new JsonObject
        {
            ["type"] = "vless",
            ["tag"] = "proxy",
            ["server"] = server.Address,
            ["server_port"] = server.Port,
            ["uuid"] = server.UserId
        };
        if (!string.IsNullOrWhiteSpace(server.Flow))
            o["flow"] = server.Flow;
        if (!string.IsNullOrWhiteSpace(server.PacketEncoding))
            o["packet_encoding"] = server.PacketEncoding;
        ApplyTlsAndTransport(o, server);
        return o;
    }

    private static JsonObject BuildVmess(ProxyServer server)
    {
        var o = new JsonObject
        {
            ["type"] = "vmess",
            ["tag"] = "proxy",
            ["server"] = server.Address,
            ["server_port"] = server.Port,
            ["uuid"] = server.UserId,
            ["security"] = string.IsNullOrWhiteSpace(server.Cipher) ? "auto" : server.Cipher,
            ["alter_id"] = server.AlterId
        };
        if (!string.IsNullOrWhiteSpace(server.PacketEncoding))
            o["packet_encoding"] = server.PacketEncoding;
        ApplyTlsAndTransport(o, server);
        return o;
    }

    private static JsonObject BuildTrojan(ProxyServer server)
    {
        var o = new JsonObject
        {
            ["type"] = "trojan",
            ["tag"] = "proxy",
            ["server"] = server.Address,
            ["server_port"] = server.Port,
            ["password"] = server.Password
        };
        ApplyTlsAndTransport(o, server);
        return o;
    }

    private static JsonObject BuildShadowsocks(ProxyServer server)
    {
        return new JsonObject
        {
            ["type"] = "shadowsocks",
            ["tag"] = "proxy",
            ["server"] = server.Address,
            ["server_port"] = server.Port,
            ["method"] = string.IsNullOrWhiteSpace(server.Cipher) ? "aes-128-gcm" : server.Cipher,
            ["password"] = server.Password
        };
    }

    private static void ApplyTlsAndTransport(JsonObject outbound, ProxyServer server)
    {
        var security = ShareLinkParser.NormalizeSecurity(server.Security);
        if (security is "tls" or "reality")
            outbound["tls"] = BuildTls(server, security == "reality");

        var network = ShareLinkParser.NormalizeNetwork(server.Network);
        if (network is "ws" or "websocket")
        {
            var ws = new JsonObject
            {
                ["type"] = "ws",
                ["path"] = string.IsNullOrWhiteSpace(server.Path) ? "/" : server.Path
            };
            if (!string.IsNullOrWhiteSpace(server.Host))
                ws["headers"] = new JsonObject { ["Host"] = server.Host };
            if (server.MaxEarlyData > 0)
            {
                ws["max_early_data"] = server.MaxEarlyData;
                if (!string.IsNullOrWhiteSpace(server.EarlyDataHeaderName))
                    ws["early_data_header_name"] = server.EarlyDataHeaderName;
            }

            outbound["transport"] = ws;
        }
        else if (network is "grpc")
        {
            outbound["transport"] = new JsonObject
            {
                ["type"] = "grpc",
                ["service_name"] = string.IsNullOrWhiteSpace(server.ServiceName) ? "" : server.ServiceName
            };
        }
        else if (network is "httpupgrade")
        {
            var hu = new JsonObject
            {
                ["type"] = "httpupgrade",
                ["path"] = string.IsNullOrWhiteSpace(server.Path) ? "/" : server.Path
            };
            if (!string.IsNullOrWhiteSpace(server.Host))
                hu["host"] = server.Host;
            outbound["transport"] = hu;
        }
        else if (network is "h2" or "http")
        {
            var h2 = new JsonObject { ["type"] = "http" };
            if (!string.IsNullOrWhiteSpace(server.Path))
                h2["path"] = server.Path;
            if (!string.IsNullOrWhiteSpace(server.Host))
                h2["host"] = new JsonArray { server.Host };
            outbound["transport"] = h2;
        }
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
        if (server.UpMbps > 0)
            o["up_mbps"] = server.UpMbps;
        if (server.DownMbps > 0)
            o["down_mbps"] = server.DownMbps;
        if (!string.IsNullOrWhiteSpace(server.Path))
            o["obfs"] = new JsonObject
            {
                ["type"] = "salamander",
                ["password"] = server.Path
            };
        o["tls"] = BuildTls(server, reality: false);
        return o;
    }

    private static JsonObject BuildTuic(ProxyServer server)
    {
        var congestion = string.IsNullOrWhiteSpace(server.Mode) ? "bbr" : server.Mode;
        if (congestion is "native" or "quic")
            congestion = "bbr";

        var o = new JsonObject
        {
            ["type"] = "tuic",
            ["tag"] = "proxy",
            ["server"] = server.Address,
            ["server_port"] = server.Port,
            ["uuid"] = string.IsNullOrWhiteSpace(server.UserId) ? server.Password : server.UserId,
            ["password"] = server.Password,
            ["congestion_control"] = congestion
        };
        if (!string.IsNullOrWhiteSpace(server.UdpRelayMode))
            o["udp_relay_mode"] = server.UdpRelayMode.Trim();
        o["tls"] = BuildTls(server, reality: false);
        return o;
    }

    private static JsonObject BuildWireGuard(ProxyServer server)
    {
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
            ["mtu"] = server.Mtu > 0 ? server.Mtu : 1400
        };
    }

    private static JsonArray BuildLocalAddress(ProxyServer server)
    {
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
        o["tls"] = BuildTls(server, reality: false);
        return o;
    }

    private static JsonObject BuildTls(ProxyServer server, bool reality)
    {
        var tls = new JsonObject
        {
            ["enabled"] = true,
            ["server_name"] = string.IsNullOrWhiteSpace(server.Sni)
                ? (string.IsNullOrWhiteSpace(server.Host) ? server.Address : server.Host)
                : server.Sni,
            ["insecure"] = server.AllowInsecure
        };
        if (!string.IsNullOrWhiteSpace(server.Alpn))
        {
            var alpn = new JsonArray();
            foreach (var a in server.Alpn.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                alpn.Add(a);
            tls["alpn"] = alpn;
        }

        if (!string.IsNullOrWhiteSpace(server.Fingerprint))
        {
            tls["utls"] = new JsonObject
            {
                ["enabled"] = true,
                ["fingerprint"] = server.Fingerprint
            };
        }

        if (reality)
        {
            tls["reality"] = new JsonObject
            {
                ["enabled"] = true,
                ["public_key"] = server.PublicKey,
                ["short_id"] = string.IsNullOrWhiteSpace(server.ShortId) ? "" : server.ShortId
            };
        }

        return tls;
    }
}
