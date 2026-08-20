# Tip: Android Connect troubleshooting

If **Connect** closes the app or fails on Samsung / Android 12+ devices:

1. Install the latest **v2rayF-android-arm64.apk** from [Releases](https://github.com/drmikecrypto/v2rayF/releases).
2. **Uninstall** older versions first (clears bad VPN/core state).
3. Tap **Connect** and allow the **VPN** permission when prompted.
4. If connect fails, read the status message — the app tears down VPN so normal internet keeps working.
5. After upgrading, **uninstall** then install the new APK so VPN/core native libs refresh cleanly.

**Vision (xtls-rprx-vision)** works on phones from **v2.0.4** (TUN no longer sniffs TLS into the Vision splice). From **v2.0.6**, Android TUN uses empty sniff for **all** Xray transports so TUN apps work. Set Android **Private DNS** to Off if browsers still fail.

## Good ping but crawl-speed Connected (all configs) — fixed in v2.0.9

**v2.0.7–2.0.8** removed VPN HTTP proxy and raised MTU / forced xudp. Test delay stayed green (SOCKS only) while Chrome over empty-sniff TUN crawled for **every** protocol.

**v2.0.9** restores:

- VPN HTTP proxy `127.0.0.1:10809` for Chromium
- MTU **1280**
- `packetEncoding` only when the share link sets it

Leave **Packet fragment** / **Adaptive Survive** off unless DPI blocks Connect. Uninstall old APK, install **2.0.9+**.

## Idle “Connected” but no internet (phone or Windows)

After sitting unused, NAT may drop the outbound while the UI still shows Connected. **v2.0.6** adds TCP keepalive and a soft path probe (when traffic is flat); with **Auto-reconnect** on, the app reconnects up to twice. Toggle Disconnect → Connect if it still looks stuck.

## Chrome, Brave, Play Store, or Translate offline (Instagram/YouTube work)

The phone always uses **VpnService TUN**. Instagram/YouTube mostly stay on IPv4 TCP. Chrome prefers **IPv6 + HTTP/3**.

v2.0.3+: IPv6 blackhole when Block IPv6 is on, MTU 1280. **v2.0.9** again sets VPN HTTP proxy so Chromium uses CONNECT (not raw QUIC over gVisor).

While testing:

1. Set Android **Private DNS** to **Off**.
2. Uninstall the old APK, then install **v2.0.9+**.
3. Connect, then open Chrome and Play Store.

## WhatsApp offline (other apps work)

WhatsApp uses **TUN DNS**. **v2.0.5** sets VPN DNS to `172.19.0.1` so Xray `UseIPv4` applies. Uninstall, install **v2.0.5+**, reconnect, force-stop WhatsApp once.

## Still broken?

With USB debugging enabled:

```bash
adb logcat -s v2rayF AndroidRuntime
```

## Technical notes (v1.4.3+)

- Xray on Android reads the VPN TUN fd from `xray.tun.fd` / `XRAY_TUN_FD` (inherited as fd **3**).
- The core is started with libc `posix_spawn` so the TUN fd is not closed.
- The core binary is `libxray.so` under the app native lib dir with `LD_LIBRARY_PATH` set at launch.
