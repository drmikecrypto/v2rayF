# Engine-first roadmap (Beat V2Box)

Shipped through **v2.2.2**:

- Phase A: default DoH for Connect + DoH retry — **kept**. Speedtest DNS stays UDP (v2.2.1; DoH-in-speedtest caused universal timeouts).
- Phase B: Android classic on sing-box TUN (`stack: system`) — **re-enabled in v2.2.2** for Instagram Direct / raw-socket apps; keeps v2.2.1 speedtest safeguards. Desktop classic stays Xray. Hy2/TUIC/WG remain on sing-box.

## Phase C (not in this release)

Track separately once head-to-head Mbps vs V2Box on the same subscription links is competitive:

1. Visual / UX system (brand, motion, hierarchy — not default Avalonia panels)
2. Multi-path / bonding aggregation (SpeedyFi-like) only after single-link engine wins
3. Onboarding + diagnostics that show path RTT vs Mbps honestly

Do not start Phase C as a substitute for engine parity.
