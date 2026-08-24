using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using v2rayF.Models;
using v2rayF.Services;

namespace v2rayF.Desktop.Services;

/// <summary>
/// Running-process catalog for Desktop App Network (TUN process rules).
/// Traffic sampling is not available without elevated ETW — returns empty rates.
/// </summary>
public sealed class DesktopAppNetworkCatalog
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private static readonly HashSet<string> SkipNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "smss", "csrss", "wininit", "services", "lsass",
        "svchost", "fontdrvhost", "dwm", "Memory Compression", "Secure System"
    };

    private IReadOnlyList<InstalledAppInfo>? _cache;
    private DateTimeOffset _cacheUtc = DateTimeOffset.MinValue;

    public Task<IReadOnlyList<InstalledAppInfo>> GetNetworkAppsAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (!forceRefresh &&
            _cache is not null &&
            DateTimeOffset.UtcNow - _cacheUtc < CacheTtl)
        {
            return Task.FromResult(_cache);
        }

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var list = LoadProcesses(cancellationToken);
            _cache = list;
            _cacheUtc = DateTimeOffset.UtcNow;
            return (IReadOnlyList<InstalledAppInfo>)list;
        }, cancellationToken);
    }

    public Task<IReadOnlyDictionary<string, AppTrafficSnapshot>> GetAppTrafficAsync(
        IReadOnlyList<string> ids,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, AppTrafficSnapshot>>(
            new Dictionary<string, AppTrafficSnapshot>(StringComparer.OrdinalIgnoreCase));

    private static List<InstalledAppInfo> LoadProcesses(CancellationToken cancellationToken)
    {
        var selfName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "v2rayF");
        var byId = new Dictionary<string, InstalledAppInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var process in Process.GetProcesses())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using (process)
                {
                    var name = process.ProcessName;
                    if (string.IsNullOrWhiteSpace(name) || SkipNames.Contains(name))
                        continue;

                    string fileName;
                    string display;
                    try
                    {
                        var path = process.MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            fileName = Path.GetFileName(path);
                            display = Path.GetFileNameWithoutExtension(path);
                        }
                        else
                        {
                            fileName = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                                ? name
                                : name + ".exe";
                            display = name;
                        }
                    }
                    catch
                    {
                        // Access denied for some system processes — still list by ProcessName.
                        fileName = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                            ? name
                            : name + ".exe";
                        display = name;
                    }

                    if (byId.ContainsKey(fileName))
                        continue;

                    var isSelf = fileName.Contains("v2rayF", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(name, selfName, StringComparison.OrdinalIgnoreCase);

                    byId[fileName] = new InstalledAppInfo
                    {
                        Id = fileName,
                        DisplayName = display,
                        IsSelf = isSelf
                    };
                }
            }
            catch
            {
                // Ignore processes that disappear mid-enumeration.
            }
        }

        return byId.Values
            .OrderBy(a => a.IsSelf ? 0 : 1)
            .ThenBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(400)
            .ToList();
    }
}
