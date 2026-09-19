# Free servers (≤150ms on your network)

## What the Free button does

1. Soft-refreshes the public shortlist pool (background)
2. Downloads a diverse candidate list
3. **Tests each candidate on your device** (your ISP path)
4. Keeps only servers with latency **1–150ms**
5. Maintains at most **5 Free-tagged** servers in your list

Re-tap: Free servers that are still ≤150ms stay; slow / failed / missing Free slots are replaced. Your own imported servers are never touched.

## Tips

- Private DNS: Off
- Battery: unrestricted for v2rayF
- If zero servers pass 150ms, your network may be heavily filtered — try again later or another ISP path
- Free nodes are **untrusted** public proxies — do not use for banks / email / personal accounts

## Manual URLs (advanced)

```text
https://raw.githubusercontent.com/drmikecrypto/PulseConfigs/main/candidates.json
https://raw.githubusercontent.com/drmikecrypto/PulseConfigs/main/top5.txt
```

Machine contract: `index.json` → `urls.candidates` / `v2rayF.top5_button`.

## فارسی

- دکمه **Free** تا ۵ سرور با تأخیر ≤۱۵۰ms روی **شبکه شما** اضافه می‌کند
- فشار دوباره فقط سرورهای Free کند را عوض می‌کند
- سرورهای رایگان قابل اعتماد نیستند

## 中文

- **Free** 最多保留 5 个在你网络上 ≤150ms 的免费节点
- 再次点击只替换变慢的 Free 槽位
- 免费节点不可信，勿用于敏感账号
