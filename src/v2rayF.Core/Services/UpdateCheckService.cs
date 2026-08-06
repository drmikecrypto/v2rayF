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

    public async Task<UpdateOffer?> CheckAsync(string releaseAssetFileName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(releaseAssetFileName))
            return null;

        using var response = await Http.GetAsync(
            $"https://api.github.com/repos/{Repo}/releases/latest",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;

        var tag = root.GetProperty("tag_name").GetString();
        if (string.IsNullOrWhiteSpace(tag) || !AppVersion.IsNewerThanCurrent(tag))
            return null;

        if (!root.TryGetProperty("assets", out var assets))
            return null;

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
            return null;

        UpdateDownloadHelper.EnsureAllowedDownloadUrl(downloadUrl);

        var sha256 = digest;
        if (string.IsNullOrWhiteSpace(sha256) && !string.IsNullOrWhiteSpace(checksumsUrl))
        {
            UpdateDownloadHelper.EnsureAllowedDownloadUrl(checksumsUrl);
            sha256 = await TryParseSha256SumsAsync(checksumsUrl, releaseAssetFileName, cancellationToken)
                .ConfigureAwait(false);
        }

        return new UpdateOffer
        {
            Tag = tag,
            Version = AppVersion.Normalize(tag),
            DownloadUrl = downloadUrl,
            AssetFileName = releaseAssetFileName,
            Sha256 = sha256
        };
    }

    private static async Task<string?> TryParseSha256SumsAsync(
        string url,
        string assetFileName,
        CancellationToken cancellationToken)
    {
        try
        {
            var text = await Http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (trimmed.Length < 66)
                    continue;

                // "hash  filename" or "hash *filename"
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
        }
        catch
        {
            // Checksums optional when GitHub digest is missing.
        }

        return null;
    }
}
