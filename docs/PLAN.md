# v2rayF PLAN — From glued client to trusted tunnel

Single source of truth for product direction. Older notes in [`roadmap-engine-first.md`](roadmap-engine-first.md) point here.

## Operating rules

- GitHub push / public release only when the maintainer explicitly asks (authorized for **v2.6.2.20** Chromium HTTP assist off + UI cleanup + Paste fix).
- Local builds, APK sideloads, and private testing are fine between releases.
- North star: drop any valid config → **every app on the device** reaches the internet through that exit — not “browser works, Instagram Direct dies.”

## Focus

**Android-first** (Iran/China phones) for 3–6 months. Windows follows once Android full-tunnel QA passes. macOS/Linux TUN/kill-switch after that.

## Success criteria (Connect green)

Connect may show Connected when:

1. Core process is alive, and
2. Local SOCKS probe passes.

On Android sing-box TUN: HTTP proxy `10809` and TUN gen204/FCM are **advisory** at Connect (soft retry / status tip if weak). Hard-requiring HTTP or TUN at Connect caused false timeouts on cold REALITY/Vision (HTTP: v2.6.2.17→18; TUN: v2.6.2.14→15).

## Golden app matrix

Same subscription, clean install, Private DNS **Off**. Mark pass/fail (not Mbps alone).

| App | Check |
|-----|--------|
| Chrome | HTTPS page load |
| Instagram | Feed + **Direct** (MQTT) |
| WhatsApp | Chat send/receive |
| Telegram | Chat + media |
| YouTube | Video playback |
| Maps | Load + search |
| Play Services | FCM push arrives |
| One UDP game / voice | Real-time UDP path |

**Scorecard:** run v2rayF vs v2rayNG vs V2Box on the same phone and link; file results under private notes (do not commit credentials).

## Phases

### Phase 0 — Freeze the leak of trust

- [x] Codify success criteria + golden matrix (this file)
- [x] Scorecard process documented
- [x] Release gate: maintainer approves every push

### Phase 1 — One live engine on Android

- [x] Android live Connect prefers sing-box when `PreferSingBoxOnAndroid`; classic Test All stays on Xray (`UseSingBoxForSpeedtest` = `RequiresSingBox` only — restore after D13 / v2.2.3)
- [x] TUN fd lifecycle documented as one state machine (`AndroidTunLifecycle` + rebind policy)
- [x] CI / package-android: fail if unpatched sing-box in release path (D18)

**Exit gate:** Instagram Direct + WhatsApp + Chrome on clean install without manual App Network tweaks.

### Phase 2 — Full-tunnel correctness

- [x] DNS: keep real UDP via proxy + IPv6 fail-closed; Private DNS conflict UX on Android
- [x] Bypass China on sing-box via rule-sets (no longer silent Bypass LAN stub)
- [x] IR / CN network presets (one-tap profiles)
- [x] Prefer tunnel+DNS over growing exception lists; document shrink policy
- [x] UDP/QUIC golden checks documented (manual scorecard)

**Exit gate:** golden matrix green on IR-like and CN-like path (or lab equivalent).

### Phase 3 — Config completeness

- [x] Unsupported SIP003 / exotic plugins: hard refuse with reason (never silent “connected”)
- [x] Unsupported schemes (Hysteria1, Naive, SSR, …) return refuse hints
- [x] Subscription mirror hints + auto-retry for GitHub/raw (CN/IR)

### Phase 4 — Desktop parity + innovation (after Android wins)

- [x] Path truth UI (TUN / HTTP assist / Direct counts) while Connected
- [x] Scorecard template export (Settings)
- [x] Desktop TUN / kill-switch honesty on macOS/Linux (`TunRequirementMessage`)
- Windows TUN reliability bar (ongoing)
- Then UX / multipath / diagnostics (former “Phase C”)

### Phase 5 — Distribution for CN/IR

- [x] Sideload + in-app update primary (documented); subscription mirrors tip
- [x] fa / zh tips (Private DNS, battery) — `docs/tips/fa-zh-connect.md`
- [x] Battery exemption re-prompt after revoke (D14)
- No Play Store dependency for core updates

## What we will not do

- One-off domain/package patches without a golden-matrix failure and a removal plan
- UX redesign as a substitute for tunnel correctness
- iOS before Android full-tunnel bar
- Silent Survive / traffic rewrite without consent
