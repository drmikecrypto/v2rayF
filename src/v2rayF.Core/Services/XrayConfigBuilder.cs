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
    public const int ApiPort = 10085;
    public const string GooglePingUrl = "https://www.google.com/generate_204";
    public const int TunMtu = 1500;
    /// <summary>Android VpnService MTU — inner 1500 + Xray overhead fragments on LTE.</summary>
    public const int AndroidTunMtu = 1280;

    /// <summary>WNS / desktop push host suffixes — route via proxy under TUN.</summary>
    public static readonly string[] WindowsNotificationDomainSuffixes =
    [
        "wns.windows.com",
        "notify.windows.com",
        "push.services.microsoft.com",
        "mp.microsoft.com"
    ];

    private static readonly JsonSerializerOptions CompactJson = new() { WriteIndented = false };

    /// <summary>Options for minimal single-server Xray runtime (speedtest / probe).</summary>
    public sealed class ServerRuntimeOptions
    {
        public int SocksPort { get; init; } = SpeedtestSocksPort;
        public bool EnableFragment { get; init; }
        public AppSettings Settings { get; init; } = new();
    }

    /// <summary>
    /// Minimal per-server Xray JSON: same DNS bootstrap and outbound as live connect,
    /// with a local SOCKS inbound for proxy-path probes.
    /// </summary>
    public static string BuildServerRuntime(ProxyServer server, ServerRuntimeOptions opts)
    {
        EnsureOutboundReady(server);
        var useFragment = AdaptiveSurviveService.ShouldApplyFragmentForServer(server, opts.EnableFragment);
        var outboundHosts = CollectOutboundDomainHosts([server]);
        var outbounds = new JsonArray();

        if (useFragment)
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

        outbounds.Add(BuildOutbound(server, "proxy", useFragment));
        outbounds.Add(new JsonObject { ["tag"] = "direct", ["protocol"] = "freedom" });
        outbounds.Add(new JsonObject { ["tag"] = "dns-out", ["protocol"] = "dns" });

        var config = new JsonObject
        {
            ["log"] = new JsonObject { ["loglevel"] = "warning" },
            ["dns"] = BuildDns(opts.Settings, outboundHosts),
            ["inbounds"] = new JsonArray
            {
                new JsonObject
                {
                    ["tag"] = "speedtest-in",
                    ["port"] = opts.SocksPort,
                    ["listen"] = "127.0.0.1",
                    ["protocol"] = "socks",
                    ["settings"] = new JsonObject { ["udp"] = false }
                }
            },
            ["outbounds"] = outbounds,
            ["routing"] = BuildServerRuntimeRouting()
        };

        return config.ToJsonString(CompactJson);
    }

    private static JsonObject BuildServerRuntimeRouting() => new()
    {
        ["domainStrategy"] = "AsIs",
        ["rules"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "field",
                ["inboundTag"] = new JsonArray { "dns-module" },
                ["outboundTag"] = "direct"
            },
            PublicDnsDirectRule(),
            new JsonObject
            {
                ["type"] = "field",
                ["network"] = "tcp,udp",
                ["outboundTag"] = "proxy"
            }
        }
    };

    public static string Build(
        ProxyServer server,
        AppSettings settings,
        int? tunFd = null,
        IReadOnlyList<ProxyServer>? multipathServers = null)
    {
        var peers = NormalizePeers(server, multipathServers, settings.SmartMultipathEnabled);
        foreach (var peer in peers)
            EnsureOutboundReady(peer);
        var useBalancer = peers.Count > 1;

        var inbounds = new JsonArray
        {
            BuildLocalSocksInbound("socks-in", SocksPort, "127.0.0.1"),
            BuildLocalHttpInbound("http-in", HttpPort, "127.0.0.1"),
            BuildApiInbound()
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
            // Xray TUN schema (not sing-box). Android only needs name/MTU — FD via env xray.tun.fd.
            // Desktop uses gateway + autoSystemRoutingTable (requires Xray >= 26.4 / v26.7.28).
            var tunSettings = new JsonObject
            {
                ["name"] = TunConstants.InterfaceName,
                ["MTU"] = tunFd is null ? TunMtu : AndroidTunMtu
            };

            if (tunFd is null)
            {
                var gateway = new JsonArray { "172.19.0.1/30" };
                var routes = new JsonArray { "0.0.0.0/0" };
                if (!settings.BlockIpv6)
                {
                    gateway.Add("fdfe:dcba:9876::1/126");
                    routes.Add("::/0");
                }

                tunSettings["gateway"] = gateway;
                tunSettings["dns"] = new JsonArray { "172.19.0.1" };
                tunSettings["autoSystemRoutingTable"] = routes;
                tunSettings["autoOutboundsInterface"] = "auto";
            }

            inbounds.Add(new JsonObject
            {
                ["tag"] = "tun-in",
                ["protocol"] = "tun",
                ["settings"] = tunSettings,
                ["sniffing"] = TunSniffing(tunFd is not null)
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

        var outboundHosts = CollectOutboundDomainHosts(peers);

        var config = new JsonObject
        {
            ["log"] = new JsonObject { ["loglevel"] = "warning" },
            ["dns"] = BuildDns(settings, outboundHosts),
            ["inbounds"] = inbounds,
            ["outbounds"] = outbounds,
            ["routing"] = BuildRouting(settings, useBalancer),
            ["stats"] = new JsonObject(),
            ["api"] = new JsonObject
            {
                ["tag"] = "api",
                ["services"] = new JsonArray { "StatsService" }
            },
            ["policy"] = new JsonObject
            {
                ["system"] = new JsonObject
                {
                    ["statsOutboundUplink"] = true,
                    ["statsOutboundDownlink"] = true
                }
            }
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
                    ["destination"] = GooglePingUrl,
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

    public static string BuildSpeedtest(
        ProxyServer server,
        int socksPort = SpeedtestSocksPort,
        bool enableFragment = false) =>
        BuildServerRuntime(server, new ServerRuntimeOptions
        {
            SocksPort = socksPort,
            EnableFragment = enableFragment,
            // UDP DNS for probes — DoH default is for live Connect only (v2.2.1).
            Settings = new AppSettings { DnsThroughProxy = false }
        });

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

    private static JsonObject BuildDns(AppSettings settings, IReadOnlyList<string> outboundDomainHosts)
    {
        var dns = new JsonObject
        {
            ["queryStrategy"] = settings.BlockIpv6 ? "UseIPv4" : "UseIP",
            ["tag"] = "dns-module"
        };

        var servers = new JsonArray();

        // Bootstrap: resolve outbound hostnames via direct UDP DNS (avoids chicken-and-egg
        // when general DNS is DoH-through-proxy).
        if (outboundDomainHosts.Count > 0)
        {
            var domains = new JsonArray();
            foreach (var host in outboundDomainHosts)
                domains.Add($"full:{host}");

            servers.Add(new JsonObject
            {
                ["address"] = "1.1.1.1",
                ["domains"] = domains.DeepClone(),
                ["skipFallback"] = true
            });
            servers.Add(new JsonObject
            {
                ["address"] = "8.8.8.8",
                ["domains"] = domains,
                ["skipFallback"] = true
            });
        }

        if (settings.DnsThroughProxy)
        {
            // App DNS via DoH; 1.1.1.1/8.8.8.8 stay direct (see BuildRouting).
            servers.Add(new JsonObject
            {
                ["address"] = "https://1.1.1.1/dns-query",
                ["skipFallback"] = false
            });
            servers.Add(new JsonObject
            {
                ["address"] = "https://8.8.8.8/dns-query",
                ["skipFallback"] = false
            });
        }
        else
        {
            servers.Add("1.1.1.1");
            servers.Add("8.8.8.8");
        }

        dns["servers"] = servers;
        return dns;
    }

    private static List<string> CollectOutboundDomainHosts(IEnumerable<ProxyServer> peers)
    {
        var hosts = new List<string>();
        foreach (var peer in peers)
        {
            var host = peer.Address?.Trim();
            if (string.IsNullOrWhiteSpace(host))
                continue;
            if (IPAddressLooksLike(host))
                continue;
            if (hosts.Any(h => string.Equals(h, host, StringComparison.OrdinalIgnoreCase)))
                continue;
            hosts.Add(host);
        }

        return hosts;
    }

    private static bool IPAddressLooksLike(string host) =>
        System.Net.IPAddress.TryParse(host, out _);

    private static JsonObject BuildApiInbound() => new()
    {
        ["tag"] = "api",
        ["listen"] = "127.0.0.1",
        ["port"] = ApiPort,
        ["protocol"] = "dokodemo-door",
        ["settings"] = new JsonObject { ["address"] = "127.0.0.1" }
    };

    private static JsonObject BuildRouting(AppSettings settings, bool useBalancer)
    {
        var rules = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "field",
                ["inboundTag"] = new JsonArray { "api" },
                ["outboundTag"] = "api"
            }
        };

        // App DNS (TUN/local) before public-resolver IP rules, or 1.1.1.1:53 skips dns-out.
        rules.Add(new JsonObject
        {
            ["type"] = "field",
            ["port"] = "53,853",
            ["network"] = "udp,tcp",
            ["outboundTag"] = "dns-out"
        });

        // DNS module bootstrap only — not TUN packets to 1.1.1.1 / 8.8.8.8.
        rules.Add(PublicDnsDirectRule());
        rules.Add(new JsonObject
        {
            ["type"] = "field",
            ["inboundTag"] = new JsonArray { "dns-module" },
            ["outboundTag"] = "direct"
        });

        if (settings.EnableTunMode)
        {
            // VPN DNS subnet sits inside 172.16.0.0/12 — do not Bypass-LAN it to direct.
            rules.Add(new JsonObject
            {
                ["type"] = "field",
                ["ip"] = new JsonArray { "172.19.0.0/30" },
                ["outboundTag"] = "dns-out"
            });
        }

        if (settings.EnableTunMode && settings.AllowDesktopNotificationRouting)
            AppendWindowsNotificationRules(rules);

        if (settings.BlockIpv6)
        {
            rules.Add(new JsonObject
            {
                ["type"] = "field",
                ["inboundTag"] = new JsonArray { "tun-in" },
                ["ip"] = new JsonArray { "::/0" },
                ["outboundTag"] = "block"
            });
        }

        if (settings.EnableTunMode)
            AppendProcessAppNetworkRules(rules, settings);

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

    /// <summary>
    /// Desktop App Network: process → direct / blackhole while TUN captures traffic.
    /// </summary>
    private static void AppendProcessAppNetworkRules(JsonArray rules, AppSettings settings)
    {
        var block = AppNetworkPolicy.GetBlockIds(settings, mobile: false);
        if (block.Count > 0)
        {
            var names = new JsonArray();
            foreach (var name in block)
                names.Add(name);
            rules.Add(new JsonObject
            {
                ["type"] = "field",
                ["process"] = names,
                ["outboundTag"] = "block"
            });
        }

        var direct = AppNetworkPolicy.GetDirectIds(settings, mobile: false);
        if (direct.Count > 0)
        {
            var names = new JsonArray();
            foreach (var name in direct)
                names.Add(name);
            rules.Add(new JsonObject
            {
                ["type"] = "field",
                ["process"] = names,
                ["outboundTag"] = "direct"
            });
        }
    }

    private static JsonObject PublicDnsDirectRule() => new()
    {
        ["type"] = "field",
        ["inboundTag"] = new JsonArray { "dns-module" },
        ["ip"] = new JsonArray { "1.1.1.1", "8.8.8.8" },
        ["outboundTag"] = "direct"
    };

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

    private static JsonObject LocalSniffing() => new()
    {
        ["enabled"] = true,
        ["destOverride"] = new JsonArray { "http", "tls" },
        ["routeOnly"] = true
    };

    /// <summary>
    /// Android TUN (fd inherited): empty destOverride — TLS/HTTP sniff fights Vision/WS/Trojan/SS on gVisor.
    /// Desktop TUN: http,tls only (no quic — Chromium QUIC over TUN hangs).
    /// </summary>
    private static JsonObject TunSniffing(bool androidTunFd)
    {
        var destOverride = new JsonArray();
        if (!androidTunFd)
        {
            destOverride.Add("http");
            destOverride.Add("tls");
        }

        return new JsonObject
        {
            ["enabled"] = true,
            ["destOverride"] = destOverride,
            ["routeOnly"] = true
        };
    }

    private static void AppendWindowsNotificationRules(JsonArray rules)
    {
        foreach (var suffix in WindowsNotificationDomainSuffixes)
        {
            rules.Add(new JsonObject
            {
                ["type"] = "field",
                ["domain"] = new JsonArray { $"domain:{suffix}" },
                ["outboundTag"] = "proxy"
            });
        }
    }

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
        ["settings"] = new JsonObject { ["udp"] = true },
        ["sniffing"] = LocalSniffing()
    };

    private static JsonObject BuildLocalHttpInbound(string tag, int port, string listen) => new()
    {
        ["tag"] = tag,
        ["port"] = port,
        ["listen"] = listen,
        ["protocol"] = "http",
        ["sniffing"] = LocalSniffing()
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
            var sockopt = BuildLiveSockopt(server, includeNoDelay: true);
            sockopt["dialerProxy"] = "fragment";
            stream["sockopt"] = sockopt;
        }
        else
        {
            ApplyLiveSockopt(outbound, server);
        }

        return outbound;
    }

    /// <summary>Reject incomplete REALITY / normalize Vision before emitting JSON.</summary>
    public static void EnsureOutboundReady(ProxyServer server)
    {
        ShareLinkParser.NormalizeVisionFlow(server);
        var security = ShareLinkParser.NormalizeSecurity(server.Security);
        if (security == "reality" && string.IsNullOrWhiteSpace(server.PublicKey))
            throw new InvalidOperationException(
                "REALITY requires a public key (pbk). This link is incomplete.");
    }

    public const int TcpKeepAliveIdleSec = 45;
    public const int TcpKeepAliveIntervalSec = 15;

    private static void ApplyLiveSockopt(JsonObject outbound, ProxyServer server)
    {
        if (outbound["streamSettings"] is not JsonObject stream)
        {
            stream = new JsonObject();
            outbound["streamSettings"] = stream;
        }

        stream["sockopt"] = BuildLiveSockopt(server, includeNoDelay: !IsVisionFlow(server));
    }

    private static JsonObject BuildLiveSockopt(ProxyServer server, bool includeNoDelay)
    {
        var sockopt = new JsonObject
        {
            ["tcpKeepAliveIdle"] = TcpKeepAliveIdleSec,
            ["tcpKeepAliveInterval"] = TcpKeepAliveIntervalSec
        };
        if (includeNoDelay)
            sockopt["tcpNoDelay"] = true;
        return sockopt;
    }

    private static bool IsVisionFlow(ProxyServer server) =>
        ShareLinkParser.IsVisionFlow(server);

    private static JsonObject BuildVmessOutbound(ProxyServer server, string tag)
    {
        var user = new JsonObject
        {
            ["id"] = server.UserId,
            ["alterId"] = server.AlterId,
            ["security"] = string.IsNullOrWhiteSpace(server.Cipher) ? "auto" : server.Cipher
        };
        if (!string.IsNullOrWhiteSpace(server.PacketEncoding))
            user["packetEncoding"] = server.PacketEncoding;

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
                        ["users"] = new JsonArray { user }
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
        if (!string.IsNullOrWhiteSpace(server.PacketEncoding))
            user["packetEncoding"] = server.PacketEncoding;

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

    private static JsonObject BuildShadowsocksOutbound(ProxyServer server, string tag)
    {
        var outbound = new JsonObject
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

        var network = ShareLinkParser.NormalizeNetwork(server.Network);
        var security = ShareLinkParser.NormalizeSecurity(server.Security);
        if (network is not "tcp" || security is not "none")
            outbound["streamSettings"] = BuildStreamSettings(server);
        else
            outbound["streamSettings"] = new JsonObject(); // so live keepalive sockopt can attach

        return outbound;
    }

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
            {
                var ws = new JsonObject
                {
                    ["path"] = string.IsNullOrWhiteSpace(server.Path) ? "/" : server.Path,
                    ["headers"] = new JsonObject
                    {
                        ["Host"] = FirstHost(server)
                    }
                };
                if (server.MaxEarlyData > 0)
                {
                    ws["maxEarlyData"] = server.MaxEarlyData;
                    ws["earlyDataHeaderName"] = string.IsNullOrWhiteSpace(server.EarlyDataHeaderName)
                        ? "Sec-WebSocket-Protocol"
                        : server.EarlyDataHeaderName;
                }

                stream["wsSettings"] = ws;
                break;
            }

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
                if (!string.IsNullOrWhiteSpace(server.Extra))
                {
                    try
                    {
                        var extra = JsonNode.Parse(server.Extra);
                        if (extra is JsonObject extraObj)
                            xhttp["extra"] = extraObj.DeepClone();
                    }
                    catch (JsonException)
                    {
                        // Share-link extra was not JSON; omit.
                    }
                }
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
                    ["security"] = string.IsNullOrWhiteSpace(server.QuicSecurity) ? "none" : server.QuicSecurity,
                    ["key"] = server.QuicKey ?? "",
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
