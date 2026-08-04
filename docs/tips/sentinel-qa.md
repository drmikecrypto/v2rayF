# Sentinel QA checklist

Manual checks after builds that touch connect, DNS, or platform networking.

## Lifecycle

- [ ] Connect → Disconnect within ~2s, UI returns to Idle, no hang
- [ ] Spam Connect while Connecting — second tap ignored / cancel works
- [ ] Kill Xray process while Connected → UI shows drop; **kill switch stays armed** until Disconnect; no indefinite “Connected”
- [ ] Mid-connect Disconnect cancels and tears down VPN/proxy
- [ ] Failover between dead nodes does not open a clearnet window (kill switch stays armed)

## Leak shield

- [ ] With TUN + Sentinel: packet capture shows **no** cleartext DNS to ISP resolvers
- [ ] With Block IPv6: IPv6 sites fail closed / no native IPv6 path outside tunnel
- [ ] Kill switch (desktop, elevated): with tunnel down unexpectedly, clearnet browsers fail

## Smart Connect / Multipath

- [ ] Mixed live/dead servers: Smart Connect picks a working node
- [ ] Multipath with ≥2 nodes: killing one node does not drop all traffic
- [ ] REALITY nodes preferred on equal latency ties

## Routing / Share

- [ ] Custom Direct / Proxy / Block lists affect destinations as expected
- [ ] Android per-app bypass excludes listed packages from VPN
- [ ] Secure Share: second device via SOCKS uses proxy exit IP
- [ ] Subscription refresh while connected with “via proxy” uses `127.0.0.1:10809`

## Regression

- [ ] Clipboard import (vless/vmess/ss/trojan)
- [ ] Subscription import
- [ ] Tray quit disconnects cleanly (desktop)
- [ ] In-app updater still offered when release is newer
