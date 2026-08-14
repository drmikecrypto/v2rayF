# Latency testing

**Test delay** (selected server) and **Test All** measure **proxy-path RTT** through a temporary local SOCKS (HTTP via Xray). Results are **not** TCP ping to the VPS port.

| Result | Meaning |
|--------|---------|
| `123 ms` | The node successfully proxied an HTTPS probe |
| `timeout` | Proxy path failed (even if the TCP port is open) |
| `—` | Not tested yet / in progress |

If a VPS is powered off or the protocol handshake fails, you should see **`timeout`** — never a false “230 ms” from a bare TCP connect.

Each test boots Xray on an **ephemeral localhost SOCKS port** (not a fixed `10818`). When packet fragment is enabled in Settings (or Adaptive Survive enables it), speedtest configs honor fragment for non-Vision peers.

Probe URLs (first success wins):

1. `https://cp.cloudflare.com/generate_204`
2. `https://www.gstatic.com/generate_204`
3. `https://www.google.com/generate_204`
4. `https://www.google.com/`

Speedtest configs include DNS servers `1.1.1.1` / `8.8.8.8` (routed direct) so domain-based nodes resolve even when system DNS is poisoned or offline.

## Smart Connect

Smart Connect uses a cheap TCP prefilter only to shortlist candidates, then **proxy-path** probes (≈10s budget each). Domain nodes that fail system-DNS TCP still enter the shortlist. Reality peers get reserved shortlist slots.

Only peers that pass the proxy-path check are preferred for Connect. If every probe times out, Connect still tries **Adaptive Survive** (fragment / Sentinel) on TCP-best and Reality candidates instead of aborting immediately. TCP-only reachability is shown as `timeout` in the list after ranking.

## Connect health

After the core binds SOCKS `10808`, Connect runs the same HTTPS-through-proxy probe before the UI shows **Connected**. A listening port alone is not enough — if the probe fails, the connection is torn down.
