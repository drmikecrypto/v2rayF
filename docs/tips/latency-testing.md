# Latency testing

v2rayF measures latency in two ways:

## Proxy ping (preferred)

When the Xray core is available, **Test** and **Test All** start a short-lived local SOCKS proxy on port `10818`, route traffic through the selected server, and measure HTTP time to a `generate_204` endpoint (Google or Cloudflare).

This matches how v2rayN “Real ping” works: the number reflects the full proxy path, not just a TCP handshake to the server IP.

If you are **already connected** to the server under test, the app reuses the active SOCKS port (`10808`) instead of starting a second core.

## TCP fallback

If the core is missing or the proxy test fails, the app falls back to a raw TCP connect to the server address and port. This can be faster but less accurate for TLS/REALITY/WebSocket nodes.

## Results

| Display | Meaning |
|---------|---------|
| `123 ms` | Successful measurement |
| `timeout` | No response within 10 seconds |
| `—` | Test in progress or not run yet |

After **Test All**, results are saved with your server list.
