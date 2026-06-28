# v2rayF

**Cross-platform proxy client** for [V2Ray](https://www.v2fly.org/) / [Xray](https://github.com/XTLS/Xray-core), Shadowsocks, and Trojan.

Import servers from clipboard or subscription URLs, test latency, pick routing rules, connect with one click — on **Windows**, **macOS**, **Linux** (x64 & ARM64), and **Android** (ARM64).

<p align="center">
  <a href="https://github.com/drmikecrypto/v2rayF/releases/latest"><img src="https://img.shields.io/github/v/tag/drmikecrypto/v2rayF?style=flat-square&label=release" alt="Latest release"></a>
  <a href="https://github.com/drmikecrypto/v2rayF/actions/workflows/ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/drmikecrypto/v2rayF/ci.yml?branch=main&style=flat-square" alt="CI"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue?style=flat-square" alt="License MIT"></a>
  <a href="https://github.com/drmikecrypto/v2rayF/stargazers"><img src="https://img.shields.io/github/stars/drmikecrypto/v2rayF?style=flat-square" alt="GitHub stars"></a>
</p>

---

## Download

**[→ Latest release](https://github.com/drmikecrypto/v2rayF/releases/latest)** — pick the zip for your platform:

| Platform | File | Run |
|----------|------|-----|
| Windows x64 | `v2rayF-win-x64.zip` | `v2rayF.exe` |
| Windows ARM64 | `v2rayF-win-arm64.zip` | `v2rayF.exe` |
| Linux x64 | `v2rayF-linux-x64.zip` | `./run-v2rayF.sh` |
| Linux ARM64 | `v2rayF-linux-arm64.zip` | `./run-v2rayF.sh` |
| macOS Intel | `v2rayF-osx-x64.zip` | `./run-v2rayF.sh` |
| macOS Apple Silicon | `v2rayF-osx-arm64.zip` | `./run-v2rayF.sh` |
| Android ARM64 | `v2rayF-android-arm64.zip` | Install `v2rayF-android-arm64.apk` |

Each desktop package includes **Xray-core** and geo data (`geoip.dat`, `geosite.dat`) — no extra setup. The Android APK bundles the same core and geo files.

> **macOS first launch:** if Gatekeeper blocks the app, run `xattr -cr /path/to/folder` or right-click → Open once.

---

## Features

- **Protocols** — VMess, VLESS (incl. REALITY), Shadowsocks, Trojan, SOCKS
- **Import** — clipboard, paste box, subscription URL (`https://…`)
- **Latency test** — per server or test all
- **Routing** — Global, Bypass LAN, Bypass China, custom direct list
- **TUN / VPN mode** — full-device capture (Admin on Windows; VPN permission on Android)
- **System proxy** — Windows, macOS, GNOME, KDE, XFCE (desktop only)
- **Tray icon** — status at a glance; minimize to tray while connected
- **Local proxies** — SOCKS `127.0.0.1:10808`, HTTP `127.0.0.1:10809`

---

## Quick start

1. Download and extract the zip for your OS from [Releases](https://github.com/drmikecrypto/v2rayF/releases/latest).
2. Import your share link (`vless://…`, `vmess://…`, etc.) or subscription URL.
3. Select a server → **Connect**.
4. Browse — system proxy is set automatically (unless you use TUN-only mode).

See [docs/GETTING_STARTED.md](docs/GETTING_STARTED.md) for routing, TUN, and troubleshooting.

---

## Screenshots

<!-- Add screenshots after first release: docs/images/main-window.png -->
_Screenshots coming soon — PRs welcome!_

---

## Build from source

### Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- PowerShell 7+ (for packaging)

### Run in dev (desktop)

```bash
git clone https://github.com/drmikecrypto/v2rayF.git
cd v2rayF
dotnet run --project src/v2rayF.Desktop/v2rayF.Desktop.csproj
```

Place `xray` / `xray.exe` in `src/v2rayF.Desktop/cores/` for local connects, or run the packager (downloads Xray automatically):

```powershell
pwsh -File scripts/package-all.ps1
```

### Build Android APK

Requires the [.NET Android workload](https://learn.microsoft.com/dotnet/android/overview):

```powershell
dotnet workload install android
pwsh -File scripts/package-android.ps1      # download Xray for Android assets
pwsh -File scripts/package-android-release.ps1
```

Output: `dist/v2rayF-android-arm64.zip` containing the signed-ready APK.

---

## Project structure

```
v2rayF/
├── src/
│   ├── v2rayF.Core/      # Shared models & services (Xray, parsing, stores)
│   ├── v2rayF/           # Shared Avalonia UI (ViewModels, Views)
│   ├── v2rayF.Desktop/   # Desktop entry + bundled cores/
│   └── v2rayF.Android/   # Android entry + VPN services
├── scripts/              # Packaging & launch scripts
├── .github/workflows/    # CI + release automation
└── docs/
```

---

## Contributing

Contributions are welcome! See [CONTRIBUTING.md](CONTRIBUTING.md).

- [Report a bug](https://github.com/drmikecrypto/v2rayF/issues/new?template=bug_report.md)
- [Request a feature](https://github.com/drmikecrypto/v2rayF/issues/new?template=feature_request.md)
- [Security issues](SECURITY.md)

---

## Legal & credits

- Licensed under [MIT](LICENSE).
- Uses [Xray-core](https://github.com/XTLS/Xray-core) (bundled in releases, not committed to this repo).
- UI built with [Avalonia UI](https://avaloniaui.net/).

**Use only on networks and servers you are authorized to access.** Circumventing restrictions may be illegal in your jurisdiction.

---

## Repository topics (for discoverability)

When starring or forking, these topics help others find the project:

`v2ray` `xray` `proxy` `vpn-client` `shadowsocks` `trojan` `vless` `vmess` `cross-platform` `avalonia` `android` `desktop-app` `tun`
