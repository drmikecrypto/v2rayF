# Free servers (on your network)

## What the Free button does

1. Soft-refreshes the public shortlist pool (background)
2. Downloads a diverse candidate list (via Cloudflare Worker when possible)
3. **Tests each candidate on your device** (your ISP path)
4. Prefers servers with latency **1–150ms**; if slots remain empty, fills up to **450ms** (typical Iran→edge RTT)
5. Maintains at most **5 Free-tagged** servers in your list

Re-tap: Free servers that are still fast enough stay; slow / failed / missing Free slots are replaced. Your own imported servers are never touched.

## Tips

- Private DNS: Off
- Battery: unrestricted for v2rayF
- If zero servers pass, your network may be heavily filtered — try cellular vs Wi‑Fi, or later
- Free nodes are **untrusted** public proxies — do not use for banks / email / personal accounts

## Manual URLs (advanced)

```text
https://pulseconfigs-mirror.drmikecrypto.workers.dev/candidates.json
https://raw.githubusercontent.com/drmikecrypto/PulseConfigs/main/candidates.json
https://raw.githubusercontent.com/drmikecrypto/PulseConfigs/main/top5.txt
```

Machine contract: `index.json` → `urls.candidates` / `v2rayF.top5_button`.

## فارسی

- دکمه **Free** تا ۵ سرور روی **شبکه شما** اضافه می‌کند (هدف ≤۱۵۰ms، در صورت نیاز تا ۴۵۰ms)
- فشار دوباره فقط سرورهای Free کند را عوض می‌کند
- سرورهای رایگان قابل اعتماد نیستند

## 中文

- **Free** 最多保留 5 个在你网络上可用的免费节点（优先 ≤150ms，必要时 ≤450ms）
- 再次点击只替换变慢的 Free 槽位
- 免费节点不可信，勿用于敏感账号
