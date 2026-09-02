using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using v2rayF.Models;

namespace v2rayF.Services;

/// <summary>
/// Passphrase-encrypted profile export (.v2rayf) and in-session vault lock for sensitive UI.
/// </summary>
public sealed class ProfileVault
{
    public const string FileExtension = ".v2rayf";
    private const string Magic = "V2RAYF01";
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 200_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public bool IsUnlocked { get; private set; }

    public void Unlock() => IsUnlocked = true;

    public void Lock() => IsUnlocked = false;

    public byte[] Export(IReadOnlyList<ProxyServer> servers, AppSettings settings, string passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase) || passphrase.Length < 8)
            throw new InvalidOperationException("Vault passphrase must be at least 8 characters.");

        var payload = new VaultPayload
        {
            Servers = servers.ToList(),
            Settings = CloneExportSettings(settings)
        };
        var json = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = DeriveKey(passphrase, salt);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[json.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(key, TagSize))
            aes.Encrypt(nonce, json, cipher, tag);

        using var ms = new MemoryStream();
        ms.Write(Encoding.ASCII.GetBytes(Magic));
        ms.WriteByte((byte)salt.Length);
        ms.Write(salt);
        ms.WriteByte((byte)nonce.Length);
        ms.Write(nonce);
        ms.WriteByte((byte)tag.Length);
        ms.Write(tag);
        var len = BitConverter.GetBytes(cipher.Length);
        ms.Write(len);
        ms.Write(cipher);
        return ms.ToArray();
    }

    public VaultPayload Import(byte[] data, string passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase))
            throw new InvalidOperationException("Passphrase required.");

        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        var magic = Encoding.ASCII.GetString(br.ReadBytes(Magic.Length));
        if (magic != Magic)
            throw new InvalidOperationException("Not a v2rayF vault file.");

        var saltLen = br.ReadByte();
        var salt = br.ReadBytes(saltLen);
        var nonceLen = br.ReadByte();
        var nonce = br.ReadBytes(nonceLen);
        var tagLen = br.ReadByte();
        var tag = br.ReadBytes(tagLen);
        var cipherLen = br.ReadInt32();
        var cipher = br.ReadBytes(cipherLen);

        var key = DeriveKey(passphrase, salt);
        var plain = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, cipher, tag, plain);
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("Wrong passphrase or corrupted vault.");
        }

        var payload = JsonSerializer.Deserialize<VaultPayload>(plain, JsonOptions)
                      ?? throw new InvalidOperationException("Vault payload empty.");
        return payload;
    }

    private static byte[] DeriveKey(string passphrase, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase),
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            KeySize);
    }

    private static AppSettings CloneExportSettings(AppSettings s) => new()
    {
        RoutingMode = s.RoutingMode,
        CustomDirectRules = s.CustomDirectRules,
        CustomProxyRules = s.CustomProxyRules,
        CustomBlockRules = s.CustomBlockRules,
        SmartConnectEnabled = s.SmartConnectEnabled,
        StartupRankServersEnabled = s.StartupRankServersEnabled,
        AllowDesktopNotificationRouting = s.AllowDesktopNotificationRouting,
        SmartMultipathEnabled = s.SmartMultipathEnabled,
        SelectedServerId = s.SelectedServerId,
        KillSwitchEnabled = s.KillSwitchEnabled,
        BlockIpv6 = s.BlockIpv6,
        DnsThroughProxy = s.DnsThroughProxy,
        EnablePacketFragment = s.EnablePacketFragment,
        AdaptiveSurviveEnabled = s.AdaptiveSurviveEnabled,
        AutoReconnectEnabled = s.AutoReconnectEnabled,
        BatteryOptimizationPromptShown = s.BatteryOptimizationPromptShown,
        LastBatteryPromptUtc = s.LastBatteryPromptUtc,
        SubscriptionUrl = s.SubscriptionUrl,
        StorageVersion = 2
    };

    public sealed class VaultPayload
    {
        public List<ProxyServer> Servers { get; set; } = [];
        public AppSettings? Settings { get; set; }
    }
}
