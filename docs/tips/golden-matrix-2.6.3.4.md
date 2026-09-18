# Golden matrix soak — v2.6.3.4

Repeatable lab runbook for **trusted tunnel** QA. Fill results privately — **do not commit** share links, subscriptions, or phone logs.

Canonical criteria: [`PLAN.md`](../PLAN.md). Game/UDP sheet: [`game-v2box-scorecard.md`](game-v2box-scorecard.md). Export template: Settings → scorecard / `ConnectivityScorecard`.

## Setup

| Item | Value |
|------|--------|
| Build | Release **v2.6.3.4** (APK + optional desktop zip) |
| Phone | Clean install (uninstall → install). Private DNS **Off**. Battery unrestricted for v2rayF. |
| Config | Same Sentinel subscription / share link for all clients |
| Modes | Daily (HTTP assist on) and Gaming (assist off) |
| Peers | v2rayF · v2rayNG · V2Box |

Optional lab feed: parent-folder Sentinel VPS (`deep_fix.sh`) — keep credentials out of git.

## Android app matrix (pass / fail)

Same link · Daily mode first. Mark each cell; Mbps alone does not pass.

| Check | v2rayF Daily | v2rayF Gaming | v2rayNG | V2Box | Notes |
|-------|--------------|---------------|---------|-------|-------|
| Chrome HTTPS | | | | | |
| Instagram feed | | | | | |
| Instagram Direct (MQTT) | | | | | |
| WhatsApp chat | | | | | |
| Telegram chat + media | | | | | |
| YouTube playback | | | | | |
| Maps load + search | | | | | |
| Play Services FCM push | | | | | |
| UDP game or voice | | | | | Expect Gaming ≈ V2Box |

## Session resume (v2.6.3.4 verify)

This is the lock/unlock blackhole gate — do **not** skip.

1. Connect (Daily) until status is Connected and Chrome HTTPS works.
2. Lock the phone 1–2 minutes (screen off).
3. Unlock — **do not open v2rayF**.
4. Open Chrome → HTTPS must work within a few seconds.
5. Instagram Direct / Play Store still OK after unlock.
6. Optional: open v2rayF — status must not stay “Connected” with no system internet. Weak TUN tip is OK; silent blackhole is a fail.

| Step | Pass? | Notes |
|------|-------|-------|
| Lock → unlock → Chrome without opening app | | |
| Instagram Direct after unlock | | |
| No Connected blackhole (fail-closed or recover) | | |

## Desktop Windows TUN honesty (Phase 4)

Run as Administrator with TUN + kill switch on (Sentinel / Iran / China presets).

| Check | Pass? | Notes |
|-------|-------|-------|
| Connect → system traffic via TUN (not only SOCKS) | | |
| Adapter named `v2rayF` present while Connected | | `Get-NetAdapter -Name v2rayF` |
| Kill switch armed only when TUN up | | Missing adapter → KS not armed |
| Sleep / lock → wake → path recovers or tears down (no blackhole) | | |
| Disconnect restores clearnet | | |

## Exit gates

- **Android:** Daily green on Chrome + Instagram Direct + WhatsApp without manual App Network tweaks; Gaming UDP row matches V2Box; lock/unlock verify passes.
- **Windows:** TUN Connect never means “SOCKS green + dead WinTun + kill switch blackhole.”

When both gates pass, update [`PLAN.md`](../PLAN.md) Phase 4 Windows line to done and only then open former Phase C (UX / multipath / diagnostics).
