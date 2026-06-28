# Note: android-permissions

## Connect fails with "Permission denied" on Android 10+

**Symptom:** Ping works, but Connect shows:

```text
ErrorStartingProcess, .../files/cores/xray, ... Permission denied
```

**Cause:** From Android 10 (API 29) onward, SELinux blocks `exec()` on files under the app
writable data directory (`files/`, `cache/`). Extracting the Xray binary to `files/cores/xray`
and calling `chmod +x` is not enough — the kernel still denies execution.

**Fix (v1.1.2+):** Ship the core as a native library:

- `NativeLibs/arm64-v8a/libxray.so` (build action: `AndroidNativeLibrary`)
- `android:extractNativeLibs="true"` in the manifest
- Runtime path: `ApplicationInfo.NativeLibraryDir/libxray.so`

Geo files (`geoip.dat`, `geosite.dat`) stay in assets and are copied to `files/cores/` for
read-only use by Xray.

See [Getting started](../GETTING_STARTED.md) and `scripts/package-android.ps1`.
