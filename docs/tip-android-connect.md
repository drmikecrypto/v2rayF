# Tip: Android Connect troubleshooting

If **Connect** closes the app or fails on Samsung / Android 12 devices:

1. Install the latest **v2rayF-android-arm64.apk** from [Releases](https://github.com/drmikecrypto/v2rayF/releases).
2. **Uninstall** older versions first (clears bad VPN/core state).
3. Use a **Compat** VLESS link — avoid `flow=xtls-rprx-vision` on phones.
4. Tap **Connect** and allow the **VPN** permission when prompted.
5. If connect fails, read the status message — the app tears down VPN so normal internet keeps working.

## Still broken?

With USB debugging enabled:

```bash
adb logcat -s v2rayF
```

Tap Connect and check for `XRAY_TUN_FD`, `libxray.so`, or VPN errors.

## Technical notes (v1.1.8+)

- Xray on Android reads the VPN TUN fd from the `XRAY_TUN_FD` environment variable.
- The core runs from `libxray.so` in native libs with `LD_LIBRARY_PATH` set at launch.
