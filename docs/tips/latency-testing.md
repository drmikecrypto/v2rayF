# Latency testing

**Test delay** (selected server) and **Test All** show **TCP round-trip** to the node’s `address:port` (same ballpark as v2rayN tcping), **only after** an HTTPS probe through Xray succeeds.

| Result | Meaning |
|--------|---------|
| `102 ms` | Tunnel works; number is TCP RTT to the VPS (not the cold HTTPS handshake) |
| `timeout` | Proxy path failed (even if the TCP port is open) |
| `—` | Not tested yet / in progress |

A VPS that answers TCP but cannot proxy still shows **`timeout`** — never a false ping from a bare connect.

Each proxy-path check boots Xray on an **ephemeral localhost SOCKS port**. After SOCKS is up it:

1. Discards one warmup GET (TLS/Reality handshake)
2. Times two sequential GETs and keeps the **min** (v2rayN real-ping style)
3. Tries Cloudflare `generate_204` first, then gstatic / Google — **never four URLs at once**

Probe URLs (first success after warmup wins):

1. `https://cp.cloudflare.com/generate_204`
2. `https://www.gstatic.com/generate_204`
3. `https://www.google.com/generate_204`

Test All runs TCP for every row in parallel, then verifies the proxy path one core at a time.

## Smart Connect

Smart Connect uses TCP only to shortlist candidates, then ranks by **warmed** proxy-path RTT. The list still shows TCP ms for rows that passed. Domain nodes that fail system-DNS TCP still enter the shortlist.

If every probe times out, Connect still tries **Adaptive Survive** (fragment / Sentinel) instead of aborting immediately.

## Connect health

After the core binds SOCKS `10808`, Connect runs the same warmup + timed HTTPS probe (~4s budget) before the UI shows **Connected**. A listening port alone is not enough — if the probe fails, the connection is torn down.
