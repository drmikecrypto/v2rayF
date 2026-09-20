# Phase C — UX / multipath polish / richer diagnostics

**Status:** Open. Field soak of **v2.6.4.1** accepted (Android phone + Windows). Gaming Boost remains the first honest multipath/gaming slice — see [`gaming-boost.md`](gaming-boost.md).

Canonical plan: [`PLAN.md`](../PLAN.md).

## Scope

### UX polish
- Clearer Connected / weak-TUN / assist path messaging (path-truth UI + recover CTAs)
- Daily vs Gaming Boost discoverability without burying Assist off semantics
- Scorecard export + **session diagnostics** copy (Connect → SOCKS → TUN → soft recovery)

### Multipath polish
- Build on Gaming Boost: UDP-aware rank, observatory interval, Soft Multipath honesty when paths flake
- Avoid silent Survive / traffic rewrite (PLAN forbid list)
- No GearUP-style fake acceleration claims

### Richer diagnostics
- Exportable session timeline via Settings → **Copy session diagnostics**
- Surface `TunVpnMissingMs` / weak-TUN tip reasons in the blob
- Soft recovery / lock-unlock resume events recorded while Connected

## Explicit non-goals (still deferred)
- iOS
- macOS/Linux real TUN (honesty message stays)
- UX redesign as a substitute for tunnel correctness
