# Backlog — v2.6.2.14 hygiene (deferred from defect audit)

Tracked from the 2.6.2.12 audit. Items marked **done** landed with the trusted-tunnel PLAN work.

| ID | Finding | Fix | Status |
|----|---------|-----|--------|
| D12 | Connected ignores TUN and SetHttpProxy OEM failure | StatusText weak proxy/TUN; re-show HTTP proxy warning | open |
| D13 | Android speedtest = Xray; live = sing-box | Speedtest use PreferSingBox path on Android | **done** |
| D14 | Battery exemption never re-prompts after revoke | Clear prompt flag when OS still optimizing | **done** |
| D15 | StatusSanitizer misses hy2/tuic/anytls/wg and logcat dumps | Extend schemes + scrub | open |
| D16 | tip/desktop docs TunOnlyFailThreshold 2 vs code 6 | Living tips → 6 | open |
| D17 | `Defaults_SurviveAndDoH_Off` misnamed | Rename | open |
| D18 | CI `-AllowUnpatchedSingBox` | Build patched lib or fail if stock | **done** |
| D19 | Release workflow installs Go/NDK on all RIDs | Gate `if: matrix.android` | **done** |
| D20 | `SECURITY.md` still 1.4.x | Support 2.6.x; Sponsors/advisories contact | open |
| D21 | `llms.txt` 1.4 / ProcessBuilder stale | Align DualCore 2.6.x | open |
| D22 | Version scattered in 4 csproj | Optional Directory.Build.props + CI assert | open |
| D23 | Local `dist/` / `.tools/` junk | `scripts/clean-artifacts.ps1` only | open |
