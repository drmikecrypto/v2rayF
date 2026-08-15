# Latency testing

**Test delay** (selected server) and **Test All** show **TCP round-trip** to the node’s `address:port` (same ballpark as v2rayN tcping), **only after** an HTTPS probe through Xray succeeds.

| Result | Meaning |
|--------|---------|
| `102 ms` | Tunnel works; number is TCP RTT to the VPS |
| `timeout` | Proxy path failed (even if the TCP port is open) |
| `—` | Not tested yet / in progress |

A VPS that answers TCP but cannot proxy still shows **`timeout`**.

Each proxy-path check boots Xray on an **ephemeral localhost SOCKS port**, then issues **one** Cloudflare `generate_204` (gstatic / Google as fallback). Test All runs several Xray workers in parallel (3 desktop / 2 Android). IP nodes that fail TCP are not probed.

After Test All (and Smart Connect ranking), the list is **sorted fastest first**.

## Smart Connect

TCP shortlist, then one-GET proxy-path. Vision peers are never mixed into a multipath balancer.

## Connect health

After SOCKS `10808` binds: **warmup GET + one timed GET** (8s normal, 12s Vision/REALITY). Connected ping stays the list **TCP** ms — never the HTTPS probe. No repeating HTTPS ping while connected.

## Adaptive Survive

**Off by default.** When enabled, failed connects may retry with TLS hello fragment (works for some DPI; **slow**). Prefer leaving it off unless you need it.
