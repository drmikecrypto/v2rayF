# Engine-first roadmap (Beat V2Box)

Shipped in **v2.2.0**, **partially rolled back in v2.2.1**:

- Phase A: default DoH for Connect + DoH retry — **kept**. Speedtest DNS realigned to UDP in 2.2.1 (DoH-in-speedtest caused universal timeouts).
- Phase B: Android classic on sing-box TUN — **rolled back** in 2.2.1 (classic → Xray again). Hy2/TUIC/WG remain on sing-box. Re-attempt only after head-to-head QA.

## Phase C (not in this release)

Track separately once head-to-head Mbps vs V2Box on the same subscription links is competitive:

1. Visual / UX system (brand, motion, hierarchy — not default Avalonia panels)
2. Multi-path / bonding aggregation (SpeedyFi-like) only after single-link engine wins
3. Onboarding + diagnostics that show path RTT vs Mbps honestly

Do not start Phase C as a substitute for engine parity.
