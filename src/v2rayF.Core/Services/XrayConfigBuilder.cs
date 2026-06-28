using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using v2rayF.Models;

namespace v2rayF.Services;

public static class XrayConfigBuilder
{
    public const int SocksPort = 10808;
    public const int HttpPort = 10809;

    public static string Build(ProxyServer server, AppSettings settings, int? tunFd = null)
    {
        var inbounds = new JsonArray
        {
            new JsonObject
            {
                ["tag"] = "socks-in",
                ["port"] = SocksPort,
                ["listen"] = "127.0.0.1",
                ["protocol"] = "socks",
                ["settings"] = new JsonObject { ["udp"] = true }
            },
            new JsonObject
            {
                ["tag"] = "http-in",
                ["port"] = HttpPort,
                ["listen"] = "127.0.0.1",
                ["protocol"] = "http"
            }
        };

        if (settings.EnableTunMode)
        {
            var tunSettings = new JsonObject
            {
                ["name"] = "v2rayF",
                ["MTU"] = 1500,
                ["inet4_address"] = "172.19.0.1/30",
                ["stack"] = "system"
            };

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
                    ["destOverride"] = new JsonArray { "http", "tls", "quic" }
                }
            });
        }

        var config = new JsonObject
        {
            ["log"] = new JsonObject { ["loglevel"] = "warning" },
            ["inbounds"] = inbounds,
            ["outbounds"] = new JsonArray
            {
                BuildOutbound(server),
                new JsonObject { ["tag"] = "direct", ["protocol"] = "freedom" },
                new JsonObject { ["tag"] = "block", ["protocol"] = "blackhole" }
            },
            ["routing"] = BuildRouting(settings)
        };

        return config.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public const int SpeedtestSocksPort = 10818;

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
                BuildOutbound(server),
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

    private static JsonObject BuildRouting(AppSettings settings)
    {
        var rules = new JsonArray();

        if (settings.EnableTunMode)
        {
            rules.Add(new JsonObject
            {
                ["type"] = "field",
                ["inboundTag"] = new JsonArray { "tun-in" },
                ["port"] = "53",
                ["network"] = "udp",
                ["outboundTag"] = "direct"
            });
        }

        switch (settings.RoutingMode)
        {
            case RoutingMode.BypassLan:
                rules.Add(new JsonObject
                {
                    ["type"] = "field",
                    ["ip"] = new JsonArray { "geoip:private" },
                    ["outboundTag"] = "direct"
                });
                break;

            case RoutingMode.BypassChina:
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
                foreach (var entry in ParseCustomRules(settings.CustomDirectRules))
                {
                    if (entry.StartsWith("full:", StringComparison.Ordinal) ||
                        entry.StartsWith("domain:", StringComparison.Ordinal) ||
                        entry.StartsWith("regexp:", StringComparison.Ordinal) ||
                        entry.Contains('.') && !entry.Contains('/'))
                    {
                        var domain = entry.Contains(':') ? entry : $"domain:{entry}";
                        rules.Add(new JsonObject
                        {
                            ["type"] = "field",
                            ["domain"] = new JsonArray { domain },
                            ["outboundTag"] = "direct"
                        });
                    }
                    else
                    {
                        rules.Add(new JsonObject
                        {
                            ["type"] = "field",
                            ["ip"] = new JsonArray { entry },
                            ["outboundTag"] = "direct"
                        });
                    }
                }
                break;
        }

        rules.Add(new JsonObject
        {
            ["type"] = "field",
            ["network"] = "tcp,udp",
            ["outboundTag"] = "proxy"
        });

        return new JsonObject
        {
            ["domainStrategy"] = settings.RoutingMode == RoutingMode.BypassChina ? "IPIfNonMatch" : "AsIs",
            ["rules"] = rules
        };
    }

    private static IEnumerable<string> ParseCustomRules(string rules) =>
        rules.Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static JsonObject BuildOutbound(ProxyServer server) =>
        server.Protocol switch
        {
            ProxyProtocol.VMess => BuildVmessOutbound(server),
            ProxyProtocol.VLESS => BuildVlessOutbound(server),
            ProxyProtocol.Shadowsocks => BuildShadowsocksOutbound(server),
            ProxyProtocol.Trojan => BuildTrojanOutbound(server),
            ProxyProtocol.Socks => BuildSocksOutbound(server),
            _ => throw new NotSupportedException($"Protocol {server.Protocol} is not supported.")
        };

    private static JsonObject BuildVmessOutbound(ProxyServer server)
    {
        var outbound = new JsonObject
        {
            ["tag"] = "proxy",
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

    private static JsonObject BuildVlessOutbound(ProxyServer server)
    {
        var user = new JsonObject
        {
            ["id"] = server.UserId,
            ["encryption"] = "none"
        };

        if (!string.IsNullOrWhiteSpace(server.Flow))
            user["flow"] = server.Flow;

        var outbound = new JsonObject
        {
            ["tag"] = "proxy",
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

        return outbound;
    }

    private static JsonObject BuildShadowsocksOutbound(ProxyServer server) => new()
    {
        ["tag"] = "proxy",
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

    private static JsonObject BuildTrojanOutbound(ProxyServer server) => new()
    {
        ["tag"] = "proxy",
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
                ["allowInsecure"] = server.AllowInsecure
            }
        }
    };

    private static JsonObject BuildSocksOutbound(ProxyServer server)
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
            ["tag"] = "proxy",
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
                    ["allowInsecure"] = server.AllowInsecure
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
                    ["spiderX"] = string.IsNullOrWhiteSpace(server.Path) ? "/" : server.Path
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
