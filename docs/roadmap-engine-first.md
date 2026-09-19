# Engine-first roadmap

**Canonical plan:** [`PLAN.md`](PLAN.md) — trusted tunnel, Android-first for CN/IR, no GitHub push until maintainer approval.

Shipped through v2.2.2 (kept):

- Phase A: default DoH for Connect + DoH retry. Speedtest DNS stays UDP.
- Phase B: Android classic on sing-box TUN; desktop classic on Xray; Hy2/TUIC/WG on sing-box.

Windows TUN fail-closed (missing `v2rayF` adapter) landed with the Phase 4 reliability bar. **Gaming Boost** (UDP-aware rank, hotter leastPing, assist/fragment off) is the first honest multipath/gaming slice — see [`tips/gaming-boost.md`](tips/gaming-boost.md). Former full “Phase C” UX stays deferred until golden-matrix soak of v2.6.3.4 meets [`PLAN.md`](PLAN.md) exit gates — see [`tips/golden-matrix-2.6.3.4.md`](tips/golden-matrix-2.6.3.4.md).
