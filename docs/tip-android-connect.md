# Tip: Android Connect troubleshooting

If **Connect** closes the app or fails on Samsung / Android 12+ devices:

1. Install the latest **v2rayF-android-arm64.apk** from [Releases](https://github.com/drmikecrypto/v2rayF/releases).
2. **Uninstall** older versions first (clears bad VPN/core state).
3. Tap **Connect** and allow the **VPN** permission when prompted.
4. If connect fails, read the status message — the app tears down VPN so normal internet keeps working.
5. After upgrading, **uninstall** then install the new APK so VPN/core native libs refresh cleanly.

## v2.2.0 — Android sing-box engine (V2Box-class path)

On Android, **VLESS / VMess / Trojan / Shadowsocks / REALITY / Vision** (and Hy2/TUIC/WG) run on **sing-box** with TUN `file_descriptor` (same VpnService fd inheritance as Xray). Desktop still uses Xray for classic protocols.

**Secure DNS (DoH) defaults ON** so Connect no longer requires flipping Secure DNS by hand. Connect gate budgets are longer (12s / 16s Vision); if DoH was off and the gate fails, one automatic DoH retry runs (never silent fragment).

VPN HTTP proxy `10809` stays on for Chromium until head-to-head QA proves sing-box TUN alone is enough (avoids the 2.0.7 regression).

Set Android **Private DNS** to Off. Leave Packet fragment / Adaptive Survive off unless DPI blocks Connect.

## Idle “Connected” but no internet

**v2.0.6** keepalive + soft path probe; Auto-reconnect up to twice.

## Chrome / WhatsApp

IPv6 blackhole + VPN DNS `172.19.0.1` (v2.0.5). HTTP proxy for Chromium kept in 2.2.0.

## Still broken?

```bash
adb logcat -s v2rayF AndroidRuntime
```

## Product polish (Phase C — deferred)

UI redesign and SpeedyFi-style multi-WAN aggregation are tracked **after** single-link Android speed matches V2Box on the same configs. See [docs/roadmap-engine-first.md](roadmap-engine-first.md).
