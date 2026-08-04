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
            inbounds.Add(BuildShareSocksInbound(sharePort, settings.ShareAuthUser, settings.ShareAuthPass));
            inbounds.Add(BuildShareHttpInbound(sharePort + 1, settings.ShareAuthUser, settings.ShareAuthPass));
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

        return config.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
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

        return config.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public static void EnsureShareCredentials(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ShareAuthUser))
            settings.ShareAuthUser = "v2rayf";

        if (string.IsNullOrWhiteSpace(settings.ShareAuthPass))
            settings.ShareAuthPass = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
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

    private static JsonObject BuildShareSocksInbound(int port, string user, string pass) => new()
    {
        ["tag"] = "share-socks",
        ["port"] = port,
        ["listen"] = "0.0.0.0",
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

    private static JsonObject BuildShareHttpInbound(int port, string user, string pass) => new()
    {
        ["tag"] = "share-http",
        ["port"] = port,
        ["listen"] = "0.0.0.0",
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
                                ["security"] = "auto"
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
        var user = new JsonObject
        {
            ["id"] = server.UserId,
            ["encryption"] = "none"
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
        ["streamSettings"] = new JsonObject
        {
            ["network"] = server.Network,
            ["security"] = "tls",
            ["tlsSettings"] = new JsonObject
            {
                ["serverName"] = string.IsNullOrWhiteSpace(server.Sni) ? server.Address : server.Sni,
                ["allowInsecure"] = server.AllowInsecure,
                ["fingerprint"] = string.IsNullOrWhiteSpace(server.Fingerprint) ? "chrome" : server.Fingerprint
            }
        }
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

    private static JsonObject BuildStreamSettings(ProxyServer server)
    {
        var stream = new JsonObject
        {
            ["network"] = string.IsNullOrWhiteSpace(server.Network) ? "tcp" : server.Network
        };

        switch (server.Security?.ToLowerInvariant())
        {
            case "tls":
                stream["security"] = "tls";
                stream["tlsSettings"] = new JsonObject
                {
                    ["serverName"] = string.IsNullOrWhiteSpace(server.Sni) ? server.Address : server.Sni,
                    ["allowInsecure"] = server.AllowInsecure,
                    ["fingerprint"] = string.IsNullOrWhiteSpace(server.Fingerprint) ? "chrome" : server.Fingerprint
                };
                break;

            case "reality":
                stream["security"] = "reality";
                stream["realitySettings"] = new JsonObject
                {
                    ["serverName"] = string.IsNullOrWhiteSpace(server.Sni) ? server.Address : server.Sni,
                    ["fingerprint"] = string.IsNullOrWhiteSpace(server.Fingerprint) ? "chrome" : server.Fingerprint,
                    ["publicKey"] = server.PublicKey,
                    ["shortId"] = server.ShortId,
                    ["spiderX"] = string.IsNullOrWhiteSpace(server.SpiderX) ? "/" : server.SpiderX
                };
                break;

            default:
                stream["security"] = "none";
                break;
        }

        if (server.Network.Equals("ws", StringComparison.OrdinalIgnoreCase))
        {
            stream["wsSettings"] = new JsonObject
            {
                ["path"] = string.IsNullOrWhiteSpace(server.Path) ? "/" : server.Path,
                ["headers"] = new JsonObject
                {
                    ["Host"] = string.IsNullOrWhiteSpace(server.Host) ? server.Sni : server.Host
                }
            };
        }
        else if (server.Network.Equals("grpc", StringComparison.OrdinalIgnoreCase))
        {
            stream["grpcSettings"] = new JsonObject
            {
                ["serviceName"] = server.Path
            };
        }

        return stream;
    }
}
