using System;
using System.Collections.Generic;
using System.Linq;
using v2rayF.Models;

namespace v2rayF.Services;

/// <summary>
/// Parse/normalize App Network Direct/Block lists. Direct wins over Block for the same id.
/// </summary>
public static class AppNetworkPolicy
{
    public const string AndroidSelfPackage = "com.drmikecrypto.v2rayf";

    public static IReadOnlyList<string> ParseIdList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        return raw
            .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string SerializeIdList(IEnumerable<string> ids) =>
        string.Join('\n',
            ids.Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Direct list for VpnService disallow / desktop process→direct.
    /// Never includes the v2rayF package (already excluded from the tunnel by the OS builder).
    /// </summary>
    public static IReadOnlyList<string> GetDirectIds(AppSettings settings, bool mobile) =>
        FilterSelf(ParseIdList(mobile ? settings.AndroidBypassPackages : settings.DesktopDirectProcesses), mobile);

    /// <summary>
    /// Block list for core routing. Excludes ids that are also Direct (Direct never hits TUN).
    /// </summary>
    public static IReadOnlyList<string> GetBlockIds(AppSettings settings, bool mobile)
    {
        var direct = new HashSet<string>(GetDirectIds(settings, mobile), StringComparer.OrdinalIgnoreCase);
        var blockRaw = ParseIdList(mobile ? settings.AndroidBlockPackages : settings.DesktopBlockProcesses);
        return FilterSelf(blockRaw.Where(id => !direct.Contains(id)), mobile);
    }

    public static AppNetworkMode GetMode(AppSettings settings, string id, bool mobile)
    {
        if (IsSelfId(id, mobile))
            return AppNetworkMode.Vpn;

        var comparer = StringComparer.OrdinalIgnoreCase;
        if (GetDirectIds(settings, mobile).Contains(id, comparer))
            return AppNetworkMode.Direct;
        if (GetBlockIds(settings, mobile).Contains(id, comparer))
            return AppNetworkMode.Block;
        return AppNetworkMode.Vpn;
    }

    /// <summary>Apply mode to settings lists. Self id is ignored.</summary>
    public static void SetMode(AppSettings settings, string id, AppNetworkMode mode, bool mobile)
    {
        if (string.IsNullOrWhiteSpace(id) || IsSelfId(id, mobile))
            return;

        id = id.Trim();
        var direct = ParseIdList(mobile ? settings.AndroidBypassPackages : settings.DesktopDirectProcesses)
            .ToList();
        var block = ParseIdList(mobile ? settings.AndroidBlockPackages : settings.DesktopBlockProcesses)
            .ToList();

        direct.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
        block.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));

        switch (mode)
        {
            case AppNetworkMode.Direct:
                direct.Add(id);
                break;
            case AppNetworkMode.Block:
                block.Add(id);
                break;
        }

        var serializedDirect = SerializeIdList(direct);
        var serializedBlock = SerializeIdList(block);
        if (mobile)
        {
            settings.AndroidBypassPackages = serializedDirect;
            settings.AndroidBlockPackages = serializedBlock;
        }
        else
        {
            settings.DesktopDirectProcesses = serializedDirect;
            settings.DesktopBlockProcesses = serializedBlock;
        }
    }

    public static bool IsSelfId(string id, bool mobile) =>
        mobile
            ? string.Equals(id, AndroidSelfPackage, StringComparison.OrdinalIgnoreCase)
            : id.Contains("v2rayF", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> FilterSelf(IEnumerable<string> ids, bool mobile) =>
        ids.Where(id => !IsSelfId(id, mobile)).ToList();
}
