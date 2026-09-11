# App Network

Per-app control for traffic while v2rayF is connected.

## Modes

| Mode | Android | Desktop (TUN) |
|------|---------|----------------|
| **VPN** | Through the VPN tunnel (default) | Through TUN + proxy |
| **Direct** | OS clearnet (`AddDisallowedApplication`) | Core `direct` egress (still on TUN) |
| **Block** | Stays on TUN; sing-box `package_name` → `block` | Xray `process` → `blackhole` |

Direct wins over Block for the same app.

## Efficiency

- App list is cached; refresh only when you tap Refresh or reopen after TTL
- Per-app ↑/↓ rates poll only while App Network is open (Android)
- No extra foreground service for App Network

## Games / Unity (Android)

Do **not** put international games on **Direct** from Iran (clearnet cannot reach them). Keep them on **VPN**. **Chromium HTTP proxy assist** stays **on** by default (Play Store / Translate). Turning it **off** may help some Unity titles but recreates the known Play break — reconnect after changing.

## Apply

Tap **Apply** or **Done**. If you are connected, v2rayF reconnects once so VPN exclusions and core rules reload.

## Legacy bypass lists

Package names previously saved under per-app bypass are still stored as `AndroidBypassPackages` and show as **Direct** in App Network.
