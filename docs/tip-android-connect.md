# Tip: Android Connect troubleshooting

1. Prefer the in-app **Update** button when a new GitHub release is available — it downloads the signed APK, verifies SHA256, and installs over the existing app (native cores refresh automatically).
2. Tap **Connect** and allow the **VPN** permission when prompted.
3. If connect fails, read the status message — the app tears down VPN so normal internet keeps working.
4. Uninstall first only if the installer reports a **signature mismatch** (very old sideload builds before stable signing).

## v2.3.2 — Play Store + Direct real DNS

**2.3.2** restores VPN HTTP proxy (Play Store / Translate) and resolves Instagram/Facebook **without FakeIP** so Direct MQTT can dial public IPs. WhatsApp/Telegram stay on FakeIP.

## v2.3.1 — Instagram Direct (drop VPN HTTP proxy)

**2.3.1** removes Android VPN HTTP proxy so Instagram **Direct** uses TUN like WhatsApp/Telegram. Feed still works via TUN. After Update: force-stop Instagram once.

## v2.3.0 — WhatsApp / Telegram / Direct (FakeIP + gVisor)

**2.3.0** uses full **gVisor** TUN and **FakeIP** DNS for messaging apps. If chats stay offline while feed works on 2.2.x, Update to **2.3.0**, then force-stop those apps once.

## v2.2.10 — Direct + Telegram (TUN UDP)

**2.2.10** switches Android TUN to **`mixed`** stack and forces UDP DNS for VpnService. If Direct/Telegram stay offline while feed works on 2.2.9, Update to **2.2.10**, then force-stop those apps once.

## v2.2.9 — Instagram Direct TUN DNS

**2.2.9** hijacks VPN DNS (`172.19.0.1:53`) into sing-box so Instagram **Direct** (MQTT) works again. Feed/reels already used HTTP proxy `10809`. After Update: Private DNS Off → Connect → **force-stop Instagram once**.

## v2.2.8 — sing-box 1.12 DNS (Connect fix)

**2.2.8** migrates live sing-box DNS to the 1.12 schema. If Connect showed `did not become ready in time: …migrate-to-new-dns-server-formats`, update to **2.2.8**.

## v2.2.6 — sing-box TUN fd (not JSON `file_descriptor`)

sing-box **1.12** rejects `file_descriptor` in config. **2.2.6** passes the VPN fd via **`SING_BOX_TUN_FD`** to a patched **libsingbox.so** (built in release CI). Use in-app **Update** — do not stay on 2.2.5 for Connect.

## v2.2.4 — sing-box bundled in APK

Connect needs **libsingbox.so** for Android classic (and Hy2). Older APKs only shipped libxray.so. **2.2.4** includes both; in-app Update is enough.

## v2.2.3 — Test delay on Xray, Connect on sing-box

**Test delay / ping** for classic protocols uses **Xray**. Live **Connect** on Android classic uses **sing-box** TUN for Instagram Direct.

## v2.2.2 — Instagram Direct + classic sing-box TUN

Instagram **feed/reels** often use VPN HTTP proxy `10809`. **Direct** (MQTT / raw sockets) must go through **TUN**.

After Connect: set Android **Private DNS** Off, then **force-stop Instagram** once before opening Direct.

## Idle “Connected” but no internet

**v2.0.6** keepalive + soft path probe; Auto-reconnect up to twice.

## Chrome / WhatsApp / Instagram

IPv6 blackhole + VPN DNS `172.19.0.1` (v2.0.5). HTTP proxy for Chromium/feed; raw-socket chat needs healthy TUN (sing-box on Android classic).

## Still broken?

```bash
adb logcat -s v2rayF AndroidRuntime
```

## Product polish (Phase C — deferred)

UI redesign and SpeedyFi-style multi-WAN aggregation are tracked **after** single-link Android speed matches V2Box on the same configs. See [docs/roadmap-engine-first.md](roadmap-engine-first.md).
