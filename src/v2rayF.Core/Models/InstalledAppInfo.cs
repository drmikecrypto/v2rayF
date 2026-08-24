namespace v2rayF.Models;

/// <summary>Catalog entry for App Network (Android package or desktop process).</summary>
public sealed class InstalledAppInfo
{
    /// <summary>Stable id: package name or process file name (e.g. chrome.exe).</summary>
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>Optional PNG/JPEG bytes for the row icon (kept small by the platform).</summary>
    public byte[]? IconPng { get; init; }

    /// <summary>Android application UID when known; used for TrafficStats.</summary>
    public int? Uid { get; init; }

    /// <summary>True for the v2rayF package/process — mode changes are locked.</summary>
    public bool IsSelf { get; init; }
}
