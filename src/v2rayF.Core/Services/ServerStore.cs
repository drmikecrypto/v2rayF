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
            return servers ?? [];
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
            await using var stream = File.Create(_storePath);
            await JsonSerializer.SerializeAsync(stream, servers.ToList(), JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }
}
