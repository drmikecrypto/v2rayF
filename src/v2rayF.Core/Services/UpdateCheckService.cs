using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace v2rayF.Services;

public sealed class UpdateCheckService
{
    private const string Repo = "drmikecrypto/v2rayF";
    private static readonly HttpClient Http = CreateClient();
    private readonly SemaphoreSlim _gate = new(1, 1);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("v2rayF", AppVersion.Normalize(AppVersion.Current)));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    /// <summary>Legacy helper — returns offer or null (does not distinguish errors).</summary>
    public async Task<UpdateOffer?> CheckAsync(string releaseAssetFileName, CancellationToken cancellationToken = default)
    {
        var result = await CheckDetailedAsync(releaseAssetFileName, cancellationToken).ConfigureAwait(false);
        return result.Offer;
    }

    public async Task<UpdateCheckResult> CheckDetailedAsync(
        string releaseAssetFileName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(releaseAssetFileName))
            return UpdateCheckResult.MissingAsset("Release asset name is empty.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await CheckCoreAsync(releaseAssetFileName, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<UpdateCheckResult> CheckCoreAsync(
        string releaseAssetFileName,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await Http.GetAsync(
                $"https://api.github.com/repos/{Repo}/releases/latest",
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return UpdateCheckResult.Transient("Update check timed out.");
        }
        catch (HttpRequestException ex)
        {
            return UpdateCheckResult.Transient($"GitHub unreachable: {ex.Message}");
        }
        catch (Exception ex)
        {
            return UpdateCheckResult.Transient(ex.Message);
        }

        using (response)
        {
            if ((int)response.StatusCode is 408 or 429 or >= 500)
                return UpdateCheckResult.Transient($"GitHub HTTP {(int)response.StatusCode}");

            if (!response.IsSuccessStatusCode)
                return UpdateCheckResult.Transient($"GitHub HTTP {(int)response.StatusCode}");

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var root = doc.RootElement;

            var tag = root.GetProperty("tag_name").GetString();
            if (string.IsNullOrWhiteSpace(tag) || !AppVersion.IsNewerThanCurrent(tag))
                return UpdateCheckResult.UpToDate();

            if (!root.TryGetProperty("assets", out var assets))
                return UpdateCheckResult.MissingAsset("Release has no assets.");

            string? checksumsUrl = null;
            string? downloadUrl = null;
            string? digest = null;

            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (name.Equals("SHA256SUMS", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
                {
                    checksumsUrl = asset.GetProperty("browser_download_url").GetString();
                    continue;
                }

                if (!name.Equals(releaseAssetFileName, StringComparison.OrdinalIgnoreCase))
                    continue;

                downloadUrl = asset.GetProperty("browser_download_url").GetString();
                if (asset.TryGetProperty("digest", out var digestProp))
                {
                    var d = digestProp.GetString();
                    if (!string.IsNullOrWhiteSpace(d) && d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                        digest = d["sha256:".Length..].Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(downloadUrl))
                return UpdateCheckResult.MissingAsset($"No asset named {releaseAssetFileName}.");

            try
            {
                UpdateDownloadHelper.EnsureAllowedDownloadUrl(downloadUrl);
            }
            catch (Exception ex)
            {
                return UpdateCheckResult.MissingAsset(ex.Message);
            }

            var sha256 = digest;
            if (string.IsNullOrWhiteSpace(sha256) && !string.IsNullOrWhiteSpace(checksumsUrl))
            {
                try
                {
                    UpdateDownloadHelper.EnsureAllowedDownloadUrl(checksumsUrl);
                    sha256 = await TryParseSha256SumsAsync(checksumsUrl, releaseAssetFileName, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (HttpRequestException ex)
                {
                    return UpdateCheckResult.Transient($"Checksums download failed: {ex.Message}");
                }
            }

            if (string.IsNullOrWhiteSpace(sha256))
                return UpdateCheckResult.MissingAsset("Release has no SHA256 checksum.");

            return UpdateCheckResult.WithOffer(new UpdateOffer
            {
                Tag = tag,
                Version = AppVersion.Normalize(tag),
                DownloadUrl = downloadUrl,
                AssetFileName = releaseAssetFileName,
                Sha256 = sha256
            });
        }
    }

    private static async Task<string?> TryParseSha256SumsAsync(
        string url,
        string assetFileName,
        CancellationToken cancellationToken)
    {
        var text = await Http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length < 66)
                continue;

            var parts = trimmed.Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                continue;

            var filePart = parts[1].Trim().TrimStart('*');
            if (!filePart.Equals(assetFileName, StringComparison.OrdinalIgnoreCase) &&
                !filePart.EndsWith('/' + assetFileName, StringComparison.OrdinalIgnoreCase) &&
                !filePart.EndsWith('\\' + assetFileName, StringComparison.OrdinalIgnoreCase))
                continue;

            return parts[0].Trim();
        }

        return null;
    }
}
