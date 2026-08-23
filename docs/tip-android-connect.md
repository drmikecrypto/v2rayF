# Tip: Android Connect troubleshooting

If **Connect** closes the app or fails on Samsung / Android 12+ devices:

1. Install the latest **v2rayF-android-arm64.apk** from [Releases](https://github.com/drmikecrypto/v2rayF/releases).
2. **Uninstall** older versions first (clears bad VPN/core state).
3. Tap **Connect** and allow the **VPN** permission when prompted.
4. If connect fails, read the status message — the app tears down VPN so normal internet keeps working.
5. After upgrading, **uninstall** then install the new APK so VPN/core native libs refresh cleanly.

## v2.2.3 — Test delay on Xray, Connect on sing-box

**Test delay / ping** for classic protocols uses **Xray** (2.2.2 put speedtest on sing-box and every ping timed out). Live **Connect** on Android classic still uses **sing-box** TUN for Instagram Direct.

## v2.2.2 — Instagram Direct + classic sing-box TUN

Instagram **feed/reels** often use VPN HTTP proxy `10809`. **Direct** (MQTT / raw sockets) must go through **TUN**.

**v2.2.2** runs Android classic VLESS/VMess/Trojan/SS/REALITY/Vision on **sing-box** (`stack: system`) for live Connect. VPN HTTP proxy stays for Chromium.

After Connect: set Android **Private DNS** Off, then **force-stop Instagram** once before opening Direct.

## v2.2.1 — timeout emergency (speedtest)

Universal Test delay / Connect timeouts fixed via UDP DNS for speedtest and no ephemeral 10809 clash.

## Idle “Connected” but no internet

**v2.0.6** keepalive + soft path probe; Auto-reconnect up to twice.

## Chrome / WhatsApp / Instagram

IPv6 blackhole + VPN DNS `172.19.0.1` (v2.0.5). HTTP proxy for Chromium/feed; raw-socket chat needs healthy TUN (2.2.2 sing-box on Android classic).

## Still broken?

```bash
adb logcat -s v2rayF AndroidRuntime
```

## Product polish (Phase C — deferred)

UI redesign and SpeedyFi-style multi-WAN aggregation are tracked **after** single-link Android speed matches V2Box on the same configs. See [docs/roadmap-engine-first.md](roadmap-engine-first.md).
