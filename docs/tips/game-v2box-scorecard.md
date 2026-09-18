# Game / V2Box scorecard (private lab)

Fill this privately — **do not commit credentials, share links, or subscription URLs**.

Same phone · same Sentinel subscription · Private DNS **Off**.

| Check | v2rayF Daily (assist on) | v2rayF Gaming (assist off) | V2Box | Notes |
|-------|--------------------------|----------------------------|-------|-------|
| Chrome HTTPS | | | | |
| Play Store open | | | | Expect fail on Gaming |
| Google Translate | | | | Expect fail on Gaming |
| Instagram Direct | | | | |
| WhatsApp | | | | |
| Unity game Web Access (e.g. Narco) | | | | |
| Unity lag / DNS-TCP diagnosis | | | | |
| FCM push | | | | |

## Stack note

Shipping Android TUN stack is **gvisor** only. `system` / `mixed` blackholed VpnService in **v2.4.1**. Lab overrides require both:

- `AllowExperimentalAndroidTunStack = true`
- `ExperimentalAndroidTunStack = system|mixed`

Never enable experimental stack in a public release without a sandbox gate and golden-matrix proof.

## After the lab

Update [`PLAN.md`](../PLAN.md) exit gates only when Daily mode stays green for Chromium + messengers and Gaming mode matches V2Box on the UDP/game row.

Full soak sheet (lock/unlock + Windows TUN): [`golden-matrix-2.6.3.4.md`](golden-matrix-2.6.3.4.md).
