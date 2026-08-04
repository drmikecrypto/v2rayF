# Secure Share (tunnel gateway)

Secure Share exposes authenticated **SOCKS5** and **HTTP** proxies on your LAN while connected, so other devices (phone↔PC, PC↔phone, hotspot clients that support a proxy) use the same tunnel exit.

## How to use

1. Enable **Secure Share** in settings and connect.
2. Copy the shown `socks5://user:pass@LAN-IP:port` endpoint.
3. On the client device, set that as the system or app proxy.

HTTP share listens on **port + 1** (default SOCKS `10880`, HTTP `10881`).

## Hotspot / tethering reality check

Many Android OEM **Wi‑Fi hotspots bypass VpnService**. Do not assume tethered traffic is covered by the VPN alone.

| Scenario | Recommended path |
|----------|------------------|
| Phone → PC / PC → Phone | Point client at Secure Share SOCKS/HTTP |
| PC ICS / Windows hotspot | Share with TUN up + client proxy, or route clients via Secure Share |
| USB tethering | Prefer Secure Share proxy on the host |

Credentials are auto-generated and stored in settings. Regenerate by clearing `ShareAuthPass` in settings JSON or toggling share off/on after wipe.
