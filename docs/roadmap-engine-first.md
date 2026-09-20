# Engine-first roadmap

**Canonical plan:** [`PLAN.md`](PLAN.md) — trusted tunnel, Android-first for CN/IR, no GitHub push until maintainer approval.

Shipped through v2.2.2 (kept):

- Phase A: default DoH for Connect + DoH retry. Speedtest DNS stays UDP.
- Phase B: Android classic on sing-box TUN; desktop classic on Xray; Hy2/TUIC/WG on sing-box.

Windows TUN fail-closed (missing `v2rayF` adapter) landed with the Phase 4 reliability bar. **Gaming Boost** (UDP-aware rank, hotter leastPing, assist/fragment off) is the first honest multipath/gaming slice — see [`tips/gaming-boost.md`](tips/gaming-boost.md). **Phase C** (UX / multipath polish / richer diagnostics) is **open** after field soak of **v2.6.4.1** (Android + Windows) — see [`tips/phase-c.md`](tips/phase-c.md). Lab: `scripts/run-golden-matrix-lab.ps1`.
