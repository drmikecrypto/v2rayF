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

After SOCKS `10808` binds: **one** HTTPS GET. Vision/REALITY gets 8s; other transports 4s. No repeating HTTPS ping while connected.
