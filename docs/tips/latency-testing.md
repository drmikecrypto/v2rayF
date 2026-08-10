# Latency testing

**Test delay** (selected server) and **Test All** measure **proxy-path RTT to `www.google.com`** through a temporary local SOCKS (HTTP via Xray). Results are **not** TCP ping to the VPS port.

| Result | Meaning |
|--------|---------|
| `123 ms` | The node successfully proxied traffic to Google |
| `timeout` | Proxy path failed (even if the TCP port is open) |
| `—` | Not tested yet / in progress |

If a VPS is powered off or the protocol handshake fails, you should see **`timeout`** — never a false “230 ms” from a bare TCP connect.

Probe URLs (first success wins):

1. `https://www.google.com/generate_204`
2. `https://www.google.com/`

## Smart Connect

Smart Connect uses a cheap TCP prefilter only to shortlist candidates, then **proxy-path** probes. Only peers that pass the proxy-path check are connected. TCP-only reachability is shown as `timeout` in the list after ranking.
