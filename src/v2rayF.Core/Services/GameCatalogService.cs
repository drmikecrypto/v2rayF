using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using v2rayF.Models;

namespace v2rayF.Services;

/// <summary>
/// Bundled game App Network shortcuts (packages / processes). Does not force Direct on Gaming Boost —
/// full tunnel remains default; users merge catalog ids into App Network when they want splits.
/// </summary>
public static class GameCatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private static GameCatalogRoot? _cached;

    public static IReadOnlyList<GameCatalogEntry> Entries => Load().Entries;

    public static GameCatalogRoot Load()
    {
        if (_cached is not null)
            return _cached;

        var asm = typeof(GameCatalogService).Assembly;
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("game-catalog.json", StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            _cached = new GameCatalogRoot();
            return _cached;
        }

        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            _cached = new GameCatalogRoot();
            return _cached;
        }

        _cached = JsonSerializer.Deserialize<GameCatalogRoot>(stream, JsonOptions) ?? new GameCatalogRoot();
        return _cached;
    }

    /// <summary>Test seam — replace catalog without embedded resource.</summary>
    public static void SetCatalogForTests(GameCatalogRoot root) => _cached = root;

    public static void ResetCacheForTests() => _cached = null;

    /// <summary>
    /// Merge catalog Android packages / desktop processes into App Network Direct lists
    /// (download/CDN style split). Does not clear existing entries.
    /// </summary>
    public static int ApplyToDirect(AppSettings settings, bool mobile, IEnumerable<string>? entryIds = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var ids = ResolveEntries(entryIds);
        var added = 0;
        if (mobile)
        {
            var list = AppNetworkPolicy.ParseIdList(settings.AndroidBypassPackages).ToList();
            foreach (var pkg in ids.SelectMany(e => e.AndroidPackages))
            {
                if (list.Any(x => string.Equals(x, pkg, StringComparison.OrdinalIgnoreCase)))
                    continue;
                list.Add(pkg);
                added++;
            }

            settings.AndroidBypassPackages = AppNetworkPolicy.SerializeIdList(list);
        }
        else
        {
            var list = AppNetworkPolicy.ParseIdList(settings.DesktopDirectProcesses).ToList();
            foreach (var proc in ids.SelectMany(e => e.DesktopProcesses))
            {
                if (list.Any(x => string.Equals(x, proc, StringComparison.OrdinalIgnoreCase)))
                    continue;
                list.Add(proc);
                added++;
            }

            settings.DesktopDirectProcesses = AppNetworkPolicy.SerializeIdList(list);
        }

        return added;
    }

    /// <summary>
    /// Merge catalog domain suffixes into CustomDirectRules (one per line).
    /// Useful for Steam CDN / store downloads off-exit while games stay on TUN.
    /// </summary>
    public static int ApplyDomainsToCustomDirect(AppSettings settings, IEnumerable<string>? entryIds = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var ids = ResolveEntries(entryIds);
        var existing = (settings.CustomDirectRules ?? "")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        var added = 0;
        foreach (var suffix in ids.SelectMany(e => e.DomainSuffixes))
        {
            if (existing.Any(x => string.Equals(x, suffix, StringComparison.OrdinalIgnoreCase)))
                continue;
            existing.Add(suffix);
            added++;
        }

        settings.CustomDirectRules = string.Join('\n', existing);
        return added;
    }

    private static IReadOnlyList<GameCatalogEntry> ResolveEntries(IEnumerable<string>? entryIds)
    {
        var all = Entries;
        if (entryIds is null)
            return all;

        var set = new HashSet<string>(entryIds, StringComparer.OrdinalIgnoreCase);
        if (set.Count == 0)
            return all;

        return all.Where(e => set.Contains(e.Id)).ToList();
    }
}

public sealed class GameCatalogRoot
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("entries")]
    public List<GameCatalogEntry> Entries { get; set; } = [];
}

public sealed class GameCatalogEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("androidPackages")]
    public List<string> AndroidPackages { get; set; } = [];

    [JsonPropertyName("desktopProcesses")]
    public List<string> DesktopProcesses { get; set; } = [];

    [JsonPropertyName("domainSuffixes")]
    public List<string> DomainSuffixes { get; set; } = [];
}
