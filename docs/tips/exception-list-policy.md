# Exception-list shrink policy

Hand-maintained domain/package lists are **glue**. Prefer correct TUN + DNS + IPv6 fail-closed.

## Rules

1. Do not add a host/package unless the golden matrix fails without it.
2. Every addition needs a one-line comment citing the app/symptom and a removal plan.
3. Prefer `domain_suffix` over long exact-host tables when safe.
4. Meta MQTT HTTP-proxy exclusions stay minimal (realtime hosts only) — not full Meta CDNs.
5. Review [`PushRoutingDomains.cs`](../src/v2rayF.Core/Services/PushRoutingDomains.cs) when changing DNS or Bypass China.

## Current intentional lists

| List | Why it still exists |
|------|---------------------|
| FCM exact hosts | Must beat Google UDP/443 Chromium block |
| Meta MQTT exclusions | VpnService HTTP CONNECT breaks Instagram Direct |
| Messaging suffixes | Explicit proxy until scorecard proves dns.final alone |
| OEM push suffixes | Vendor push CDNs outside Google |
