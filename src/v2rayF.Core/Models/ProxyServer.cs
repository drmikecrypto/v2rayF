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
