using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace v2rayF.Services;

public static class UpdateDownloadHelper
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(20)
    };

    private static readonly string[] AllowedDownloadHosts =
    [
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
        "github-releases.githubusercontent.com"
    ];

    static UpdateDownloadHelper()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd($"v2rayF/{AppVersion.Normalize(AppVersion.Current)}");
    }

    public static void EnsureAllowedDownloadUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Update download URL must be HTTPS.");

        if (!AllowedDownloadHosts.Any(h =>
                uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Update host '{uri.Host}' is not allowed.");
    }

    public static async Task<string> DownloadAsync(
        string url,
        string destPath,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        EnsureAllowedDownloadUrl(url);
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

        progress?.Report("Downloading update…");
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = File.Create(destPath);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        return destPath;
    }

    /// <summary>Verifies SHA256 hex digest of a file. Throws on mismatch.</summary>
    public static void VerifySha256(string filePath, string? expectedSha256Hex)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256Hex))
            throw new InvalidOperationException("Update package has no SHA256 checksum to verify.");

        var expected = expectedSha256Hex.Trim().ToLowerInvariant();
        if (expected.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            expected = expected["sha256:".Length..].Trim();

        using var stream = File.OpenRead(filePath);
        var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!hash.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Update SHA256 mismatch — refusing to install.");
    }

    public static string ExtractZip(string zipPath, string extractDir, IProgress<string>? progress)
    {
        progress?.Report("Unpacking…");
        if (Directory.Exists(extractDir))
            Directory.Delete(extractDir, true);
        Directory.CreateDirectory(extractDir);

        ExtractZipSafe(zipPath, extractDir);
        return extractDir;
    }

    /// <summary>Extracts a zip rejecting Zip-Slip (absolute / parent-traversal entry names).</summary>
    public static void ExtractZipSafe(string zipPath, string extractDir)
    {
        var fullRoot = Path.GetFullPath(extractDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name) && entry.FullName.EndsWith('/'))
            {
                // Directory entry — create after validation.
            }

            var relative = entry.FullName.Replace('\\', '/');
            if (Path.IsPathRooted(relative) ||
                relative.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(p => p == ".."))
                throw new InvalidOperationException($"Zip entry path is unsafe: {entry.FullName}");

            var destination = Path.GetFullPath(Path.Combine(extractDir, entry.FullName));
            if (!destination.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) &&
                !destination.Equals(fullRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Zip entry escaped target directory: {entry.FullName}");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }
    }
}
