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

After SOCKS `10808` binds: **warmup GET + one timed GET** (8s normal, 12s Vision/REALITY). Connected UI shows list **TCP** ms and, when available, **path** HTTPS ms (`102 · path 480`). Green TCP alone is not Mbps.

VMess-WS-TLS / VLESS-TCP / Trojan / SS can show a low TCP number and still feel slower than Vision+REALITY on the same VPS (splice vs WS/TLS framing). Leave **Packet fragment** and **Adaptive Survive** off unless DPI blocks Connect — Survive is no longer forced silently when Smart Connect finds no path.

## Adaptive Survive

**Off by default.** When enabled, failed connects may retry with TLS hello fragment (works for some DPI; **slow** for non-Vision). Prefer leaving it off unless you need it.

## While connected

Traffic rates poll every **5s** (single-flight). Health checks need **three** SOCKS misses before a drop. Kill switch requires **TUN**; with system proxy only, apps are not blackholed.

**Secure DNS (DoH)** is off by default for lower first-hit latency. When enabled, Xray resolves via Cloudflare/Google DoH on the **direct** path (not hairpinned through the proxy)—safer DNS, but new domains can feel slower to open. Leave it off if you want the snappiest browsing; turn it on when you care about encrypted DNS.

## Protocol throughput notes

- **Hysteria2** — `up` / `down` (Mbps) from share links and Clash are applied as `up_mbps` / `down_mbps` in sing-box. Missing values use sing-box defaults (do not invent fake Mbps).
- **TUIC** — congestion defaults to `bbr`; `udp_relay_mode` is honored when present.
- **WireGuard** — link/Clash `mtu` is honored; otherwise 1400.
- **VLESS Vision / REALITY / classic Xray** — no mux; fragment only if you enable Packet fragment / Adaptive Survive (slow).
