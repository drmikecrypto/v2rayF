using System.Collections.Generic;
using v2rayF.Models;

namespace v2rayF.Services;

    /// <summary>Result of parsing pasted text / files / subscriptions with honest skip reasons.</summary>
public sealed class ImportResult
{
    public IReadOnlyList<ProxyServer> Servers { get; init; } = [];

    public IReadOnlyList<string> SkipReasons { get; init; } = [];

    public int SkippedCount { get; init; }

    /// <summary>True when every candidate was refused (plugins / unsupported) and nothing imported.</summary>
    public bool IsHardRefuse => Servers.Count == 0 && SkipReasons.Count > 0;

    public string SummaryHint
    {
        get
        {
            if (SkipReasons.Count == 0)
                return "";
            if (IsHardRefuse)
            {
                if (SkipReasons.Count == 1)
                    return $"Refused: {SkipReasons[0]}";
                return $"Refused {SkippedCount} unsupported: {string.Join("; ", SkipReasons)}";
            }

            // Prefer a single compact line for StatusText.
            if (SkipReasons.Count == 1)
                return SkipReasons[0];
            return $"Skipped {SkippedCount} unsupported: {string.Join("; ", SkipReasons)}";
        }
    }

    public static ImportResult Empty { get; } = new();

    public static ImportResult FromServers(
        IReadOnlyList<ProxyServer> servers,
        IReadOnlyList<string>? skipReasons = null,
        int skippedCount = 0) =>
        new()
        {
            Servers = servers,
            SkipReasons = skipReasons ?? [],
            SkippedCount = skippedCount > 0 ? skippedCount : (skipReasons?.Count ?? 0)
        };
}
