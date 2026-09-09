# Tip: Android Connect troubleshooting

1. Prefer the in-app **Update** button when a new GitHub release is available — it downloads the signed APK, verifies SHA256, and installs over the existing app (native cores refresh automatically).
2. Tap **Connect** and allow the **VPN** permission when prompted.
3. If connect fails, read the status message — the app tears down VPN so normal internet keeps working.
4. Uninstall first only if the installer reports a **signature mismatch** (very old sideload builds before stable signing).
5. Keep Private DNS **Off**. After a TUN/DNS change, force-stop Instagram/WhatsApp once if sockets were stale.

## v2.6.2.13 — force TUN rebind

- TunPathFailed force-rebinds VPN even when bypass hash matches (fixes no-op rebind)
- Failed establish after teardown clears TUN fd (no Refresh with dead fd)
- Connect/core status DualCore-aware; GMS/GSF UI Direct/Block only
- Drop no-op TUN DNS carve-outs; delete unused FakeIP constants / ForegroundService stub

## v2.6.2.12 — GMS clearnet; IG Direct parity

- GMS/GSF Direct by default; expanded Meta MQTT exclusions; Meta routes like messaging
- TunPathFailed always rebinds; apex google.com removed from UDP/443 block

## v2.6.2.11 — notify Stop; games/push UDP

- Status bar opens app; Stop disconnects and exits
- Google UDP/443 narrowed; soft TUN rebind throttled to 90s

## v2.6.2.10 — messenger TUN rebind

- Soft recovery re-establishes Android VPN (new fd) before RefreshRuntime
- FCM hosts beat Google UDP/443 block; status shows TUN recovering/weak briefly

## v2.6.2.9 — Reality/Vision budgets + TUN sniff override

- Android TUN: sniff on, `sniff_override_destination` off
- Vision/REALITY: longer resume/dial/ready budgets; tun-only soft threshold **6**
- Private DNS should stay Off on device

## v2.6.2.8 — TUN real UDP DNS

- FakeIP catch-all removed; TUN DNS is real UDP via proxy (`dns.final`)
- Block IPv6: early AAAA reject
- Private DNS Off; force-stop apps once if sockets were stale across the update
