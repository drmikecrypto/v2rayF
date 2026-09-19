# Gaming Boost

Honest GearUP-inspired posture for **your** exit nodes — not a private game backbone.

## What GearUP does (and we do not)

[GearUP](https://www.gearupbooster.com/) routes game traffic through its own Adaptive Intelligent Routing (AIR) overlay. v2rayF cannot invent that network. Gaming Boost maximizes UDP-friendly behavior on the node **you** paste in.

## What Gaming Boost applies

One tap **Gaming Boost** in Settings (then **Save settings**):

| Lever | Value |
|-------|--------|
| Chromium HTTP assist (10809) | Off — full TUN like V2Box |
| Packet fragment | Off (RTT poison) |
| Adaptive Survive | Off (no mid-match rewrite) |
| Smart Multipath | On (Xray `leastPing`; Hy2/TUIC already UDP-native) |
| Smart Connect | On — ranks with **Gaming** profile (prefer Hy2/TUIC/WG when close) |
| Observatory interval | 15s when multipath + Gaming (vs 1m) |
| Path health | Tighter probes while Connected |

Tip after apply: *Optimizes your exit for UDP games… not a private booster backbone.*

## Optional: game catalog → App Network Direct

**Apply game catalog → App Network Direct** merges Discord / Steam / PUBG / Riot / etc. packages into App Network Direct (and known CDN domains into Custom Direct). Use this when you want downloads/clearnet for those apps while keeping the rest on TUN.

Default Gaming Boost does **not** force games Direct — IR/CN users need full tunnel.

## Lab scorecard

See [game-v2box-scorecard.md](game-v2box-scorecard.md). Expect Play Store / Translate to fail with assist off. UDP / Unity row should match or beat V2Box on the same link.

## Not in scope

- Building a paid AIR node network
- Packet-duplication FEC
- Silent Survive / traffic rewrite
- Claiming lower ping than physics + your exit allow
