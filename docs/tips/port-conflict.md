# Local port conflicts

v2rayF uses **10808** (SOCKS) and **10809** (HTTP) on localhost by default.

If connect fails with a port-in-use message:

1. Quit other proxy clients (v2rayN, Clash, etc.) that may bind the same ports.
2. Restart v2rayF and try again.
3. On Windows, check listeners: `netstat -ano | findstr 10808`

On Windows TUN, also ensure `wintun.dll` is present in the app `cores/` folder (shipped with the package). Missing WinTun surfaces as a TUN/wintun error rather than a port conflict.

You can change ports in the generated Xray config under **Settings** if your workflow requires different values.
