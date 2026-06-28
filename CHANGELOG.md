# Changelog

All notable changes to v2rayF are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.1.3] - 2026-06-28

### Fixed

- Android ANR after connect/disconnect — Xray shutdown no longer blocks the UI thread
- Android VPN service now calls `StartForeground` when started (required on Android 8+)
- VPN teardown stops the service and closes the TUN interface on disconnect

### Changed

- Faster connect — core readiness detected via SOCKS port probe instead of a fixed delay
- Android startup pre-extracts geo assets in the background
- Server list and settings load in parallel on app launch

## [1.1.2] - 2026-06-27

### Added

- Real proxy latency test (HTTP via local SOCKS, like v2rayN Real ping)
- Subscription URL saved in settings with **Refresh** to re-fetch servers
- Android clipboard support for **Paste** import
- FAQ index (`docs/faq/README.md`) replacing placeholder stub pages

### Fixed

- Android connect failure on Android 10+ — Xray shipped as `libxray.so` in native libs (SELinux)
- Import text box clears automatically after a successful **Add** / **Paste**

### Changed

- `scripts/package-android.ps1` installs Xray to `NativeLibs/arm64-v8a/libxray.so`
- Latency docs and subscription docs updated

## [1.1.1] - 2026-06-27

### Fixed

- Connect crash on Windows ("different thread owns this object") — Xray process events now marshal to the UI thread
- Clear error when local ports 10808/10809 are in use (e.g. v2rayN already running)
- Windows release packages ship as `v2rayF.exe` instead of `v2rayF.Desktop.exe`

## [1.1.0] - 2026-06-26

### Added

- **Android app** (ARM64 APK) with VPN-based full-device proxy via Xray TUN
- Shared `v2rayF.Core` library and platform abstractions (`ICoreEnvironment`, `IPlatformIntegration`)
- Split desktop head (`v2rayF.Desktop`) from shared Avalonia UI (`v2rayF`)
- Mobile-optimized `MainView` for Android
- `scripts/package-android.ps1` and `scripts/package-android-release.ps1`
- Android build job in release workflow (`v2rayF-android-arm64.zip`)

### Changed

- Desktop entry point moved to `src/v2rayF.Desktop/`
- Xray cores path is now `src/v2rayF.Desktop/cores/`

## [1.0.0] - 2026-06-26

### Added

- Cross-platform desktop app for Windows, macOS, and Linux (x64 and ARM64)
- Protocol support: VMess, VLESS (incl. REALITY), Shadowsocks, Trojan, SOCKS
- Import from clipboard, text paste, and subscription URLs
- Server list with connect/disconnect and double-click to connect
- Latency test per server and batch test for all servers
- Routing modes: Global, Bypass LAN, Bypass China, Custom direct rules
- TUN mode for full-device traffic capture
- System tray icon with connection status
- Automatic system proxy on Windows, macOS, GNOME, KDE, and XFCE
- Local SOCKS (`127.0.0.1:10808`) and HTTP (`127.0.0.1:10809`) inbounds
- Bundled [Xray-core](https://github.com/XTLS/Xray-core) with geo data in release packages
- GitHub Actions workflow for automated multi-platform releases

[1.1.3]: https://github.com/drmikecrypto/v2rayF/releases/tag/v1.1.3
[1.1.2]: https://github.com/drmikecrypto/v2rayF/releases/tag/v1.1.2
[1.1.1]: https://github.com/drmikecrypto/v2rayF/releases/tag/v1.1.1
[1.1.0]: https://github.com/drmikecrypto/v2rayF/releases/tag/v1.1.0
[1.0.0]: https://github.com/drmikecrypto/v2rayF/releases/tag/v1.0.0
