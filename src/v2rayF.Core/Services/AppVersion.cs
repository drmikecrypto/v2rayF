using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace v2rayF.Services;

public static class AppVersion
{
    private static string? _override;

    /// <summary>Prefer platform package version (e.g. Android VersionName) when available.</summary>
    public static void OverrideCurrent(string version) =>
        _override = string.IsNullOrWhiteSpace(version) ? null : Normalize(version);

    public static string Current =>
        _override
        ?? ReadInformationalVersion(Assembly.GetEntryAssembly())
        ?? ReadInformationalVersion(Assembly.GetExecutingAssembly())
        ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
        ?? "0.0.0";

    public static bool IsNewerThanCurrent(string remoteTagOrVersion)
    {
        var remote = ToComparable(Normalize(remoteTagOrVersion));
        var current = ToComparable(Normalize(Current));
        return remote > current;
    }

    public static string Normalize(string value)
    {
        value = value.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
            value = value[1..];

        // Strip SemVer pre-release / build metadata: 1.4.3-beta+git → 1.4.3
        var plus = value.IndexOf('+');
        if (plus >= 0)
            value = value[..plus];
        var dash = value.IndexOf('-');
        if (dash >= 0)
            value = value[..dash];

        return value.Trim();
    }

    /// <summary>Parse to a 4-part Version so 1.4.3 and 1.4.3.0 compare equal.</summary>
    public static Version ToComparable(string normalized)
    {
        if (!Version.TryParse(normalized, out var parsed))
            return new Version(0, 0, 0, 0);

        var major = Math.Max(parsed.Major, 0);
        var minor = Math.Max(parsed.Minor, 0);
        var build = parsed.Build < 0 ? 0 : parsed.Build;
        var revision = parsed.Revision < 0 ? 0 : parsed.Revision;
        return new Version(major, minor, build, revision);
    }

    private static string? ReadInformationalVersion(Assembly? assembly)
    {
        if (assembly is null)
            return null;

        try
        {
            var info = assembly
                .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), inherit: false)
                .OfType<AssemblyInformationalVersionAttribute>()
                .FirstOrDefault()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
                return Normalize(info);
        }
        catch
        {
            // ignore
        }

        return null;
    }

    /// <summary>Windows/macOS/Linux: prefer FileVersion of the running executable when present.</summary>
    public static void TryOverrideFromEntryExecutable()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
                return;

            var fvi = FileVersionInfo.GetVersionInfo(path);
            var candidate = fvi.ProductVersion ?? fvi.FileVersion;
            if (!string.IsNullOrWhiteSpace(candidate))
                OverrideCurrent(candidate);
        }
        catch
        {
            // Keep assembly version.
        }
    }
}
