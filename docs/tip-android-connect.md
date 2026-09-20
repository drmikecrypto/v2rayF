# Tip: Android Connect troubleshooting

## Current defaults (2.6.3.5+)

- **Daily mode** (default assist posture): Chromium HTTP proxy assist **on** — Play Store / Translate via `10809`; Meta feed/CDN QUIC shoved to TCP→10809
- **Gaming Boost**: assist **off** + fragment/Survive **off** + multipath **on** + UDP-aware Smart Connect — full TUN; expect Play/Translate to fail (lab vs V2Box: [`tips/game-v2box-scorecard.md`](tips/game-v2box-scorecard.md)). Details: [`tips/gaming-boost.md`](tips/gaming-boost.md)
- **Sentinel / Iran / China**: leak-oriented presets; assist **on**
- Connect green requires SOCKS. Fail-closed only if VpnService **Network is missing**. gen204/FCM miss → weak-TUN tip + soft rebind (not Connect failure). HTTP `10809` remains advisory for Play + Instagram feed.
- Lock/unlock: soft session recovery force-rebinds TUN when needed (no manual Disconnect).
- Path diagnostics line is **off** unless Settings → **Show path diagnostics while Connected**
- **Copy session diagnostics** (Settings) exports Connect → SOCKS → TUN → soft recovery timeline — see [`tips/phase-c.md`](tips/phase-c.md)
- **Secure Share**: LAN SOCKS/HTTP so other devices use this phone’s tunnel (set proxy on clients; OEM hotspot bypasses VPN) — [`tips/secure-share.md`](tips/secure-share.md)
- Android TUN stack = **gvisor** (do not enable experimental system/mixed without sandbox flags)

1. Prefer the in-app **Update** button when a new GitHub release is available — it downloads the signed APK, verifies SHA256, and installs over the existing app (native cores refresh automatically).
2. Tap **Connect** and allow the **VPN** permission when prompted.
3. If connect fails with **TUN path failed**, the VPN Network was missing after start — try again or reinstall the release APK. A weak-TUN tip while Connected is advisory (not a hard fail).
4. Uninstall first only if the installer reports a **signature mismatch** (very old sideload builds before stable signing).
5. Keep Private DNS **Off** (Settings → Network → Private DNS). Opportunistic/strict Private DNS breaks VPN DNS hijack — the app warns when it detects this. After a TUN/DNS change, **force-stop Instagram once** if Direct feed pull-to-refresh stalls (Connected status tips this once).
6. **VLESS/VMess** can show fast Test delay then Connected with no system internet: latency is SOCKS TCP; TUN DNS is UDP via the proxy. Live TUN injects `packet_encoding=xudp` when the link omits it (any transport/TLS/REALITY; Vision skipped — core already muxes UDP). Shadowsocks carries UDP natively. If it still fails, Copy session diagnostics and compare SOCKS vs TUN.
7. Use **Daily** / **Gaming Boost** / **Iran** / **China** / **Sentinel** presets in Settings (Save settings to persist).
8. fa/zh Private DNS + battery: [`tips/fa-zh-connect.md`](tips/fa-zh-connect.md). Subscription mirrors: [`tips/subscription-mirrors.md`](tips/subscription-mirrors.md).

## v2.6.3.4 — lock/unlock Connected blackhole

- Resume refuses SOCKS-only “healthy” when TUN weak; SessionResume force-rebinds (90s throttle)
- Unlock fires session recovery without opening the app (`USER_PRESENT` / `SCREEN_ON`)

## v2.6.3.3 — Instagram feed stall (send/likes OK)

- Meta feed/CDN UDP/443 block when assist on; MQTT exclusions unchanged
- Async 10809 advisory tip + one-shot force-stop Instagram tip

## v2.6.3.2 — Connect false-negative fix

- gen204/FCM miss no longer blocks Connect; fail-closed only when VPN Network is missing

## v2.6.3.1 — fail-closed TUN (no blackhole Connected)

- SOCKS OK + TUN dead → rebind once, then fail Connect (tears VPN) — **over-corrected** in 2.6.3.2 for HTTPS flap
- Faster Connect (no HTTP 10809 on critical path)

## v2.6.3.0 — Daily / Gaming modes + optional path truth

- Named Daily / Gaming / Sentinel assist postures
- Optional path diagnostics (default off)
- Connected status re-surfaces weak HTTP/TUN and OEM SetHttpProxy failure (D12)

## v2.6.2.21 — Chromium assist on again

- Default **on** (Play Store / Translate like v2.6.2.19); Upgrade migrates assist off → on once
- Toggle still available; **off** breaks Play (same as 2.3.1) — reconnect after changing
- Narco/V2Box full parity not claimed yet (gVisor vs system stack is separate)

## v2.6.2.20 — Chromium HTTP assist off by default

- Default: full TUN (no `SetHttpProxy`) — Unity/games like Narco Empire stay off HTTP CONNECT
- Settings → **Chromium HTTP proxy assist (10809)** ON only if Play Store / Translate need it
- Reconnect after toggling
- Connected status is clean (no Instagram force-stop tip; no path-truth line)
- **Paste** uses Android system clipboard when Avalonia clipboard fails — or paste into the box and tap **Add**
- **Superseded for Google:** use **2.6.2.21+** (assist default on)

## v2.6.2.19 — Xray Test All

- Classic Test All / rank uses **Xray** again (undo D13 / restore v2.2.3)
- Live Connect still sing-box TUN; Hy2/TUIC/WG still sing-box probes
- Confirm Reality + WS + SS show ms before treating this build as ship-ready

## v2.6.2.18 — HTTP 10809 advisory

- Connect green = SOCKS; Android HTTP `10809` no longer hard-fails Connect when SOCKS works
- HTTP probe: 8s+ connect budget, Vision single gen204, warmup after SOCKS
- Test All / rank: warm-then-measure; Reality ready wait 5s

## v2.6.2.17 — Test All TIMEOUT

- Sing-box Test All no longer resolves gen204 on clearnet (`ip_is_private` / 1.1.1.1) — SS/VLESS/Trojan were false TIMEOUT
- Rank budget **12s for all** protocols; Vision/REALITY rank uses one gen204 URL (HTTP/1.1)

## v2.6.2.16 — probes

- Test All / rank Vision-REALITY budget: **12s** (was 4s hard cap)
- SOCKS gen204 resolves DNS **through the core** (not poisoned clearnet DNS)
- Android HTTP `10809` gets a fresh budget after SOCKS OK

## v2.6.2.15 — Connect gate

- TUN app-path is advisory at Connect again (fixes REALITY/Vision false TIMEOUT vs other clients)
- SOCKS (+ Android HTTP 10809) remain the hard gate; TUN probed after localhost with a fresh budget

## v2.6.2.14 — trusted tunnel

- Iran/China profiles; Bypass China rule-sets; Private DNS warning; subscription mirrors
- Note: D13 also forced sing-box speedtest (universal Test All TIMEOUT) — fixed in **2.6.2.19**
- Note: hard TUN Connect gate caused false timeouts — use 2.6.2.15+

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
