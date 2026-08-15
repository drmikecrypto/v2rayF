# Supported configs (v2rayF)

Honest matrix for what this build can **import, test, and connect**.

## Xray runtime (full path)

| Protocol | Transports | Security |
|----------|------------|----------|
| VLESS | tcp, ws, grpc, h2, httpupgrade, xhttp, kcp, quic | none, tls, reality (+ Vision) |
| VMess | same | none, tls, reality |
| Trojan | same | tls, reality |
| Shadowsocks | plain (+ stream when link has type=) | — |
| SOCKS5 | — | — |

Also: WS early data (`ed`), `packetEncoding` (xudp/packet), REALITY pbk/sid/spx, xHTTP `extra`.

## Import-only maps (still run on Xray)

- **Clash Meta** `proxies:` — vmess / vless / trojan / ss / socks5
- **sing-box JSON** outbounds — same Xray-capable types

## Skipped (need sing-box — not in 1.5.x)

Hysteria2 (`hy2`), TUIC, WireGuard, anytls, Shadowsocks SIP003 **plugins**.

Skipped items show a clear StatusText reason; they are **not** imported as fake plain nodes.

## Roadmap

- **1.6.x** — live session feel (stats, reconnect, TUN/proxy)
- **2.0** — dual-core sing-box so hy2/TUIC/WG/anytls actually connect
