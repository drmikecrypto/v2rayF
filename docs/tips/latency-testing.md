# Latency testing

**Test delay** (selected server) and **Test All** measure **proxy-path RTT** through a temporary local SOCKS (HTTP via Xray). Results are **not** TCP ping to the VPS port.

| Result | Meaning |
|--------|---------|
| `123 ms` | The node successfully proxied an HTTPS probe |
| `timeout` | Proxy path failed (even if the TCP port is open) |
| `—` | Not tested yet / in progress |

If a VPS is powered off or the protocol handshake fails, you should see **`timeout`** — never a false “230 ms” from a bare TCP connect.

Probe URLs (first success wins):

1. `https://cp.cloudflare.com/generate_204`
2. `https://www.gstatic.com/generate_204`
3. `https://www.google.com/generate_204`
4. `https://www.google.com/`

Speedtest configs include DNS servers `1.1.1.1` / `8.8.8.8` (routed direct) so domain-based nodes resolve even when system DNS is poisoned or offline.

## Smart Connect

Smart Connect uses a cheap TCP prefilter only to shortlist candidates, then **proxy-path** probes. Only peers that pass the proxy-path check are connected. TCP-only reachability is shown as `timeout` in the list after ranking.
