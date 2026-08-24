# Routing rules

v2rayF maps UI presets to Xray `routing` rules (and bundled `geoip.dat` / `geosite.dat` where needed).

| Preset | Behavior |
|--------|----------|
| **Global (Sentinel)** | Almost everything via proxy. Loopback stays local. Use with **Sentinel profile** (kill switch + DNS through proxy + IPv6 block). |
| **Bypass LAN** | Private IPv4/IPv6 ranges go direct; the rest via proxy. |
| **Bypass China** | `geosite:cn` / `geoip:cn` + private → direct. Requires geo files. |
| **Custom** | Three lists: **Direct**, **Force proxy**, **Block** (blackhole). Domains or CIDRs, one per line. |

DNS (port 53 and the Xray DNS module) is routed through the proxy when **DNS through proxy** is enabled — it is never forced to clearnet in TUN mode.

## App Network (per-app)

Use **Settings → Open App Network** to set each app to **VPN**, **Direct**, or **Block**. See [app-network.md](app-network.md).

On Android, **Direct** excludes the package from the VPN with `VpnService.Builder.AddDisallowedApplication` (same mechanism as the old package-name bypass list). **Block** keeps the app on the tunnel and drops its traffic in sing-box.
