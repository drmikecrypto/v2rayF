using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using v2rayF.Services;

namespace v2rayF.Models;

public partial class ProxyServer : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "Server";

    public ProxyProtocol Protocol { get; set; } = ProxyProtocol.Unknown;

    public string Address { get; set; } = "";

    public int Port { get; set; }

    public string UserId { get; set; } = "";

    public string Password { get; set; } = "";

    public int AlterId { get; set; }

    public string Network { get; set; } = "tcp";

    public string Security { get; set; } = "none";

    public string Flow { get; set; } = "";

    public string Sni { get; set; } = "";

    public string Host { get; set; } = "";

    public string Path { get; set; } = "";

    public string Fingerprint { get; set; } = "chrome";

    public string PublicKey { get; set; } = "";

    public string ShortId { get; set; } = "";

    public string SpiderX { get; set; } = "";

    /// <summary>Comma-separated ALPN list (e.g. h2,http/1.1).</summary>
    public string Alpn { get; set; } = "";

    /// <summary>TCP header type: none | http.</summary>
    public string HeaderType { get; set; } = "";

    /// <summary>gRPC service name (falls back to Path when empty).</summary>
    public string ServiceName { get; set; } = "";

    /// <summary>Transport mode (gRPC multi, xHTTP mode, etc.).</summary>
    public string Mode { get; set; } = "";

    /// <summary>mKCP seed.</summary>
    public string Seed { get; set; } = "";

    /// <summary>xHTTP extra JSON (query extra=).</summary>
    public string Extra { get; set; } = "";

    /// <summary>QUIC encryption (none/aes-128-gcm/chacha20-poly1305).</summary>
    public string QuicSecurity { get; set; } = "";

    /// <summary>QUIC key.</summary>
    public string QuicKey { get; set; } = "";

    /// <summary>WS early data size (maxEarlyData / ed).</summary>
    public int MaxEarlyData { get; set; }

    /// <summary>WS early data header name (default Sec-WebSocket-Protocol).</summary>
    public string EarlyDataHeaderName { get; set; } = "";

    /// <summary>VLESS/VMess packet encoding (xudp / packet).</summary>
    public string PacketEncoding { get; set; } = "";

    /// <summary>Hysteria2 upload bandwidth (Mbps). 0 = omit (sing-box default).</summary>
    public int UpMbps { get; set; }

    /// <summary>Hysteria2 download bandwidth (Mbps). 0 = omit (sing-box default).</summary>
    public int DownMbps { get; set; }

    /// <summary>TUIC UDP relay mode (native / quic). Empty = omit.</summary>
    public string UdpRelayMode { get; set; } = "";

    /// <summary>WireGuard MTU. 0 = builder default (1400).</summary>
    public int Mtu { get; set; }

    /// <summary>VLESS encryption (usually none).</summary>
    public string Encryption { get; set; } = "none";

    public string Cipher { get; set; } = "";

    public bool AllowInsecure { get; set; }

    public string RawLink { get; set; } = "";

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    public int? LatencyMs { get; set; }

    [JsonIgnore]
    public string DisplayProtocol => Protocol switch
    {
        ProxyProtocol.VMess => "VMess",
        ProxyProtocol.VLESS => "VLESS",
        ProxyProtocol.Shadowsocks => "SS",
        ProxyProtocol.Trojan => "Trojan",
        ProxyProtocol.Socks => "SOCKS",
        ProxyProtocol.Hysteria2 => "Hy2",
        ProxyProtocol.Tuic => "TUIC",
        ProxyProtocol.WireGuard => "WG",
        ProxyProtocol.AnyTls => "anytls",
        _ => "?"
    };

    [JsonIgnore]
    public string DisplayEndpoint => string.IsNullOrWhiteSpace(Address) ? "" : $"{Address}:{Port}";

    [JsonIgnore]
    public string DisplayTransport
    {
        get
        {
            if (Protocol == ProxyProtocol.Shadowsocks)
            {
                var net = ShareLinkParser.NormalizeNetwork(Network);
                return net is "tcp" or "" ? "SS · tcp" : $"SS · {net}";
            }

            var network = ShareLinkParser.NormalizeNetwork(Network);
            var security = ShareLinkParser.NormalizeSecurity(Security);
            var parts = new List<string> { DisplayProtocol, network };
            if (!string.IsNullOrEmpty(security) && !string.Equals(security, "none", StringComparison.OrdinalIgnoreCase))
                parts.Add(security);
            return string.Join(" · ", parts);
        }
    }

    [JsonIgnore]
    public string DisplayLatency => LatencyMs switch
    {
        null => "—",
        < 0 => "timeout",
        _ => $"{LatencyMs} ms"
    };

    public void SetLatency(int? ms)
    {
        LatencyMs = ms;
        OnPropertyChanged(nameof(LatencyMs));
        OnPropertyChanged(nameof(DisplayLatency));
    }
}
