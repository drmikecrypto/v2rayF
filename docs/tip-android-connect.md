# Tip: Android Connect troubleshooting

If **Connect** closes the app or fails on Samsung / Android 12+ devices:

1. Install the latest **v2rayF-android-arm64.apk** from [Releases](https://github.com/drmikecrypto/v2rayF/releases).
2. **Uninstall** older versions first (clears bad VPN/core state).
3. Tap **Connect** and allow the **VPN** permission when prompted.
4. If connect fails, read the status message — the app tears down VPN so normal internet keeps working.
5. After upgrading, **uninstall** then install the new APK so VPN/core native libs refresh cleanly.

**Vision (xtls-rprx-vision)** works on phones from **v2.0.4** (TUN no longer sniffs TLS into the Vision splice). From **v2.0.6**, Android TUN uses empty sniff for **all** Xray transports (VLESS-TCP, SS, Trojan-WS, VLESS-WS, HTTPUpgrade, Vision) so delay can look green while apps actually work. Set Android **Private DNS** to Off if browsers still fail.

## Idle “Connected” but no internet (phone or Windows)

After sitting unused, NAT may drop the outbound while the UI still shows Connected. **v2.0.6** adds TCP keepalive and a soft path probe (when traffic is flat); with **Auto-reconnect** on, the app reconnects up to twice. Toggle Disconnect → Connect if it still looks stuck.

## Chrome, Brave, Play Store, or Translate offline (Instagram/YouTube work)

The phone always uses **VpnService TUN**. Instagram/YouTube mostly stay on IPv4 TCP. Chrome, Brave, Play Store, and Translate prefer **IPv6 + HTTP/3**, so they died when the VPN captured `::/0` without Xray blackholing it.

v2.0.3: Xray blackholes IPv6 when **Block IPv6** is on, VPN HTTP proxy `127.0.0.1:10809` (Android 10+), MTU 1280, and the VPN is re-validated after Xray starts.

While testing:

1. Set Android **Private DNS** to **Off** (Settings → Network → Private DNS). Chrome’s own DoH plus VPN DNS fights the tunnel.
2. Uninstall the old APK, then install **v2.0.4+**.
3. Connect, then open Chrome and Play Store.

## WhatsApp offline (other apps work)

WhatsApp uses **TUN DNS**, not the VPN HTTP proxy Chrome uses. v2.0.3 pointed VPN DNS at `1.1.1.1`, which returned AAAA; Block IPv6 then blackholed those packets.

**v2.0.5** sets VPN DNS to `172.19.0.1` so Xray `UseIPv4` applies. Uninstall the old APK, install **v2.0.5+**, reconnect, then force-stop WhatsApp once.

## Still broken?

With USB debugging enabled:

```bash
adb logcat -s v2rayF AndroidRuntime
```

Tap Connect and look for:

- `RemoteServiceException` / `startForeground` — fixed in 1.4.3+ (foreground must start before disconnect)
- `read Android Tun Fd` / `bad file` / `SetNonblock` — TUN fd was not inherited by Xray (1.4.3+ uses posix_spawn + dup2)
- Missing `libxray.so` — reinstall the ARM64 APK

## Technical notes (v1.4.3+)

- Xray on Android reads the VPN TUN fd from `xray.tun.fd` / `XRAY_TUN_FD` (inherited as fd **3**).
- The core is started with libc `posix_spawn` so the TUN fd is not closed (Java `ProcessBuilder` closes non-stdio fds).
- The core binary is `libxray.so` under the app native lib dir with `LD_LIBRARY_PATH` set at launch.
