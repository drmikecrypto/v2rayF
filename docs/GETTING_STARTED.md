# Getting started with v2rayF

## 1. Install

Download the latest release for your platform from:

**https://github.com/drmikecrypto/v2rayF/releases/latest**

Extract the zip. Do not move `cores/` away from the executable.

| OS | Command |
|----|---------|
| Windows | Double-click `v2rayF.exe` |
| Linux | `chmod +x run-v2rayF.sh && ./run-v2rayF.sh` |
| macOS | `./run-v2rayF.sh` (see Gatekeeper note below) |
| Android | Install `v2rayF-android-arm64.apk` from the release zip; grant VPN permission when prompted |

### macOS Gatekeeper

If macOS says the app is damaged or from an unidentified developer:

```bash
xattr -cr /path/to/extracted/v2rayF-folder
```

Or: **Right-click** the app → **Open** → confirm once.

---

## 2. Import servers

**Single link** — paste a share URL and click **Import**, or use **Clipboard**:

- `vless://…` (VLESS, REALITY)
- `vmess://…`
- `ss://…` (Shadowsocks)
- `trojan://…`

**Subscription** — paste an `https://…` URL in the top bar and click **Import URL**. The URL is saved automatically. Use **Refresh** later to pull updated servers without pasting again.

See [Subscription URLs](tips/subscription-urls.md) and [Latency testing](tips/latency-testing.md).

---

## 3. Connect

1. Click a server in the list (or double-click to connect immediately).
2. Click **Connect**.
3. The tray icon shows status. Closing the window while connected minimizes to tray.

**Disconnect** stops Xray and restores system proxy settings.

---

## 4. Settings (right panel)

Use **Apply Sentinel profile** for maximum anti-censorship defaults (Global routing, kill switch, DNS through proxy, IPv6 block, TUN when available).

### Smart Connect / Multipath / Secure Share

| Toggle | Behavior |
|--------|----------|
| **Smart Connect** | Probes added servers and connects to the fastest working one (failover on failure) |
| **Smart Multipath** | Balances across top nodes via Xray observatory |
| **Secure Share** | LAN SOCKS/HTTP gateway so other devices use this tunnel — see [Secure Share](tips/secure-share.md) |
| **Kill switch** | Blocks clearnet if the tunnel drops (needs admin on desktop) |
| **DNS through proxy** | Prevents ISP DNS leaks |
| **Block IPv6** | Stops IPv6 bypass around the tunnel |
| **Packet fragment** | Optional DPI evasion (slower) |

### Routing

See [Routing rules](tips/routing-rules.md).

| Mode | Behavior |
|------|----------|
| **Global** | All TCP/UDP through the proxy (Sentinel) |
| **Bypass LAN** | Private IPs go direct (default) |
| **Bypass China** | CN domains/IPs direct (uses geo files in `cores/`) |
| **Custom** | Direct / Proxy / Block lists |

Click **Save settings** before connecting if you changed routing.

### TUN mode

Routes **all device traffic** through the virtual adapter (like a system VPN).

| OS | Requirement |
|----|-------------|
| Windows | Run `v2rayF.exe` **as Administrator** |
| Linux / macOS | `sudo ./run-v2rayF.sh` |
| Android | Tap **Connect** and approve the **VPN permission** prompt (always uses VPN mode) |

When TUN is on, system HTTP proxy is usually not needed.

### System proxy

When enabled (default), v2rayF sets OS proxy to `127.0.0.1:10809` on connect.

Supported: Windows registry, macOS `networksetup`, Linux GNOME / KDE / XFCE.

---

## 5. Latency test

- **Test** — selected server
- **Test All** — every server in the list

Shows TCP connect time in ms (not full proxy handshake).

---

## Data storage

| File | Location |
|------|----------|
| Server list | `%APPDATA%\v2rayF\servers.json` (Windows) |
| App settings | `%APPDATA%\v2rayF\settings.json` |
| Runtime Xray config | `%APPDATA%\v2rayF\runtime\config.json` |

On Linux/macOS, `ApplicationData` maps to `~/.config/` or equivalent.

---

## Troubleshooting

| Problem | Try |
|---------|-----|
| “Xray core missing” | Ensure `cores/xray` or `cores/xray.exe` is next to the app |
| Connect fails | Check server link, firewall, VPS port |
| Bypass China fails | Confirm `geoip.dat` and `geosite.dat` exist in `cores/` |
| TUN fails | Run with Admin / sudo |
| Linux proxy not set | Use TUN, or set manual proxy `127.0.0.1:10809` |
| macOS blocked | `xattr -cr` on the folder |

---

## Build your own packages

```powershell
pwsh -File scripts/package-all.ps1
```

Output: `dist/v2rayF-<platform>.zip`

See [CONTRIBUTING.md](../CONTRIBUTING.md) for development setup.
