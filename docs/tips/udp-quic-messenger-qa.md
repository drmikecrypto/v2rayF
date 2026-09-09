# UDP / QUIC / messenger full-tunnel QA

Manual until automated probes exist. Run with Private DNS Off after Connect is green ([`golden-app-matrix.md`](golden-app-matrix.md)).

## UDP / QUIC

| Check | Pass |
|-------|------|
| YouTube or Chrome HTTP/3 | Video/page loads (QUIC or falls back to TCP without hanging) |
| One realtime game or voice | No permanent mute / disconnect within 2 minutes |
| WhatsApp voice note send | Completes |

## Messengers

| Check | Pass |
|-------|------|
| Instagram Direct | Send + receive (MQTT path) |
| WhatsApp | Chat send/receive |
| Telegram | Chat + media |
| FCM push | Notification arrives with screen off briefly |

## Policy

If a messenger fails, fix TUN/DNS/IPv6 before adding another domain exception. See [`exception-list-policy.md`](exception-list-policy.md).
