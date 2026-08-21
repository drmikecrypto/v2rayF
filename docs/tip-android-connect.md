# Tip: Android Connect troubleshooting

If **Connect** closes the app or fails on Samsung / Android 12+ devices:

1. Install the latest **v2rayF-android-arm64.apk** from [Releases](https://github.com/drmikecrypto/v2rayF/releases).
2. **Uninstall** older versions first (clears bad VPN/core state).
3. Tap **Connect** and allow the **VPN** permission when prompted.
4. If connect fails, read the status message — the app tears down VPN so normal internet keeps working.
5. After upgrading, **uninstall** then install the new APK so VPN/core native libs refresh cleanly.

## v2.2.1 — classic Android on Xray again

**v2.2.0** routed Android classic protocols through sing-box and forced DoH into speedtest; that made **every** config show timeout. **v2.2.1** restores:

- Classic VLESS/VMess/Trojan/SS/REALITY/Vision → **Xray** on Android (same as desktop)
- Hy2/TUIC/WG/anytls → sing-box only
- Test delay → UDP DNS; Connect keeps DoH default + auto DoH retry
- VPN HTTP proxy `10809` kept (2.0.9 lesson)

Set Android **Private DNS** to Off. Leave Packet fragment / Adaptive Survive off unless DPI blocks Connect.

## v2.2.0 — Android sing-box engine (rolled back for classic)

Attempted V2Box-class path for classic protocols on sing-box TUN; rolled back in 2.2.1. Hy2 path unchanged.

## Idle “Connected” but no internet

**v2.0.6** keepalive + soft path probe; Auto-reconnect up to twice.

## Chrome / WhatsApp

IPv6 blackhole + VPN DNS `172.19.0.1` (v2.0.5). HTTP proxy for Chromium kept.

## Still broken?

```bash
adb logcat -s v2rayF AndroidRuntime
```

## Product polish (Phase C — deferred)

UI redesign and SpeedyFi-style multi-WAN aggregation are tracked **after** single-link Android speed matches V2Box on the same configs. See [docs/roadmap-engine-first.md](roadmap-engine-first.md).
