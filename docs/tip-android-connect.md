# Tip: Android Connect troubleshooting

If **Connect** closes the app or fails on Samsung / Android 12+ devices:

1. Install the latest **v2rayF-android-arm64.apk** from [Releases](https://github.com/drmikecrypto/v2rayF/releases).
2. **Uninstall** older versions first (clears bad VPN/core state).
3. Use a **Compat** VLESS link — avoid `flow=xtls-rprx-vision` on phones.
4. Tap **Connect** and allow the **VPN** permission when prompted.
5. If connect fails, read the status message — the app tears down VPN so normal internet keeps working.
6. After upgrading, **uninstall** then install the new APK so VPN/core native libs refresh cleanly.

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
