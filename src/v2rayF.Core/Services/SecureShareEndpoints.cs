using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using v2rayF.Models;

namespace v2rayF.Services;

/// <summary>
/// Production Secure Share: authenticated LAN SOCKS/HTTP for both Xray and sing-box.
/// Clients set a proxy once — not transparent hotspot NAT.
/// </summary>
public static class SecureShareEndpoints
{
    public const int DefaultSharePort = 10880;

    public static int ResolveSocksPort(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.ShareBindPort > 0 ? settings.ShareBindPort : DefaultSharePort;
    }

    public static int ResolveHttpPort(AppSettings settings) => ResolveSocksPort(settings) + 1;

    public static void EnsureShareCredentials(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(settings.ShareAuthUser))
            settings.ShareAuthUser = "v2rayf";

        if (string.IsNullOrWhiteSpace(settings.ShareAuthPass))
            settings.ShareAuthPass = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
    }

    public static void RotateSharePassword(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(settings.ShareAuthUser))
            settings.ShareAuthUser = "v2rayf";
        settings.ShareAuthPass = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
    }

    public static string ResolveShareListenAddress(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.ShareListenAllInterfaces)
            return "0.0.0.0";

        var lan = AppServices.Platform?.GetLanIPv4Address();
        return string.IsNullOrWhiteSpace(lan) ? "0.0.0.0" : lan;
    }

    /// <summary>Score NIC candidates for Secure Share advertise IP (hotspot/SoftAP first).</summary>
    public static IReadOnlyList<string> RankAdvertiseAddresses(
        IEnumerable<(string Name, string Description, string Ipv4)> nics)
    {
        var scored = new List<(int Score, string Ip)>();
        foreach (var nic in nics)
        {
            if (!IPAddress.TryParse(nic.Ipv4, out var addr) ||
                addr.AddressFamily != AddressFamily.InterNetwork)
                continue;

            var ip = addr.ToString();
            if (ip.StartsWith("127.", StringComparison.Ordinal))
                continue;
            if (ip.StartsWith("169.254.", StringComparison.Ordinal))
                continue;
            // WinTun / Xray desktop gateway — not for client advertise.
            if (ip.StartsWith("172.19.0.", StringComparison.Ordinal))
                continue;

            var label = $"{nic.Name} {nic.Description}";
            var score = 10;
            if (IsHotspotSoftApLabel(label))
                score += 100;
            if (IsPrivateRfc1918(ip))
                score += 20;
            if (label.Contains(TunConstants.InterfaceName, StringComparison.OrdinalIgnoreCase))
                score -= 80;

            scored.Add((score, ip));
        }

        return scored
            .OrderByDescending(s => s.Score)
            .Select(s => s.Ip)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public static bool IsHotspotSoftApLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return false;

        ReadOnlySpan<string> needles =
        [
            "softap", "soft ap", "hosted network", "mobile hotspot", "wi-fi direct",
            "wifi direct", "microsoft wi-fi direct", "local area connection*",
            "ap0", "wlan1", "tether", "hotspot", "androidap", "softapiface"
        ];
        foreach (var n in needles)
        {
            if (label.Contains(n, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static bool IsPrivateRfc1918(string ipv4)
    {
        if (!IPAddress.TryParse(ipv4, out var addr))
            return false;
        var bytes = addr.GetAddressBytes();
        if (bytes.Length != 4)
            return false;
        if (bytes[0] == 10)
            return true;
        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            return true;
        if (bytes[0] == 192 && bytes[1] == 168)
            return true;
        return false;
    }

    /// <summary>Enumerate live NIC IPv4s and return ranked advertise list.</summary>
    public static IReadOnlyList<string> CollectAdvertiseAddressesFromSystem()
    {
        var rows = new List<(string Name, string Description, string Ipv4)>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                    continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;
                    rows.Add((nic.Name, nic.Description ?? "", ua.Address.ToString()));
                }
            }
        }
        catch
        {
            // ignore
        }

        return RankAdvertiseAddresses(rows);
    }

    public static string? PreferAdvertiseAddress(IReadOnlyList<string>? ranked) =>
        ranked is { Count: > 0 } ? ranked[0] : null;

    public static string FormatEndpointBlock(
        AppSettings settings,
        string advertiseIp,
        bool revealPassword,
        IReadOnlyList<string>? candidates = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        EnsureShareCredentials(settings);
        var port = ResolveSocksPort(settings);
        var pass = revealPassword ? settings.ShareAuthPass : "••••••••";
        var bindHint = settings.ShareListenAllInterfaces
            ? "listen: all interfaces (0.0.0.0)"
            : $"listen: {advertiseIp}";

        var sb = new StringBuilder();
        sb.AppendLine($"socks5://{settings.ShareAuthUser}:{pass}@{advertiseIp}:{port}");
        sb.AppendLine($"http://{settings.ShareAuthUser}:{pass}@{advertiseIp}:{port + 1}");
        sb.Append(bindHint);
        sb.Append(" · Clients must set this proxy once.");
        sb.Append(" · OEM hotspot often bypasses VPN — use these proxies.");
        if (candidates is { Count: > 1 })
        {
            sb.AppendLine();
            sb.Append("Also try: ");
            sb.Append(string.Join(", ", candidates.Skip(1).Take(3)));
        }

        if (!settings.ShareListenAllInterfaces && candidates is { Count: > 1 })
        {
            sb.AppendLine();
            sb.Append("Tip: enable Listen on all interfaces if SoftAP/hotspot clients cannot reach this IP.");
        }

        return sb.ToString().TrimEnd();
    }

    public static string FormatSetupTipMarkdown(
        AppSettings settings,
        string advertiseIp,
        bool includePassword)
    {
        ArgumentNullException.ThrowIfNull(settings);
        EnsureShareCredentials(settings);
        var socks = ResolveSocksPort(settings);
        var http = socks + 1;
        var pass = includePassword ? settings.ShareAuthPass : "(unlock vault to copy password)";
        var sb = new StringBuilder();
        sb.AppendLine("# v2rayF Secure Share setup");
        sb.AppendLine();
        sb.AppendLine("Host is Connected. Set this as the **system or app proxy** on the other device.");
        sb.AppendLine();
        sb.AppendLine($"- Host IP: `{advertiseIp}`");
        sb.AppendLine($"- SOCKS5: `{advertiseIp}:{socks}`");
        sb.AppendLine($"- HTTP: `{advertiseIp}:{http}`");
        sb.AppendLine($"- User: `{settings.ShareAuthUser}`");
        sb.AppendLine($"- Pass: `{pass}`");
        sb.AppendLine();
        sb.AppendLine("## Android client");
        sb.AppendLine("Wi‑Fi → gear → Proxy → Manual → host/port above (HTTP), or use an app that supports SOCKS5 auth.");
        sb.AppendLine();
        sb.AppendLine("## Windows client");
        sb.AppendLine("Settings → Network → Proxy → Manual proxy, or browser SOCKS5 with auth.");
        sb.AppendLine();
        sb.AppendLine("OEM Wi‑Fi hotspot on the host often **bypasses** VpnService — Secure Share proxies are the supported path (not transparent tether).");
        return sb.ToString();
    }
}
