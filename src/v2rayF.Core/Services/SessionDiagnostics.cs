using System;
using System.Collections.Generic;
using System.Text;

namespace v2rayF.Services;

/// <summary>
/// Phase C session timeline: Connect → SOCKS → TUN → soft recovery / multipath notes.
/// Copy via Settings; do not commit share links or phone logs.
/// </summary>
public sealed class SessionDiagnostics
{
    public const int MaxEvents = 48;

    private readonly object _gate = new();
    private readonly List<(DateTimeOffset Utc, string Text)> _events = new();

    public void Clear()
    {
        lock (_gate)
            _events.Clear();
    }

    public void Record(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        lock (_gate)
        {
            _events.Add((DateTimeOffset.UtcNow, text.Trim()));
            while (_events.Count > MaxEvents)
                _events.RemoveAt(0);
        }
    }

    public IReadOnlyList<(DateTimeOffset Utc, string Text)> Snapshot()
    {
        lock (_gate)
            return _events.ToArray();
    }

    /// <summary>Human-readable blob for clipboard export.</summary>
    public string Export(
        string productVersion,
        string? serverLabel,
        bool socksOk,
        bool? httpWeak,
        bool? tunWeak,
        int? socksProbeMs,
        int? tunMs,
        bool gamingBoost,
        bool multipath,
        int consecutivePathFails,
        string? multipathHint,
        string? extraNotes = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# v2rayF session diagnostics");
        sb.AppendLine();
        sb.AppendLine($"Generated (UTC): {DateTimeOffset.UtcNow:o}");
        sb.AppendLine($"Version: {productVersion}");
        if (!string.IsNullOrWhiteSpace(serverLabel))
            sb.AppendLine($"Server: {serverLabel}");
        sb.AppendLine($"Mode: {(gamingBoost ? "Gaming Boost" : "Daily/other")}" +
                      (multipath ? " · Smart Multipath on" : ""));
        sb.AppendLine();
        sb.AppendLine("## Gate snapshot");
        sb.AppendLine($"- SOCKS: {(socksOk ? "ok" : "fail")}" +
                      (socksProbeMs is > 0 ? $" ({socksProbeMs} ms)" : ""));
        if (httpWeak is true)
            sb.AppendLine("- HTTP assist: **weak** (Play/Translate / Chromium may need reconnect)");
        else if (httpWeak is false)
            sb.AppendLine("- HTTP assist: ok or not probed");
        if (tunWeak is true)
        {
            sb.AppendLine(tunMs == LatencyService.TunVpnMissingMs
                ? "- TUN: **VPN/adapter missing** (TunVpnMissingMs — fail-closed path)"
                : "- TUN: **weak** (app-path probe; messengers may need force-stop / soft recovery)");
        }
        else if (tunWeak is false)
            sb.AppendLine("- TUN: ok or not required");
        if (consecutivePathFails > 0)
            sb.AppendLine($"- Path fails (flat traffic): {consecutivePathFails}");
        if (!string.IsNullOrWhiteSpace(multipathHint))
            sb.AppendLine($"- Multipath: {multipathHint}");
        sb.AppendLine();
        sb.AppendLine("## Timeline (newest last)");
        lock (_gate)
        {
            if (_events.Count == 0)
                sb.AppendLine("(empty — connect once to record)");
            else
            {
                foreach (var (utc, text) in _events)
                    sb.AppendLine($"- {utc:HH:mm:ss}Z  {text}");
            }
        }

        if (!string.IsNullOrWhiteSpace(extraNotes))
        {
            sb.AppendLine();
            sb.AppendLine("## Notes");
            sb.AppendLine(extraNotes.Trim());
        }

        sb.AppendLine();
        sb.AppendLine("Private — do not commit subscriptions or share links.");
        return sb.ToString();
    }

    /// <summary>Status-line recover CTA when SOCKS is green but TUN/HTTP assist is weak.</summary>
    public static string? FormatRecoverCta(bool tunWeak, bool httpWeak, bool gamingBoost)
    {
        if (tunWeak)
        {
            return gamingBoost
                ? "Recover: wait for soft rebind, or Disconnect → Connect (Gaming keeps assist off)"
                : "Recover: wait for soft rebind, or Disconnect → Connect; Daily assist may help Play/Translate";
        }

        if (httpWeak)
            return "Recover: reconnect, or switch Gaming Boost if you need full TUN UDP";

        return null;
    }

    /// <summary>Honest multipath flake tip — never implies silent Survive rewrite.</summary>
    public static string? FormatMultipathFlakeHint(
        bool multipathActive,
        bool gamingBoost,
        int consecutivePathFails)
    {
        if (!multipathActive || consecutivePathFails <= 0)
            return null;

        if (gamingBoost)
        {
            return consecutivePathFails >= 2
                ? "multipath flaking — Soft Multipath will re-rank on reconnect (Survive stays off)"
                : "multipath path probe miss — watching (Survive stays off)";
        }

        return consecutivePathFails >= 2
            ? "multipath flaking — Soft Multipath will re-rank on reconnect"
            : "multipath path probe miss — watching";
    }
}
