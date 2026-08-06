using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using v2rayF.Models;

namespace v2rayF.Services;

public sealed class ServerStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _storePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public ServerStore()
    {
        var folder = AppServices.CoreEnvironment?.GetDataDirectory()
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "v2rayF");
        Directory.CreateDirectory(folder);
        _storePath = Path.Combine(folder, "servers.json");
    }

    public async Task<IReadOnlyList<ProxyServer>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_storePath))
                return Array.Empty<ProxyServer>();

            await using var stream = File.OpenRead(_storePath);
            var servers = await JsonSerializer.DeserializeAsync<List<ProxyServer>>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            servers ??= [];

            var hadLegacy = servers.Any(HasUnprotectedSecrets);
            foreach (var server in servers)
                UnprotectSensitive(server);

            if (hadLegacy)
                await WriteUnlockedAsync(servers, cancellationToken).ConfigureAwait(false);

            return servers;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(IEnumerable<ProxyServer> servers, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteUnlockedAsync(servers.ToList(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task WriteUnlockedAsync(List<ProxyServer> servers, CancellationToken cancellationToken)
    {
        var toWrite = servers.Select(CloneForDisk).ToList();
        foreach (var server in toWrite)
            ProtectSensitive(server);

        await using var stream = File.Create(_storePath);
        await JsonSerializer.SerializeAsync(stream, toWrite, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    private static ProxyServer CloneForDisk(ProxyServer s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        Protocol = s.Protocol,
        Address = s.Address,
        Port = s.Port,
        UserId = s.UserId,
        Password = s.Password,
        AlterId = s.AlterId,
        Network = s.Network,
        Security = s.Security,
        Flow = s.Flow,
        Sni = s.Sni,
        Host = s.Host,
        Path = s.Path,
        Fingerprint = s.Fingerprint,
        PublicKey = s.PublicKey,
        ShortId = s.ShortId,
        SpiderX = s.SpiderX,
        Alpn = s.Alpn,
        HeaderType = s.HeaderType,
        ServiceName = s.ServiceName,
        Mode = s.Mode,
        Seed = s.Seed,
        Encryption = s.Encryption,
        Cipher = s.Cipher,
        AllowInsecure = s.AllowInsecure,
        RawLink = s.RawLink,
        AddedAt = s.AddedAt,
        LatencyMs = s.LatencyMs
    };

    private static void ProtectSensitive(ProxyServer server)
    {
        var p = AppServices.SecretProtector;
        server.UserId = SecretFieldProtector.ProtectField(p, server.UserId);
        server.Password = SecretFieldProtector.ProtectField(p, server.Password);
        server.PublicKey = SecretFieldProtector.ProtectField(p, server.PublicKey);
        server.RawLink = SecretFieldProtector.ProtectField(p, server.RawLink);
    }

    private static void UnprotectSensitive(ProxyServer server)
    {
        var p = AppServices.SecretProtector;
        server.UserId = SecretFieldProtector.UnprotectField(p, server.UserId);
        server.Password = SecretFieldProtector.UnprotectField(p, server.Password);
        server.PublicKey = SecretFieldProtector.UnprotectField(p, server.PublicKey);
        server.RawLink = SecretFieldProtector.UnprotectField(p, server.RawLink);
    }

    private static bool HasUnprotectedSecrets(ProxyServer server) =>
        IsLegacyPlaintext(server.UserId)
        || IsLegacyPlaintext(server.Password)
        || IsLegacyPlaintext(server.PublicKey)
        || IsLegacyPlaintext(server.RawLink);

    private static bool IsLegacyPlaintext(string? value) =>
        !string.IsNullOrEmpty(value) && !value.StartsWith(AesGcmSecretProtector.Prefix, StringComparison.Ordinal);
}
