using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace v2rayF.Services;

public static class UpdateDownloadHelper
{
    public const int MaxDownloadAttempts = 3;

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

        var partialPath = destPath + ".partial";
        Exception? lastError = null;

        for (var attempt = 1; attempt <= MaxDownloadAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                progress?.Report(attempt == 1
                    ? "Downloading update…"
                    : $"Retrying download ({attempt}/{MaxDownloadAttempts})…");

                await DownloadAttemptAsync(url, destPath, partialPath, progress, cancellationToken)
                    .ConfigureAwait(false);
                return destPath;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsTransientDownloadError(ex) && attempt < MaxDownloadAttempts)
            {
                lastError = ex;
                var delayMs = 500 * (1 << (attempt - 1));
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }
        }

        throw lastError ?? new InvalidOperationException("Update download failed.");
    }

    private static bool IsTransientDownloadError(Exception ex)
    {
        if (ex is IOException or TimeoutException)
            return true;
        if (ex is InvalidOperationException ioe &&
            ioe.Message.Contains("Content-Length", StringComparison.Ordinal))
            return true;
        if (ex is HttpRequestException http)
        {
            var code = http.StatusCode;
            return code is null or
                System.Net.HttpStatusCode.RequestTimeout or
                System.Net.HttpStatusCode.TooManyRequests or
                >= System.Net.HttpStatusCode.InternalServerError;
        }

        return false;
    }

    private static async Task DownloadAttemptAsync(
        string url,
        string destPath,
        string partialPath,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        long existing = 0;
        if (File.Exists(partialPath))
            existing = new FileInfo(partialPath).Length;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0)
            request.Headers.Range = new RangeHeaderValue(existing, null);

        using var response = await Http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);

        var resume = existing > 0 && response.StatusCode == System.Net.HttpStatusCode.PartialContent;
        if (!resume && existing > 0 && response.IsSuccessStatusCode)
        {
            // Server ignored Range — restart.
            existing = 0;
            try { File.Delete(partialPath); }
            catch { /* ignore */ }
        }

        if (!resume)
            response.EnsureSuccessStatusCode();
        else if (response.StatusCode != System.Net.HttpStatusCode.PartialContent &&
                 !response.IsSuccessStatusCode)
            response.EnsureSuccessStatusCode();

        var expectedTotal = response.Content.Headers.ContentLength;
        if (resume && expectedTotal is long remaining)
            expectedTotal = existing + remaining;
        else if (!resume)
            expectedTotal = response.Content.Headers.ContentLength;

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        await using (var output = new FileStream(
                         partialPath,
                         resume ? FileMode.Append : FileMode.Create,
                         FileAccess.Write,
                         FileShare.None))
        {
            var buffer = new byte[81920];
            long written = existing;
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                       .ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                written += read;
                if (expectedTotal is > 0 && written % (512 * 1024) < read)
                {
                    var pct = (int)(written * 100 / expectedTotal.Value);
                    progress?.Report($"Downloading update… {pct}%");
                }
            }

            if (expectedTotal is long len && written != len)
                throw new InvalidOperationException(
                    $"Content-Length mismatch: expected {len} bytes, got {written}.");
        }

        if (File.Exists(destPath))
            File.Delete(destPath);
        File.Move(partialPath, destPath);
    }

    /// <summary>Verifies SHA256 hex digest of a file. Throws on mismatch.</summary>
    public static void VerifySha256(string filePath, string? expectedSha256Hex)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256Hex))
            throw new InvalidOperationException("Update package has no SHA256 checksum to verify.");

        var expected = expectedSha256Hex.Trim().ToLowerInvariant();
        if (expected.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            expected = expected["sha256:".Length..].Trim();

        string hash;
        using (var stream = File.OpenRead(filePath))
            hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();

        if (!hash.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(filePath); }
            catch { /* ignore */ }
            try { File.Delete(filePath + ".partial"); }
            catch { /* ignore */ }
            throw new InvalidOperationException("Update SHA256 mismatch — refusing to install.");
        }
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
