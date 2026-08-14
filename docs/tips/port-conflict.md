# Local port conflicts

v2rayF uses **10808** (SOCKS) and **10809** (HTTP) on localhost by default for the live connection. Stats API uses **10085**.

Latency **Test** / **Test All** / Smart Connect ranking each use a temporary **ephemeral** localhost SOCKS port (not a fixed `10818`), so they should not collide with the live ports.

If connect fails with a port-in-use message:

1. Quit other proxy clients (v2rayN, Clash, etc.) that may bind the same ports.
2. Restart v2rayF and try again.
3. On Windows, check listeners: `netstat -ano | findstr 10808`

On Windows TUN, also ensure `wintun.dll` is present in the app `cores/` folder and the bundled Xray supports `gateway` / `autoSystemRoutingTable` (v1.4.3+). Missing WinTun or an old core surfaces as a TUN error or Connected-with-no-internet when kill switch is on.

You can change ports in the generated Xray config under **Settings** if your workflow requires different values.
