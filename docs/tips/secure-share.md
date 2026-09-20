# Secure Share (tunnel gateway)

Secure Share exposes authenticated **SOCKS5** and **HTTP** proxies on your LAN while Connected, so other devices use the **same tunnel exit**. Clients set a proxy **once**. This is the supported multi-device path — not transparent hotspot NAT.

Works with **Xray** (desktop classic) and **sing-box** (Android classic Connect, Hy2/TUIC/WG).

## How to use

1. Connect on the host (phone or PC).
2. Settings → enable **Secure Share (LAN gateway)** → Save → **reconnect** if you were already connected.
3. Unlock the profile vault → optionally **Reveal password**.
4. **Copy SOCKS**, **Copy HTTP**, or **Copy setup tip** and configure the other device’s system/app proxy.
5. Optional: **Listen on all interfaces** when SoftAP/hotspot clients cannot reach the advertised IP.

Default ports: SOCKS **10880**, HTTP **10881** (`ShareBindPort` + 1).

## Bind address and hotspot IP

- By default the core binds to the **preferred advertise IPv4** (SoftAP / Mobile Hotspot / Wi‑Fi Direct preferred over plain Wi‑Fi/Ethernet).
- WinTun `v2rayF` / `172.19.0.x` addresses are never advertised.
- If clients still cannot connect, enable **Listen on all interfaces** (`0.0.0.0`) and reconnect.
- The Connected endpoint line lists alternate IPs when several NICs are up.

## Credentials

Auto-generated on first enable; stored encrypted at rest. **Rotate** after vault unlock, then reconnect so the core picks up the new password.

## Windows firewall

While Connected with Secure Share on, v2rayF adds temporary **inbound** allow rules for the share ports (Private/Domain). Rules are removed on Disconnect. SoftAP should use a **Private** network profile. Kill switch remains outbound-only and does not replace these inbound rules.

## Hotspot / tethering reality check

Many Android OEM **Wi‑Fi hotspots bypass VpnService**. Do not assume tethered traffic rides the VPN alone.

| Scenario | Supported path |
|----------|----------------|
| Phone → PC / PC → Phone | Secure Share SOCKS/HTTP |
| Host Mobile Hotspot + clients | Secure Share (set proxy on clients) |
| USB tethering | Secure Share on the host |
| Transparent ICS / zero-config hotspot | **Not supported** |

## Verify

On the client, open a what-is-my-ip site after setting the proxy — it should match the host’s tunnel exit IP (not the host ISP clearnet IP).
