# Changelog

All notable changes to v2rayF are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [2.2.8] - 2026-08-23

### Fixed

- Android Connect **sing-box legacy DNS** — live config uses sing-box **1.12** DNS server format (`type` + `server`), bootstrap **dns rules**, and outbound **`domain_resolver`** for hostname nodes
- Connect timeout errors prefer **FATAL/ERROR** stderr lines instead of truncated migration URL fragments (`migrate-to-new-dns-server-formats`)

## [2.2.7] - 2026-08-23

### Fixed

- **Release CI** — PowerShell parse error in `package-android.ps1` (`$InboundPath:` → `${InboundPath}:`) blocked v2.2.6 Android build
- CI Android asset step uses `-AllowUnpatchedSingBox` when NDK/Go are unavailable (release still builds patched `libsingbox.so`)

## [2.2.6] - 2026-08-23

### Fixed

- Android Connect **sing-box config rejected** — removed invalid JSON field `file_descriptor` (not supported in sing-box 1.12)
- TUN inbound uses **`address`** (1.12 schema); VPN fd is passed via **`SING_BOX_TUN_FD`** env to a **patched libsingbox.so** built in release CI

## [2.2.5] - 2026-08-23

### Fixed

- Android Connect **“Xray core did not become ready in time”** — ready gate now uses the full **15s** Connect budget (was ~4s) with extra time for **sing-box + VPN TUN**
- Connect timeout errors show the correct core label (**sing-box** vs Xray) and trimmed stderr when the process stays alive
- sing-box live config sets **`auto_detect_interface: false`** when Android VPN TUN is active (avoids startup stalls)

## [2.2.4] - 2026-08-23

### Fixed

- Android Connect **“sing-box core not found”** — APK now ships **libsingbox.so** (in-app Update refreshes native libs; uninstall not required)
- `GetSingBoxPath` uses `nativeLibraryDir` like Xray (files/cores/sing-box is not executable on Android 10+)

## [2.2.3] - 2026-08-23

### Fixed

- Universal **Test delay / ping timeouts** after 2.2.2 — speedtest no longer uses Android classic-on-sing-box; classic ping stays on **Xray**
- Live Connect still uses **sing-box** TUN on Android classic (Instagram Direct path from 2.2.2)

## [2.2.2] - 2026-08-23

### Fixed

- Android **Instagram Direct** dark while feed/reels worked — Direct (MQTT/raw sockets) needs TUN; classic protocols again use **sing-box** `stack: system` on Android (V2Box-class path)
- Keeps v2.2.1 safeguards: speedtest **UDP DNS**, no ephemeral **10809** bind; VPN HTTP proxy `10809` still on for Chromium/feed

### Changed

- Android classic VLESS/VMess/Trojan/SS/REALITY/Vision → **sing-box** TUN again; desktop classic stays **Xray**

## [2.2.1] - 2026-08-22

### Fixed

- Emergency: **all configs timing out** after 2.2.0 — Test delay / Connect broken on every machine
- Android classic protocols (VLESS/VMess/Trojan/SS) back on **Xray** (sing-box classic path rolled back until re-proven)
- Test delay / speedtest uses **UDP DNS** again (DoH stays default for live Connect + auto-retry)
- sing-box speedtest no longer binds **10809** on ephemeral ports (parallel Test All clash)

## [2.2.0] - 2026-08-22

### Added

- Android: VLESS / VMess / Trojan / Shadowsocks / REALITY / Vision run on **sing-box** with TUN `file_descriptor` (V2Box-class path); desktop keeps Xray for classic protocols
- Connect gate: automatic one-shot Secure DNS retry when DoH was off and HTTPS probe failed (no silent fragment)

### Changed

- **Secure DNS (DoH) defaults ON** — Connect works without babysitting DNS on poisoned UDP :53 networks
- Test delay / speedtest DNS uses the same defaults as live Connect
- Connect health budgets **12s** / **16s** Vision (was 8s / 12s)
- sing-box mixed listeners on both **10808** and **10809** for Chromium SetHttpProxy parity
- Android VPN HTTP proxy **kept** until sing-box TUN is proven for Chrome (see tip-android-connect)

## [2.0.9] - 2026-08-20

### Fixed

- Emergency: Connected crawl on **every** protocol after 2.0.7/2.0.8 (Test delay stayed green). Restored Android VPN HTTP proxy `10809`, MTU **1280**, and stopped forcing `packetEncoding=xudp`
- Desktop TUN sniff again `http,tls` (Android TUN stays empty). Windows system proxy again includes `socks=`

## [2.0.8] - 2026-08-20

### Fixed

- Desktop TUN still sniffed `http,tls,quic` while Android was empty — same WS/Trojan/SS speed tax on Windows TUN. Empty `destOverride` on all platforms now
- Smart Connect no longer silently forces Adaptive Survive / packet fragment when every proxy-path fails (that made “Connected” crawl while TCP ping stayed green)

### Changed

- Android VPN MTU **1400** (was 1280) for higher throughput
- Windows system proxy: HTTP+HTTPS only (drop WinINET `socks=` edge cases; match macOS/Linux)
- Local SOCKS/HTTP sniff no longer rewrites QUIC
- Connected ping shows TCP plus path HTTPS when known (`102 · path 480`)

## [2.0.7] - 2026-08-20

### Fixed

- Android: VMess-WS / VLESS-TCP / Trojan / SS felt crawl-speed while REALITY+Vision felt fine — VPN HTTP proxy `10809` forced Chromium through CONNECT on top of WS/TLS. Chromium now uses TUN (empty sniff from 2.0.6)

### Changed

- VLESS/VMess default `packetEncoding=xudp` when the share link omits it (explicit values preserved)
- Docs: green delay ≠ throughput; leave Packet fragment / Adaptive Survive off for non-Vision speed

## [2.0.6] - 2026-08-20

### Fixed

- Idle “Connected but no internet” on phone and Windows — NAT can drop the outbound while SOCKS still listens. TCP keepalive (`idle 45` / `interval 15`) on all long-lived outbounds; soft SOCKS path probe every 60s when traffic is flat (2 fails → reconnect)
- Android TUN: empty `destOverride` for all Xray transports (VLESS-TCP, SS, Trojan-WS, VLESS-WS, HTTPUpgrade, Vision) — TLS/HTTP sniff fought those tunnels the same way it fought Vision
- Auto-reconnect retries up to **2** times with backoff (was one shot)

### Changed

- Fragment dialer merges with keepalive sockopt instead of replacing it
- Plain Shadowsocks always gets `streamSettings` so keepalive can attach
- Vision keepalive only (no `tcpNoDelay`) so splice stays safe

## [2.0.5] - 2026-08-18

### Fixed

- Android **WhatsApp** (and other raw-socket apps) had no internet while Chrome/Instagram worked — VPN DNS was `1.1.1.1` so AAAA hit the TUN IPv6 blackhole. VPN DNS is `172.19.0.1`; port 53/853 hits `dns-out` before public resolvers

## [2.0.4] - 2026-08-17

### Fixed

- Android **Vision**: Connected but apps had no internet — gVisor TUN TLS sniff fought Vision splice. TUN destOverride is empty for Vision; other configs keep http/tls sniff
- IPv6 blackhole is TUN-only so SOCKS/HTTP (health probe, VPN HTTP proxy) are not blocked

## [2.0.3] - 2026-08-17

### Fixed

- Android: Chrome, Brave, Play Store, and Translate failed while Instagram/YouTube worked — IPv6 captured by VpnService was not blackholed in Xray; Chromium had no VPN HTTP proxy; MTU 1500 fragmented on LTE
- Android TUN sniffing no longer rewrites QUIC; VPN re-validated after Xray is up

### Changed

- Android VPN MTU 1280, DNS 1.1.1.1/8.8.8.8, HTTP proxy `127.0.0.1:10809` (API 29+)

## [2.0.2] - 2026-08-16

### Added

- Hysteria2 `up`/`down` (Mbps) from share links and Clash/sing-box JSON → live `up_mbps`/`down_mbps`
- TUIC `udp_relay_mode` and WireGuard `mtu` when present on the config
- Secure DNS settings note: DoH can add first-page latency

### Changed

- sing-box **Block IPv6** also rejects IPv6 (matches Xray leak intent)

## [2.0.1] - 2026-08-15

### Changed

- **Smart Connect** Connect uses the true fastest working path (no Selected / LastGood boost)
- Clicking another server while **Connected** switches to it immediately (explicit pick skips re-rank)
- Status shows `Connecting to fastest…` then the chosen name

## [2.0.0] - 2026-08-15

### Added

- **Dual-core**: Hysteria2, TUIC, WireGuard, and anytls import → test → connect via bundled **sing-box** (desktop packages)
- `SingBoxConfigBuilder` + `CoreRuntime` route by protocol; Xray remains default for VLESS/VMess/Trojan/SS/SOCKS
- Clash Meta / sing-box JSON now **import** hy2/tuic/wg/anytls (run when sing-box binary is present)
- Packager downloads sing-box into `cores/` alongside Xray ([docs/supported-configs.md](docs/supported-configs.md))

### Changed

- Share links `hy2://`, `tuic://`, `anytls://`, `wireguard://` parse into first-class protocols instead of skip-only

## [1.6.0] - 2026-08-15

### Added

- **Auto-reconnect once** after unexpected core drop (user settings only — never forces Survive/fragment). Toggle in Settings.

### Changed

- Live-experience follow-up to 1.4.15/1.5.0: sticky reconnect without poisoning the session with DPI fragment

## [1.5.0] - 2026-08-15

### Added

- Clash Meta `proxies:` YAML import (vmess/vless/trojan/ss/socks) with skips for hy2/tuic/wg/anytls
- sing-box JSON outbound import for Xray-runnable types; unsupported types reported honestly
- WS early data (`ed` / `maxEarlyData`) and VLESS/VMess `packetEncoding` parse + Xray build
- Import summary StatusText: N imported + skip reasons ([docs/supported-configs.md](docs/supported-configs.md))

### Fixed

- Shadowsocks SIP003 `plugin=` links are **skipped** (plain SS only) instead of importing a broken node
- hy2 / TUIC / anytls / WireGuard schemes skipped with clear sing-box hints (not silent drop)

## [1.4.15] - 2026-08-15

### Fixed

- Live traffic stats no longer spawn stacked `xray api` processes every 2.5s (5s poll, single-flight, 1.5s timeout) — reduces mid-session “thinking” stutter
- HealthLoop requires three consecutive SOCKS misses (500ms timeout) before declaring a drop — fewer false “connection dropped” flaps
- Unexpected core stop no longer flashes Idle/`Disconnected` before Failed status
- Kill switch arms only with TUN; system-proxy mode no longer blackholes non-proxy apps while Connected

### Changed

- Secure DNS (DoH) and Adaptive Survive default **off** (Settings still opt-in)
- Quiet update offers no longer overwrite the Connected status line

## [1.4.14] - 2026-08-15

### Fixed

- After Connect, the ping label no longer jumps from ~100ms (TCP) to ~1500ms (cold HTTPS) — list TCP ms is kept
- Connect health uses a warmup GET then one timed GET (8s / 12s Vision·REALITY) so cold handshakes less often fail into Adaptive Survive fragment
- Adaptive Survive defaults **off** so a flaky probe cannot leave the session on slow TLS hello fragment

### Changed

- Packet fragment / Survive remain available as opt-in Settings for DPI environments

## [1.4.13] - 2026-08-14

### Fixed

- Vision (xtls-rprx-vision) infers `tls`/`reality` when the share link omitted `security`, and Connect no longer tears down after a 4s cold handshake — one GET, 8s for Vision/REALITY
- Incomplete REALITY links (empty `pbk`) are rejected with a clear error instead of a silent failed handshake
- Connected session no longer runs HTTPS probes every 15s (that caused sudden speed drops on Vision)
- Test All is parallel (3 Xray workers on desktop, 2 on Android) with a single generate_204 verify; IP nodes that fail TCP are skipped
- TUN MTU is 1500; sniffing uses `routeOnly` so destinations are not rewritten
- Vision is never mixed into a Smart Multipath `leastPing` balancer

### Changed

- After Test All / Smart Connect ranking, servers sort fastest-first
- Settings live behind one **Settings** button (desktop flyout, mobile overlay)
- QUIC key/security, xHTTP `extra`, SS SIP002 plugin-safe parse, VMess query-URI, gRPC `multiMode` import
- Hysteria2/TUIC paste is skipped with a sing-box hint (not in this Xray build)

## [1.4.12] - 2026-08-14

### Fixed

- Test / Test All no longer report ~2000ms for nodes that are ~100ms in v2rayN: the list shows **TCP RTT** after the tunnel is verified, not a cold HTTPS handshake through a freshly spawned Xray
- Proxy-path probes no longer race four HTTPS URLs on a cold SOCKS (that inflated delay and slowed Connect); they warmup, then take the min of two sequential Cloudflare `generate_204` GETs
- Connect health probe budget is 4s (was 10s) with a 2s HTTP connect timeout

### Changed

- A row still shows **timeout** when TCP works but the proxy path fails (no false ping)
- Test All runs TCP for every server in parallel, then verifies the proxy path
- Smart Connect ranks by warmed proxy-path RTT; the list still shows TCP ms for working tunnels

## [1.4.11] - 2026-08-14

### Fixed

- **Test All / speedtest** now uses the same per-server outbound and DNS bootstrap as live Connect (`BuildServerRuntime`) — domain WS/gRPC/Trojan/VMess links no longer fail Test while Connect would work
- **Smart Connect** shortlist covers transport families (WS, gRPC, Trojan, VMess, SS, plain TCP) instead of probing only TCP/Reality top-N; selected row is always shortlisted and probed first
- Transport-aware SOCKS bind wait: gRPC/HTTPUpgrade 4s, WS 3s, Reality TCP 2.5s (was fixed 2s for all)

### Changed

- Mobile server list shows **protocol · network · security** (`DisplayTransport`) so deep_fix matrix rows are distinguishable
- Smart Connect raises proxy-path probe cap to **10** when subscription has ≥10 servers
- Regression tests: full 15-link deep_fix matrix (`DeepFixMatrixTests`)

## [1.4.10] - 2026-08-14

### Fixed

- Latency Test / Test All / Smart Connect / post-connect health probes no longer use unsupported `socks5h://` — .NET only allows `socks5`, so every config previously showed `timeout` even when Xray worked (same links OK in v2rayN / v2box)
- Probe failures surface the real exception message instead of a generic timeout when the client rejects the proxy scheme

### Changed

- Android **Update** again downloads the release zip, verifies SHA256, and installs the APK on top of the existing app (PackageInstaller + FileProvider). Requires `REQUEST_INSTALL_PACKAGES` and “Install unknown apps” for this package (GitHub sideload; not Play Store–oriented)
- Desktop Update path unchanged (in-app zip replace + restart)

## [1.4.9] - 2026-08-14

### Fixed

- Live connect DNS no longer chicken-and-eggs domain nodes: outbound hosts resolve via direct `1.1.1.1` / `8.8.8.8`, and `dns-module` always routes direct (same bootstrap as speedtest)
- Connect no longer reports Connected when only local SOCKS is listening — HTTPS proxy-path probe must succeed first
- Windows system proxy now sets HTTP + HTTPS + SOCKS (`10809` / `10808`) with LAN bypass overrides
- Smart Connect no longer aborts before Adaptive Survive when every latency probe times out
- Domain nodes that fail system-DNS TCP prefilter still enter the Smart Connect shortlist; Reality peers get reserved slots

### Changed

- Latency Test / Smart Connect use ephemeral localhost SOCKS ports (no fixed `10818`)
- Speedtest honors packet fragment when enabled (Vision excluded); Smart Connect probe budget raised to 10s
- Local SOCKS/HTTP inbounds enable sniffing for domain-based routing under system proxy

### Tests

- Connect reliability config tests for DNS bootstrap, fragment speedtest, Reality/domain shortlist, and Survive candidate selection

## [1.4.8] - 2026-08-12

### Removed

- QR import (camera scan and QR image pick) on all platforms
- Android `CAMERA` permission, Google Code Scanner / ML Kit barcode module, and related libraries (`ZXing.Net`, SkiaSharp QR path)

### Changed

- Android in-app Update opens the GitHub release page in the browser instead of sideloading an APK
- Android no longer requests `REQUEST_INSTALL_PACKAGES` or uses PackageInstaller / FileProvider for updates

### Security

- Leaner Android permission surface for Play Protect / sideload friendliness (VPN/TUN permissions retained)

## [1.4.7] - 2026-08-11

### Changed

- Android QR uses Google Code Scanner (system QR UI) instead of the stock camera photo + ZXing path; falls back to image pick when Play Services is unavailable
- Mobile paste box is height-capped with Paste/Add/QR on a fixed row so long bulk pastes no longer push Add off-screen
- Latency Test / Test All probe Cloudflare and gstatic `generate_204` before Google; status text says “proxy probe failed” when the tunnel path fails

### Fixed

- Speedtest config now includes `1.1.1.1` / `8.8.8.8` DNS (routed direct) so domain-based nodes are not stuck on poisoned or offline system DNS during delay tests
- Longer speedtest core-ready wait so Reality/domain handshakes can finish before the HTTPS probe

### Tests

- Sentinel multi-link paste regression (15 share links) covering Reality/Vision, WS, gRPC, HTTPUpgrade, Trojan, VMess, and Shadowsocks parse + Build/BuildSpeedtest JSON

## [1.4.6] - 2026-08-10

### Added

- Import config files (`.txt`, `.json`, `.v2box`, `.npv`, and related dumps) plus bulk share-link lists
- Clipboard import of Xray JSON outbounds
- QR import via camera (Android) or QR image (desktop), including bulk subscription payloads
- Live ↑/↓ **speed rates** and connected ping number beside traffic meters (UI + Android notification)
- Persisted server selection across app restarts

### Changed

- **Test delay** / **Test All** report proxy-path delay only — TCP-only reachability shows as `timeout` (no false ~230 ms pings)
- Smart Connect defaults on; only working proxy-path peers are connect candidates
- Shared traffic stats hub (~2.5s) so UI and notification no longer each spawn per-second `xray api` processes
- Closing the Android app finishes VPN and clears the ongoing notification (Home still keeps the tunnel)

### Fixed

- Selecting a mid-list config no longer resets to the first server after import or relaunch

## [1.4.5] - 2026-08-09

### Added

- Live ↑/↓ traffic totals while connected (Xray Stats API), shown on desktop and phone UI
- Android ongoing notification shows clean upload/download session totals

### Changed

- Server list on phone scrolls independently so large subscription lists stay usable
- Desktop server list uses virtualization for large configs
- Latency **Test** / **Test All** measure proxy-path RTT to `www.google.com`
- In-app **Update** button is hidden until a newer GitHub release with SHA256 is available (desktop + Android)

### Fixed

- Update offer requires SHA256 before the Update button appears, so Apply always has a verifiable package

## [1.4.4] - 2026-08-08

### Fixed

- Windows in-app updates — always show **Check for updates** / **Update x.y.z** on desktop; recheck when the window is activated; clearer status when already latest or the check fails
- Version compare treats `1.4.3` and `1.4.3.0` as equal; desktop reads executable product/file version
- Desktop updater refuses install when the app folder is not writable and escapes PowerShell paths safely

## [1.4.3] - 2026-08-08

### Fixed

- Windows **Global + TUN** no internet — bump Xray to **v26.7.28** and emit official TUN settings (`gateway`, `dns`, `autoSystemRoutingTable`, `autoOutboundsInterface`) instead of ignored sing-box fields
- Android Connect force-close — always `startForeground` before disconnect handling; establish no longer races `DISCONNECT` via FGS
- Android VPN TUN fd — start Xray with `posix_spawn` + `dup2` so the VpnService fd is inherited (ProcessBuilder closed it); track/`Os.Close` detached TUN fds
- Unhandled Android exceptions marked handled so they log instead of force-closing the app
- Remove marketing subtitle (“Anti-censorship proxy hub…” / “Android Sentinel — …”) from the main UI

## [1.4.2] - 2026-08-08

### Fixed

- Windows TUN start no longer fails silently — ship `wintun.dll` with cores and surface real Xray stdout (missing WinTun / access denied / port conflicts) instead of generic “exited immediately”
- Android Connect stability — VpnService returns the system binder, excludes the app from the tunnel, uses gVisor when a TUN fd is provided, and ignores unexpected-exit teardown during startup so failed connects do not race-crash the UI
- Desktop CA1416: guard Windows-only admin check behind `OperatingSystem.IsWindows()`

## [1.4.1] - 2026-08-06

### Fixed

- Windows **TUN + kill switch** no longer blackholes browsing — allow outbound on the `v2rayF` TUN adapter before the block-all firewall rule (Connected could previously show while apps had no path)
- If the TUN allow rule cannot be added, kill switch stays disarmed and status shows the error instead of leaving a silent offline session

## [1.4.0] - 2026-08-06

### Added

- **Encrypted profile vault** — sensitive fields in `settings.json` / `servers.json` encrypted at rest (DPAPI on Windows, Android Keystore-wrapped key, restricted key file on macOS/Linux); session lock/unlock; passphrase-protected `.v2rayf` export/import
- **Adaptive Survive** — when Smart Connect failover fails, temporarily escalate packet fragment and Sentinel DNS/IPv6/Global tactics without permanently rewriting user prefs (persists only a last-successful tactic hint)
- Unit tests (`v2rayF.Core.Tests`) and CI `dotnet test` step
- Release `SHA256SUMS` asset for verified in-app updates

### Changed

- **Config versatility** — share-link import + Xray builder cover Vision on REALITY *or* TLS, Trojan REALITY/WS, and transports TCP (HTTP header), WS, gRPC (multi), H2, HTTPUpgrade, xHTTP/SplitHTTP, mKCP, QUIC, plus ALPN/fp/serviceName/mode/seed
- **Connect agility** — Smart Connect early-exits after 3 working proxy paths (max 6 path probes); dead TCP nodes skipped; per-probe budget ~4.5s; generate_204 URLs race first-success
- Adaptive Survive retries only the top 2 candidates per tactic (not the full failover list × tactics)
- **Test All** is parallel TCP-only (Test still prefers proxy-path)
- Runtime/speedtest Xray JSON is compact; process hosts await Stop without sync-over-async; Android EnsureCore caches readiness and extracts geo files in parallel

### Security

- Update downloads restricted to GitHub hosts; Zip-Slip-safe extract; SHA256 required before install
- Android `allowBackup="false"`
- Secure Share defaults to LAN IP bind (not `0.0.0.0`); optional listen-all; password masked until vault unlock + reveal; copy-once / rotate controls

## [1.3.2] - 2026-08-06

### Fixed

- Android in-app Update now signs release APKs with a stable keystore (GitHub secrets) so upgrades are no longer rejected with signature mismatch
- Android Update UX — PackageInstaller status callbacks, signature-mismatch guidance (uninstall once if upgrading from 1.3.1 and earlier CI builds), clear IsUpdating after opening the installer, refresh update check on resume
- Android `versionCode` derived as `major*10000+minor*100+patch` (1.3.2 → 10302) so upgrades always increase the package version code
- Android version label prefers package `VersionName` for update comparisons

## [1.3.1] - 2026-08-06

### Fixed

- Android Connect crash — Avalonia UI property updates after VPN/Xray awaits are marshalled back to the UI thread (fixes “different thread owns it” and app close on Connect)
- Android APK `ApplicationDisplayVersion` / version code now match the release version (was stuck on 1.2.2)

## [1.3.0] - 2026-08-04

### Added

- **Sentinel** anti-censorship profile (Global + kill switch + DNS through proxy + IPv6 block)
- **Smart Connect** — probe proxy path and connect to the fastest working node with failover
- **Smart Multipath** — Xray `burstObservatory` + `leastPing` balancer across top nodes
- **Secure Share** — authenticated LAN SOCKS/HTTP gateway for phone↔PC / hotspot clients
- Desktop **kill switch** (Windows Firewall) and Android VPN-as-kill-switch
- Custom routing **Direct / Proxy / Block** lists; Android per-app VPN bypass
- Optional TLS **packet fragment** DPI evasion; subscription fetch via local proxy when connected
- Connection state machine, core health watchdog, unexpected-exit teardown

### Fixed

- TUN DNS no longer forced to `direct` (ISP hostname leak)
- DNS module traffic tagged and routed through the proxy when enabled
- Android IPv6 catch-all route when Block IPv6 is on
- Core process death now clears Connected state and tears down platform proxy/VPN
- Kill switch stays armed after unexpected drop (fail-closed); Disconnect releases it
- Kill switch arms after core is ready (no blocked dial); Windows-only firewall rules
- Smart Connect TCP prefilter before proxy probes; prefer proxy-path OK peers only
- Packet fragment skipped for Vision flows; settings UI collapsed under Advanced

## [1.2.2] - 2026-06-30

### Fixed

- Latency test now reports **TCP RTT to the node** (same metric as v2rayNG/Hiddify), not inflated full-proxy HTTP timing
- REALITY `spiderX` no longer incorrectly reuses WebSocket `path`; reads `spx` from share links
- Xray config always includes DNS servers (not only on Android VPN)
- Bypass LAN routing uses explicit private CIDR ranges instead of `geoip:private` (works without geo files on desktop)

## [1.2.1] - 2026-06-30

### Fixed

- Linux/macOS launcher scripts now support both `v2rayF` and `v2rayF.Desktop` binary names
- Desktop zip startup reliability improved across packaging layout variations

## [1.2.0] - 2026-06-29

### Added

- In-app **Update** button when a newer release exists on [GitHub](https://github.com/drmikecrypto/v2rayF/releases)
- Desktop: downloads your platform zip, swaps files in place, restarts — settings and servers stay in AppData
- Android: downloads the release APK and opens the system installer (one confirm tap; no data loss)

## [1.1.9] - 2026-06-29

### Fixed

- Server list — each configuration now has its own **×** remove button (Android and desktop)
- Remove no longer depends on flaky ListBox selection on touch devices; toolbar **Remove** still works on the selected row
- Removing the active server disconnects first, then deletes from storage

## [1.1.8] - 2026-06-29

### Fixed

- Android connect crash — pass VPN TUN fd to Xray via `XRAY_TUN_FD` environment variable (required on Android)
- Android connect — set `LD_LIBRARY_PATH` when launching Xray so native dependencies load on Samsung and similar devices
- Android connect — removed double Xray start on connect (VPN first, single core launch)
- Android connect — clearer error when ProcessBuilder cannot exec the core instead of silent force-close

## [1.1.7] - 2026-06-28

### Fixed

- Android connect crash — Xray now starts via Java `ProcessBuilder` instead of `System.Diagnostics.Process` (fixes force-close on Samsung and other devices)
- Android connect — proxy core resolves the process host at runtime so the Android host is always used
- Android connect — status and busy state updates are marshalled to the UI thread after every async step
- Latency tests on Android use the same Java process launcher (no .NET `Process` on mobile)

## [1.1.6] - 2026-06-28

### Fixed

- Android connect crash — VPN permission and UI now run on the main thread (required by Android)
- Android connect crash — Xray process no longer uses stdout/stderr redirection (unsupported on Android)
- Connect flow stays on the Avalonia UI thread end-to-end on mobile

## [1.1.5] - 2026-06-28

### Fixed

- Android connect crash — UI updates no longer run on a background thread after VPN setup
- Android connect no longer enables VPN before Xray is verified (internet stays working during connect)
- Failed connect always tears down VPN immediately so traffic is not left routed into a dead tunnel
- VPN uses tunnel DNS and smaller MTU (1280) for unreliable networks

### Changed

- Android falls back to Bypass LAN when geo files are missing instead of blocking connect

## [1.1.4] - 2026-06-28

### Fixed

- Android connect crash — VPN foreground service now declares `specialUse` type (required on Android 14+)
- Android geo files status — geo assets are extracted on startup before status is shown
- Android notification permission requested on launch (required for VPN foreground notification)

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

[1.4.10]: https://github.com/drmikecrypto/v2rayF/releases/tag/v1.4.10
[1.4.9]: https://github.com/drmikecrypto/v2rayF/releases/tag/v1.4.9
[1.4.8]: https://github.com/drmikecrypto/v2rayF/releases/tag/v1.4.8
[1.4.7]: https://github.com/drmikecrypto/v2rayF/releases/tag/v1.4.7
[1.4.6]: https://github.com/drmikecrypto/v2rayF/releases/tag/v1.4.6
[1.4.5]: https://github.com/drmikecrypto/v2rayF/releases/tag/v1.4.5
[1.4.4]: https://github.com/drmikecrypto/v2rayF/releases/tag/v1.4.4
[1.4.3]: https://github.com/drmikecrypto/v2rayF/releases/tag/v1.4.3
[1.4.2]: https://github.com/drmikecrypto/v2rayF/releases/tag/v1.4.2
[1.4.1]: https://github.com/drmikecrypto/v2rayF/releases/tag/v1.4.1
[1.4.0]: https://github.com/drmikecrypto/v2rayF/releases/tag/v1.4.0
[1.1.6]: https://github.com/drmikecrypto/v2rayF/releases/tag/v1.1.6
[1.1.5]: https://github.com/drmikecrypto/v2rayF/releases/tag/v1.1.5
[1.1.4]: https://github.com/drmikecrypto/v2rayF/releases/tag/v1.1.4
[1.1.3]: https://github.com/drmikecrypto/v2rayF/releases/tag/v1.1.3
[1.2.2]: https://github.com/drmikecrypto/v2rayF/releases/tag/v1.2.2
[1.2.1]: https://github.com/drmikecrypto/v2rayF/releases/tag/v1.2.1
