# Supported configurations (v2rayF)

Honest matrix for what this build can import and run.

## Runs on Xray (full: import → Test → Connect)

| Protocol | Transports | Security |
|----------|------------|----------|
| VLESS | tcp, ws, grpc, h2, httpupgrade, xhttp, kcp, quic | none, tls, reality (+ Vision flow) |
| VMess | same | none, tls, reality |
| Trojan | same | tls, reality |
| Shadowsocks | plain (+ stream when link has type=) | none / tls when present |
| SOCKS5 | tcp | — |

Also supported on those stacks: WS early data (`ed`), `packetEncoding` (xudp/packet), gRPC `multiMode`, xHTTP `extra`, QUIC key.

## Import-only scrape (still runs on Xray)

- Clash Meta `proxies:` YAML — maps vmess/vless/trojan/ss/socks5; **skips** hy2/tuic/wg/anytls with a status reason
- sing-box JSON `outbounds` — maps Xray-runnable types; **skips** the rest with reasons

## Not runnable in this Xray build (honest skip)

| Scheme / type | Status |
|---------------|--------|
| Hysteria2 (`hy2://`, `hysteria2`) | Skipped — needs sing-box (Phase C / 2.0) |
| TUIC | Skipped — needs sing-box |
| WireGuard (`wg://`) | Skipped — needs sing-box |
| anytls | Skipped — needs sing-box |
| Shadowsocks SIP003 `plugin=` | **Rejected** — not imported as fake plain SS |

## Roadmap

- **1.5.x** — Xray parity + honest imports (this doc)
- **1.6.x** — live session experience
- **2.0** — dual-core sing-box for hy2 / TUIC / WG / anytls
