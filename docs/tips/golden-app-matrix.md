# Golden app matrix + Connect green

See also [`PLAN.md`](PLAN.md).

## Connect green (must all pass)

1. Core process alive  
2. Local SOCKS probe OK (`127.0.0.1:10808`)  
3. Android TUN: HTTP proxy probe OK when sing-box TUN is up (`10809`)

TUN gen204/FCM is **advisory** after Connected (soft rebind if weak). Do not refuse Connect solely on TUN probe failure — that caused false timeouts on cold REALITY/Vision (fixed in 2.6.2.15).

If SOCKS/HTTP fail → tear down and show a component-specific error.

## Golden apps (pass/fail)

Same subscription, clean install, **Private DNS Off**, battery unrestricted preferred.

| App | Pass means |
|-----|------------|
| Chrome | HTTPS loads |
| Instagram | Feed + Direct messages |
| WhatsApp | Send/receive |
| Telegram | Chat + media |
| YouTube | Playback |
| Maps | Load + search |
| Play Services | Push notification arrives |
| UDP game or voice | Real-time UDP works |

## Scorecard

Run the same phone + link on **v2rayF / v2rayNG / V2Box**. Record pass/fail per row. Do not commit credentials or live share links.
