using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

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
        _ => "?"
    };

    [JsonIgnore]
    public string DisplayEndpoint => string.IsNullOrWhiteSpace(Address) ? "" : $"{Address}:{Port}";

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
