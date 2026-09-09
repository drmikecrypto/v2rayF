using System.Collections.Generic;
using System.Text;

namespace v2rayF.Services;

/// <summary>Manual golden-matrix scorecard for testers (export markdown).</summary>
public static class ConnectivityScorecard
{
    public static IReadOnlyList<string> AppChecks { get; } =
    [
        "Chrome HTTPS",
        "Instagram feed",
        "Instagram Direct",
        "WhatsApp chat",
        "Telegram chat/media",
        "YouTube playback",
        "Maps load/search",
        "Play Services FCM push",
        "UDP game or voice"
    ];

    public static string ExportMarkdown(
        string clientLabel,
        IReadOnlyDictionary<string, bool?> results,
        string? notes = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Connectivity scorecard — {clientLabel}");
        sb.AppendLine();
        sb.AppendLine("| Check | Pass |");
        sb.AppendLine("|-------|------|");
        foreach (var check in AppChecks)
        {
            results.TryGetValue(check, out var pass);
            var cell = pass is null ? " " : pass.Value ? "yes" : "no";
            sb.AppendLine($"| {check} | {cell} |");
        }

        if (!string.IsNullOrWhiteSpace(notes))
        {
            sb.AppendLine();
            sb.AppendLine("## Notes");
            sb.AppendLine(notes.Trim());
        }

        return sb.ToString();
    }
}
