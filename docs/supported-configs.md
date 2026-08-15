# Supported configs (v2rayF 2.0)

Honest matrix for what this build can **import, test, and connect**.

## Xray core

| Protocol | Transports | Security |
|----------|------------|----------|
| VLESS | tcp, ws, grpc, h2, httpupgrade, xhttp, kcp, quic | none, tls, reality (+ Vision) |
| VMess | same | none, tls, reality |
| Trojan | same | tls, reality |
| Shadowsocks | plain (+ stream when link has type=) | — |
| SOCKS5 | — | — |

Also: WS early data (`ed`), `packetEncoding`, REALITY, xHTTP `extra`.

## sing-box core (bundled on desktop)

| Protocol | Notes |
|----------|--------|
| Hysteria2 (`hy2://`) | password + SNI / obfs |
| TUIC | uuid:password + congestion |
| WireGuard | private key @ host + peer public key |
| anytls | password + SNI |

Connect/Test route these to `cores/sing-box` automatically. If the binary is missing, Connect shows a clear error.

## Import sources

- Share links / subscriptions
- Clash Meta `proxies:` YAML
- sing-box JSON outbounds
- Xray JSON outbounds

## Still skipped

Shadowsocks SIP003 **plugins** (plain SS only) — with StatusText reason.
