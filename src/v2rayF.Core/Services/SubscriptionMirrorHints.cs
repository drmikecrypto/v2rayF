using System;
using System.Collections.Generic;

namespace v2rayF.Services;

/// <summary>CN/IR subscription + GitHub raw access hints and mirror URL builders.</summary>
public static class SubscriptionMirrorHints
{
    /// <summary>
    /// Public reverse-proxy style mirrors for github.com / raw.githubusercontent.com.
    /// Tried only after the original URL fails.
    /// </summary>
    public static readonly string[] GithubProxyPrefixes =
    [
        "https://ghproxy.net/",
        "https://mirror.ghproxy.com/",
        "https://gh.ddlc.top/"
    ];

    public static bool IsGithubHost(Uri uri) =>
        uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
        uri.Host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
        uri.Host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
        uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>Original URL first, then proxy mirrors for GitHub hosts.</summary>
    public static IReadOnlyList<Uri> BuildFetchCandidates(Uri original)
    {
        var list = new List<Uri> { original };
        if (!IsGithubHost(original))
            return list;

        var absolute = original.AbsoluteUri;
        foreach (var prefix in GithubProxyPrefixes)
        {
            if (Uri.TryCreate(prefix + absolute, UriKind.Absolute, out var mirrored))
                list.Add(mirrored);
        }

        return list;
    }

    public static string FormatFailureHint(Uri uri, Exception? error)
    {
        var detail = error?.Message ?? "request failed";
        if (!IsGithubHost(uri))
        {
            return
                $"Subscription failed ({detail}). If the host is blocked in your country, paste a mirror URL or import a file while connected.";
        }

        return
            $"Subscription failed ({detail}). GitHub/raw is often blocked in CN/IR — " +
            "v2rayF already retried public mirrors; try another mirror host, enable Subscription via proxy while Connected, or import a local file. " +
            "See docs/tips/subscription-mirrors.md";
    }
}
