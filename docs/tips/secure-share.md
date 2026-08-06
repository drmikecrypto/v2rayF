# Secure Share (tunnel gateway)

Secure Share exposes authenticated **SOCKS5** and **HTTP** proxies on your LAN while connected, so other devices (phone↔PC, PC↔phone, hotspot clients that support a proxy) use the same tunnel exit.

## How to use

1. Enable **Secure Share** in settings and connect.
2. **Unlock** the profile vault (Advanced), optionally **Reveal password**, then **Copy SOCKS (once)**.
3. On the client device, set that as the system or app proxy.

HTTP share listens on **port + 1** (default SOCKS `10880`, HTTP `10881`).

## Bind address

By default Secure Share binds to your **primary LAN IPv4** only (not all interfaces). Enable **Listen on all interfaces** only if clients cannot reach that address.

## Credentials

Credentials are auto-generated on first enable and stored encrypted at rest (1.4+). Use **Rotate password** after unlock, then reconnect so Xray picks up the new password.

## Hotspot / tethering reality check

Many Android OEM **Wi‑Fi hotspots bypass VpnService**. Do not assume tethered traffic is covered by the VPN alone.

| Scenario | Recommended path |
|----------|------------------|
| Phone → PC / PC → Phone | Point client at Secure Share SOCKS/HTTP |
| PC ICS / Windows hotspot | Share with TUN up + client proxy, or route clients via Secure Share |
| USB tethering | Prefer Secure Share proxy on the host |
