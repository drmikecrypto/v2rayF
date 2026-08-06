using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using v2rayF.Models;

namespace v2rayF.Services;

public static class XrayConfigBuilder
{
    public const int SocksPort = 10808;
    public const int HttpPort = 10809;
    public const int SpeedtestSocksPort = 10818;
    public const int DefaultSharePort = 10880;

    private static readonly JsonSerializerOptions CompactJson = new() { WriteIndented = false };

    public static string Build(
        ProxyServer server,
        AppSettings settings,
        int? tunFd = null,
        IReadOnlyList<ProxyServer>? multipathServers = null)
    {
        var peers = NormalizePeers(server, multipathServers, settings.SmartMultipathEnabled);
        var useBalancer = peers.Count > 1;

        var inbounds = new JsonArray
        {
            BuildLocalSocksInbound("socks-in", SocksPort, "127.0.0.1"),
            BuildLocalHttpInbound("http-in", HttpPort, "127.0.0.1")
        };

        if (settings.SecureShareEnabled)
        {
            var sharePort = settings.ShareBindPort > 0 ? settings.ShareBindPort : DefaultSharePort;
            EnsureShareCredentials(settings);
            var listen = ResolveShareListenAddress(settings);
            inbounds.Add(BuildShareSocksInbound(sharePort, settings.ShareAuthUser, settings.ShareAuthPass, listen));
            inbounds.Add(BuildShareHttpInbound(sharePort + 1, settings.ShareAuthUser, settings.ShareAuthPass, listen));
        }

        if (settings.EnableTunMode)
        {
            var tunSettings = new JsonObject
            {
                ["name"] = "v2rayF",
                ["MTU"] = 1280,
                ["inet4_address"] = "172.19.0.1/30",
                ["stack"] = "system"
            };

            if (!settings.BlockIpv6)
                tunSettings["inet6_address"] = "fdfe:dcba:9876::1/126";

            if (tunFd is int fd)
            {
                tunSettings["fd"] = fd;
                tunSettings["auto_route"] = false;
            }
            else
            {
                tunSettings["auto_route"] = true;
                tunSettings["strict_route"] = true;
            }

            inbounds.Add(new JsonObject
            {
                ["tag"] = "tun-in",
                ["protocol"] = "tun",
                ["settings"] = tunSettings,
                ["sniffing"] = new JsonObject
                {
                    ["enabled"] = true,
                    ["destOverride"] = new JsonArray { "http", "tls", "quic" },
                    ["routeOnly"] = false
                }
            });
        }

        var outbounds = new JsonArray();

        var useFragmentDialer = settings.EnablePacketFragment &&
                                peers.Any(p => !IsVisionFlow(p));

        if (useFragmentDialer)
        {
            outbounds.Add(new JsonObject
            {
                ["tag"] = "fragment",
                ["protocol"] = "freedom",
                ["settings"] = new JsonObject
                {
                    ["fragment"] = new JsonObject
                    {
                        ["packets"] = "tlshello",
                        ["length"] = "100-200",
                        ["interval"] = "10-20"
                    }
                }
            });
        }

        if (useBalancer)
        {
            for (var i = 0; i < peers.Count; i++)
                outbounds.Add(BuildOutbound(peers[i], $"proxy-{i}", useFragmentDialer));
        }
        else
        {
            outbounds.Add(BuildOutbound(peers[0], "proxy", useFragmentDialer));
        }

        outbounds.Add(new JsonObject { ["tag"] = "direct", ["protocol"] = "freedom" });
        outbounds.Add(new JsonObject { ["tag"] = "block", ["protocol"] = "blackhole" });
        outbounds.Add(new JsonObject
        {
            ["tag"] = "dns-out",
            ["protocol"] = "dns",
            ["settings"] = new JsonObject()
        });

        var config = new JsonObject
        {
            ["log"] = new JsonObject { ["loglevel"] = "warning" },
            ["dns"] = BuildDns(settings),
            ["inbounds"] = inbounds,
            ["outbounds"] = outbounds,
            ["routing"] = BuildRouting(settings, useBalancer)
        };

        if (useBalancer)
        {
            var selector = new JsonArray();
            for (var i = 0; i < peers.Count; i++)
                selector.Add($"proxy-{i}");

            config["burstObservatory"] = new JsonObject
            {
                ["subjectSelector"] = selector.DeepClone(),
                ["pingConfig"] = new JsonObject
                {
                    ["destination"] = "https://www.gstatic.com/generate_204",
                    ["interval"] = "1m",
                    ["sampling"] = 2,
                    ["timeout"] = "5s"
                }
            };

            config["routing"]!["balancers"] = new JsonArray
            {
                new JsonObject
                {
                    ["tag"] = "balancer",
                    ["selector"] = selector,
                    ["strategy"] = new JsonObject { ["type"] = "leastPing" }
                }
            };
        }

        return config.ToJsonString(CompactJson);
    }

    public static string BuildSpeedtest(ProxyServer server, int socksPort = SpeedtestSocksPort)
    {
        var config = new JsonObject
        {
            ["log"] = new JsonObject { ["loglevel"] = "warning" },
            ["inbounds"] = new JsonArray
            {
                new JsonObject
                {
                    ["tag"] = "speedtest-in",
                    ["port"] = socksPort,
                    ["listen"] = "127.0.0.1",
                    ["protocol"] = "socks",
                    ["settings"] = new JsonObject { ["udp"] = false }
                }
            },
            ["outbounds"] = new JsonArray
            {
                BuildOutbound(server, "proxy", enableFragment: false),
                new JsonObject { ["tag"] = "direct", ["protocol"] = "freedom" }
            },
            ["routing"] = new JsonObject
            {
                ["domainStrategy"] = "AsIs",
                ["rules"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "field",
                        ["network"] = "tcp,udp",
                        ["outboundTag"] = "proxy"
                    }
                }
            }
        };

        return config.ToJsonString(CompactJson);
    }

    public static void EnsureShareCredentials(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ShareAuthUser))
            settings.ShareAuthUser = "v2rayf";

        if (string.IsNullOrWhiteSpace(settings.ShareAuthPass))
            settings.ShareAuthPass = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
    }

    /// <summary>Regenerates the Secure Share password (call when user explicitly rotates).</summary>
    public static void RotateSharePassword(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ShareAuthUser))
            settings.ShareAuthUser = "v2rayf";
        settings.ShareAuthPass = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
    }

    public static string ResolveShareListenAddress(AppSettings settings)
    {
        if (settings.ShareListenAllInterfaces)
            return "0.0.0.0";

        var lan = AppServices.Platform?.GetLanIPv4Address();
        return string.IsNullOrWhiteSpace(lan) ? "0.0.0.0" : lan;
    }

    private static List<ProxyServer> NormalizePeers(
        ProxyServer primary,
        IReadOnlyList<ProxyServer>? multipathServers,
        bool multipathEnabled)
    {
        var list = new List<ProxyServer> { primary };
        if (!multipathEnabled || multipathServers is null || multipathServers.Count == 0)
            return list;

        foreach (var peer in multipathServers)
        {
            if (list.Any(s => s.Id == peer.Id))
                continue;
            list.Add(peer);
            if (list.Count >= SmartConnectService.MaxMultipathCandidates)
                break;
        }

        return list;
    }

    private static JsonObject BuildDns(AppSettings settings)
    {
        var dns = new JsonObject
        {
            ["queryStrategy"] = settings.BlockIpv6 ? "UseIPv4" : "UseIP",
            ["tag"] = "dns-module"
        };

        if (settings.DnsThroughProxy)
        {
            // DoH endpoints; traffic tagged dns-module is routed through the proxy.
            dns["servers"] = new JsonArray
            {
                new JsonObject
                {
                    ["address"] = "https://1.1.1.1/dns-query",
                    ["skipFallback"] = false
                },
                new JsonObject
                {
                    ["address"] = "https://8.8.8.8/dns-query",
                    ["skipFallback"] = false
                }
            };
        }
        else
        {
            dns["servers"] = new JsonArray { "1.1.1.1", "8.8.8.8" };
        }

        return dns;
    }

    private static JsonObject BuildRouting(AppSettings settings, bool useBalancer)
    {
        var rules = new JsonArray();

        // Xray DNS module traffic → proxy (or balancer).
        if (settings.DnsThroughProxy)
        {
            rules.Add(MakeOutboundRule(
                useBalancer,
                inboundTag: "dns-module"));
        }

        // App DNS on TUN/local → dns outbound (resolved via dns module above).
        rules.Add(new JsonObject
        {
            ["type"] = "field",
            ["port"] = "53",
            ["network"] = "udp,tcp",
            ["outboundTag"] = "dns-out"
        });

        // Private LAN stays out of Global except loopback (local apps).
        if (settings.RoutingMode == RoutingMode.Global)
        {
            rules.Add(new JsonObject
            {
                ["type"] = "field",
                ["ip"] = new JsonArray { "127.0.0.0/8", "::1/128" },
                ["outboundTag"] = "direct"
            });
        }

        switch (settings.RoutingMode)
        {
            case RoutingMode.BypassLan:
                rules.Add(PrivateLanDirectRule());
                break;

            case RoutingMode.BypassChina:
                rules.Add(PrivateLanDirectRule());
                rules.Add(new JsonObject
                {
                    ["type"] = "field",
                    ["domain"] = new JsonArray { "geosite:cn" },
                    ["outboundTag"] = "direct"
                });
                rules.Add(new JsonObject
                {
                    ["type"] = "field",
                    ["ip"] = new JsonArray { "geoip:cn", "geoip:private" },
                    ["outboundTag"] = "direct"
                });
                break;

            case RoutingMode.CustomDirect:
                AppendCustomRules(rules, settings.CustomBlockRules, "block");
                AppendCustomRules(rules, settings.CustomProxyRules, useBalancer ? null : "proxy", useBalancer ? "balancer" : null);
                AppendCustomRules(rules, settings.CustomDirectRules, "direct");
                rules.Add(PrivateLanDirectRule());
                break;

            case RoutingMode.Global:
            default:
                break;
        }

        if (useBalancer)
        {
            rules.Add(new JsonObject
            {
                ["type"] = "field",
                ["network"] = "tcp,udp",
                ["balancerTag"] = "balancer"
            });
        }
        else
        {
            rules.Add(new JsonObject
            {
                ["type"] = "field",
                ["network"] = "tcp,udp",
                ["outboundTag"] = "proxy"
            });
        }

        return new JsonObject
        {
            ["domainStrategy"] = settings.RoutingMode == RoutingMode.BypassChina ? "IPIfNonMatch" : "AsIs",
            ["rules"] = rules
        };
    }

    private static JsonObject MakeOutboundRule(bool useBalancer, string inboundTag)
    {
        if (useBalancer)
        {
            return new JsonObject
            {
                ["type"] = "field",
                ["inboundTag"] = new JsonArray { inboundTag },
                ["balancerTag"] = "balancer"
            };
        }

        return new JsonObject
        {
            ["type"] = "field",
            ["inboundTag"] = new JsonArray { inboundTag },
            ["outboundTag"] = "proxy"
        };
    }

    private static JsonObject PrivateLanDirectRule() => new()
    {
        ["type"] = "field",
        ["ip"] = new JsonArray
        {
            "10.0.0.0/8",
            "172.16.0.0/12",
            "192.168.0.0/16",
            "127.0.0.0/8",
            "169.254.0.0/16",
            "224.0.0.0/4",
            "240.0.0.0/4",
            "fc00::/7",
            "fe80::/10",
            "::1/128"
        },
        ["outboundTag"] = "direct"
    };

    private static void AppendCustomRules(
        JsonArray rules,
        string raw,
        string? outboundTag,
        string? balancerTag = null)
    {
        foreach (var entry in ParseCustomRules(raw))
        {
            JsonObject rule;
            if (entry.StartsWith("full:", StringComparison.Ordinal) ||
                entry.StartsWith("domain:", StringComparison.Ordinal) ||
                entry.StartsWith("regexp:", StringComparison.Ordinal) ||
                (entry.Contains('.') && !entry.Contains('/')))
            {
                var domain = entry.Contains(':') ? entry : $"domain:{entry}";
                rule = new JsonObject
                {
                    ["type"] = "field",
                    ["domain"] = new JsonArray { domain }
                };
            }
            else
            {
                rule = new JsonObject
                {
                    ["type"] = "field",
                    ["ip"] = new JsonArray { entry }
                };
            }

            if (!string.IsNullOrEmpty(balancerTag))
                rule["balancerTag"] = balancerTag;
            else
                rule["outboundTag"] = outboundTag;

            rules.Add(rule);
        }
    }

    private static IEnumerable<string> ParseCustomRules(string rules) =>
        string.IsNullOrWhiteSpace(rules)
            ? []
            : rules.Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static JsonObject BuildLocalSocksInbound(string tag, int port, string listen) => new()
    {
        ["tag"] = tag,
        ["port"] = port,
        ["listen"] = listen,
        ["protocol"] = "socks",
        ["settings"] = new JsonObject { ["udp"] = true }
    };

    private static JsonObject BuildLocalHttpInbound(string tag, int port, string listen) => new()
    {
        ["tag"] = tag,
        ["port"] = port,
        ["listen"] = listen,
        ["protocol"] = "http"
    };

    private static JsonObject BuildShareSocksInbound(int port, string user, string pass, string listen) => new()
    {
        ["tag"] = "share-socks",
        ["port"] = port,
        ["listen"] = listen,
        ["protocol"] = "socks",
        ["settings"] = new JsonObject
        {
            ["udp"] = true,
            ["auth"] = "password",
            ["accounts"] = new JsonArray
            {
                new JsonObject
                {
                    ["user"] = user,
                    ["pass"] = pass
                }
            }
        },
        ["sniffing"] = new JsonObject
        {
            ["enabled"] = true,
            ["destOverride"] = new JsonArray { "http", "tls" }
        }
    };

    private static JsonObject BuildShareHttpInbound(int port, string user, string pass, string listen) => new()
    {
        ["tag"] = "share-http",
        ["port"] = port,
        ["listen"] = listen,
        ["protocol"] = "http",
        ["settings"] = new JsonObject
        {
            ["accounts"] = new JsonArray
            {
                new JsonObject
                {
                    ["user"] = user,
                    ["pass"] = pass
                }
            },
            ["allowTransparent"] = false
        }
    };

    private static JsonObject BuildOutbound(ProxyServer server, string tag, bool enableFragment)
    {
        var outbound = server.Protocol switch
        {
            ProxyProtocol.VMess => BuildVmessOutbound(server, tag),
            ProxyProtocol.VLESS => BuildVlessOutbound(server, tag),
            ProxyProtocol.Shadowsocks => BuildShadowsocksOutbound(server, tag),
            ProxyProtocol.Trojan => BuildTrojanOutbound(server, tag),
            ProxyProtocol.Socks => BuildSocksOutbound(server, tag),
            _ => throw new NotSupportedException($"Protocol {server.Protocol} is not supported.")
        };

        if (enableFragment &&
            !IsVisionFlow(server) &&
            outbound["streamSettings"] is JsonObject stream)
        {
            stream["sockopt"] = new JsonObject
            {
                ["dialerProxy"] = "fragment"
            };
        }

        return outbound;
    }

    private static bool IsVisionFlow(ProxyServer server) =>
        !string.IsNullOrWhiteSpace(server.Flow) &&
        server.Flow.Contains("vision", StringComparison.OrdinalIgnoreCase);

    private static JsonObject BuildVmessOutbound(ProxyServer server, string tag)
    {
        var outbound = new JsonObject
        {
            ["tag"] = tag,
            ["protocol"] = "vmess",
            ["settings"] = new JsonObject
            {
                ["vnext"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["address"] = server.Address,
                        ["port"] = server.Port,
                        ["users"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["id"] = server.UserId,
                                ["alterId"] = server.AlterId,
                                ["security"] = string.IsNullOrWhiteSpace(server.Cipher) ? "auto" : server.Cipher
                            }
                        }
                    }
                }
            }
        };

        outbound["streamSettings"] = BuildStreamSettings(server);
        return outbound;
    }

    private static JsonObject BuildVlessOutbound(ProxyServer server, string tag)
    {
        ShareLinkParser.NormalizeVisionFlow(server);

        var user = new JsonObject
        {
            ["id"] = server.UserId,
            ["encryption"] = string.IsNullOrWhiteSpace(server.Encryption) ? "none" : server.Encryption
        };

        if (!string.IsNullOrWhiteSpace(server.Flow))
            user["flow"] = server.Flow;

        return new JsonObject
        {
            ["tag"] = tag,
            ["protocol"] = "vless",
            ["settings"] = new JsonObject
            {
                ["vnext"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["address"] = server.Address,
                        ["port"] = server.Port,
                        ["users"] = new JsonArray { user }
                    }
                }
            },
            ["streamSettings"] = BuildStreamSettings(server)
        };
    }

    private static JsonObject BuildShadowsocksOutbound(ProxyServer server, string tag) => new()
    {
        ["tag"] = tag,
        ["protocol"] = "shadowsocks",
        ["settings"] = new JsonObject
        {
            ["servers"] = new JsonArray
            {
                new JsonObject
                {
                    ["address"] = server.Address,
                    ["port"] = server.Port,
                    ["method"] = server.Cipher,
                    ["password"] = server.Password
                }
            }
        }
    };

    private static JsonObject BuildTrojanOutbound(ProxyServer server, string tag) => new()
    {
        ["tag"] = tag,
        ["protocol"] = "trojan",
        ["settings"] = new JsonObject
        {
            ["servers"] = new JsonArray
            {
                new JsonObject
                {
                    ["address"] = server.Address,
                    ["port"] = server.Port,
                    ["password"] = server.Password
                }
            }
        },
        ["streamSettings"] = BuildStreamSettings(server)
    };

    private static JsonObject BuildSocksOutbound(ProxyServer server, string tag)
    {
        var socksServer = new JsonObject
        {
            ["address"] = server.Address,
            ["port"] = server.Port
        };

        if (!string.IsNullOrWhiteSpace(server.UserId))
        {
            socksServer["users"] = new JsonArray
            {
                new JsonObject
                {
                    ["user"] = server.UserId,
                    ["pass"] = server.Password
                }
            };
        }

        return new JsonObject
        {
            ["tag"] = tag,
            ["protocol"] = "socks",
            ["settings"] = new JsonObject
            {
                ["servers"] = new JsonArray { socksServer }
            }
        };
    }

    /// <summary>
    /// Builds Xray streamSettings for TCP/WS/gRPC/H2/HTTPUpgrade/xHTTP/KCP/QUIC
    /// with none / TLS / REALITY (including Vision+REALITY and Vision+TLS).
    /// </summary>
    public static JsonObject BuildStreamSettings(ProxyServer server)
    {
        var network = ShareLinkParser.NormalizeNetwork(server.Network);
        var security = ShareLinkParser.NormalizeSecurity(server.Security);

        var stream = new JsonObject
        {
            ["network"] = network
        };

        ApplySecuritySettings(stream, server, security);
        ApplyTransportSettings(stream, server, network);
        return stream;
    }

    private static void ApplySecuritySettings(JsonObject stream, ProxyServer server, string security)
    {
        switch (security)
        {
            case "tls":
                stream["security"] = "tls";
                stream["tlsSettings"] = BuildTlsSettings(server);
                break;

            case "reality":
                stream["security"] = "reality";
                stream["realitySettings"] = BuildRealitySettings(server);
                break;

            default:
                stream["security"] = "none";
                break;
        }
    }

    private static JsonObject BuildTlsSettings(ProxyServer server)
    {
        var tls = new JsonObject
        {
            ["serverName"] = string.IsNullOrWhiteSpace(server.Sni) ? server.Address : server.Sni,
            ["allowInsecure"] = server.AllowInsecure,
            ["fingerprint"] = string.IsNullOrWhiteSpace(server.Fingerprint) ? "chrome" : server.Fingerprint
        };

        var alpn = SplitAlpn(server.Alpn);
        if (alpn.Count > 0)
            tls["alpn"] = alpn;

        return tls;
    }

    private static JsonObject BuildRealitySettings(ProxyServer server)
    {
        var reality = new JsonObject
        {
            ["serverName"] = string.IsNullOrWhiteSpace(server.Sni) ? server.Address : server.Sni,
            ["fingerprint"] = string.IsNullOrWhiteSpace(server.Fingerprint) ? "chrome" : server.Fingerprint,
            ["publicKey"] = server.PublicKey,
            ["shortId"] = server.ShortId ?? "",
            ["spiderX"] = string.IsNullOrWhiteSpace(server.SpiderX) ? "/" : server.SpiderX
        };

        return reality;
    }

    private static void ApplyTransportSettings(JsonObject stream, ProxyServer server, string network)
    {
        switch (network)
        {
            case "ws":
                stream["wsSettings"] = new JsonObject
                {
                    ["path"] = string.IsNullOrWhiteSpace(server.Path) ? "/" : server.Path,
                    ["headers"] = new JsonObject
                    {
                        ["Host"] = FirstHost(server)
                    }
                };
                break;

            case "grpc":
            {
                var grpc = new JsonObject
                {
                    ["serviceName"] = !string.IsNullOrWhiteSpace(server.ServiceName)
                        ? server.ServiceName
                        : (server.Path ?? "")
                };
                if (server.Mode.Equals("multi", StringComparison.OrdinalIgnoreCase))
                    grpc["multiMode"] = true;

                stream["grpcSettings"] = grpc;
                break;
            }

            case "h2":
                stream["httpSettings"] = new JsonObject
                {
                    ["path"] = string.IsNullOrWhiteSpace(server.Path) ? "/" : server.Path,
                    ["host"] = BuildHostArray(server)
                };
                break;

            case "httpupgrade":
                stream["httpupgradeSettings"] = new JsonObject
                {
                    ["path"] = string.IsNullOrWhiteSpace(server.Path) ? "/" : server.Path,
                    ["host"] = FirstHost(server)
                };
                break;

            case "xhttp":
            {
                var xhttp = new JsonObject
                {
                    ["path"] = string.IsNullOrWhiteSpace(server.Path) ? "/" : server.Path,
                    ["host"] = FirstHost(server)
                };
                if (!string.IsNullOrWhiteSpace(server.Mode))
                    xhttp["mode"] = server.Mode;
                stream["xhttpSettings"] = xhttp;
                break;
            }

            case "kcp":
            {
                var kcp = new JsonObject
                {
                    ["mtu"] = 1350,
                    ["tti"] = 50,
                    ["uplinkCapacity"] = 12,
                    ["downlinkCapacity"] = 100,
                    ["congestion"] = false,
                    ["readBufferSize"] = 2,
                    ["writeBufferSize"] = 2,
                    ["header"] = new JsonObject
                    {
                        ["type"] = string.IsNullOrWhiteSpace(server.HeaderType) ? "none" : server.HeaderType
                    }
                };
                if (!string.IsNullOrWhiteSpace(server.Seed))
                    kcp["seed"] = server.Seed;
                stream["kcpSettings"] = kcp;
                break;
            }

            case "quic":
                stream["quicSettings"] = new JsonObject
                {
                    ["security"] = "none",
                    ["key"] = "",
                    ["header"] = new JsonObject
                    {
                        ["type"] = string.IsNullOrWhiteSpace(server.HeaderType) ? "none" : server.HeaderType
                    }
                };
                break;

            default:
                // TCP / raw — optional HTTP header camouflage
                if (server.HeaderType.Equals("http", StringComparison.OrdinalIgnoreCase))
                {
                    stream["tcpSettings"] = new JsonObject
                    {
                        ["header"] = new JsonObject
                        {
                            ["type"] = "http",
                            ["request"] = new JsonObject
                            {
                                ["path"] = new JsonArray
                                {
                                    string.IsNullOrWhiteSpace(server.Path) ? "/" : server.Path
                                },
                                ["headers"] = new JsonObject
                                {
                                    ["Host"] = BuildHostArray(server)
                                }
                            }
                        }
                    };
                }
                break;
        }
    }

    private static JsonArray SplitAlpn(string alpn)
    {
        var arr = new JsonArray();
        if (string.IsNullOrWhiteSpace(alpn))
            return arr;

        foreach (var part in alpn.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            arr.Add(part);
        return arr;
    }

    private static string FirstHost(ProxyServer server)
    {
        if (!string.IsNullOrWhiteSpace(server.Host))
            return server.Host;
        if (!string.IsNullOrWhiteSpace(server.Sni))
            return server.Sni;
        return server.Address;
    }

    private static JsonArray BuildHostArray(ProxyServer server)
    {
        var host = FirstHost(server);
        return new JsonArray { host };
    }
}
