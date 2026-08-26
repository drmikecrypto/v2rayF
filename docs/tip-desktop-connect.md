# Tip: Desktop Connect troubleshooting

1. Prefer the in-app **Update** button when a new GitHub release is available.
2. Enable **TUN mode** for full-device VPN (Sentinel profile does this automatically).
3. **Connect** — status should reach Connected within a few seconds.
4. If browsing works but toasts do not, see **Windows notifications** below.

## v2.6.1 — faster Connect + Update

**2.6.1** parallelizes Smart Connect ranking and races health probes so Connect feels snappy on Reality Vision, WS, gRPC, and the rest of the Sentinel set. In-app **Update** retries downloads and no longer hides the button when GitHub is briefly unreachable.

## v2.6.0 — Windows toast notifications

**2.6.0** adds high-priority routing for WNS/push domains (`wns.windows.com`, `notify.windows.com`, `push.services.microsoft.com`, `mp.microsoft.com`) through the proxy when TUN is on. Settings → **Route Windows notifications via proxy** (on by default).

If Telegram/Discord/Phone Link toasts still fail:

- Confirm TUN + Connected (not system-proxy-only).
- Check **App Network** — blocked apps never get push traffic.
- If the tunnel crashed, **kill switch** may block clearnet until Disconnect or reboot.
- Disable **Private DNS** / problematic IPv6 paths on the OS if WNS resolves but never delivers.

## v2.6.0 — Startup server ranking

On open (when not connected), v2rayF ranks all servers in the background and selects the fastest. Manual row selection is kept if you pick another server before Connect. Throttled to once per 10 minutes — disable via Settings → **Rank servers on startup**.

## Long idle sessions

**2.6.0** sends a lightweight NAT keepalive every 25s when traffic is flat and requires three failed path probes (60s apart) before auto-reconnect — fewer false disconnects on long idle VPN.

## Instagram Direct (Android)

See [tip-android-connect.md](tip-android-connect.md) — **2.6.0** adds `mqtt.facebook.com` / `gateway.facebook.com` plus explicit sing-box TUN route rules for MQTT hosts.
