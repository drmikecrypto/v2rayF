# Pulse free servers + hard-filter tips (fa / zh / en)

## Free servers (Pulse)

v2rayF’s **Free (Pulse)** button imports up to **5** public configs from [PulseConfigs](https://github.com/drmikecrypto/PulseConfigs).

- Prefer the Cloudflare Worker mirror when GitHub raw is blocked
- Nodes are **untrusted** free proxies — do not use for banking / email / personal accounts
- “Verified” means a probe could fetch HTTP through the node; Iran L4 probes improve the Free pack when online

Manual URLs:

```text
https://raw.githubusercontent.com/drmikecrypto/PulseConfigs/main/top5.txt
https://cdn.jsdelivr.net/gh/drmikecrypto/PulseConfigs@main/top5.txt
```

Machine contract: `index.json` → `v2rayF.top5_button`.

## Hard-filter days (Iran / China)

1. **Private DNS:** Off (Settings → Network → Private DNS)
2. **Battery:** unrestricted / ignore optimizations for v2rayF
3. **TLS fragment:** enable ClientHello fragment / `tlshello` in routing/settings when CF Workers or SNI DPI times out (engine support varies by core build)
4. **DoH:** use DNS over HTTPS inside the tunnel once connected; avoid ISP DNS for proxy domains
5. **MTU:** try ~1400 on mobile if pages stall after connect
6. **Strategy diversity:** if REALITY fails immediately, try CDN WS/gRPC/HTTPUpgrade entries from the Free pack — do not rely on one protocol

## فارسی

- دکمه **Free (Pulse)** حداکثر ۵ سرور رایگان از PulseConfigs وارد می‌کند
- DNS خصوصی را خاموش کنید؛ بهینه‌سازی باتری را برای v2rayF غیرفعال کنید
- در روزهای فیلترینگ شدید، fragment و DoH را فعال کنید؛ اگر REALITY قطع شد از گزینه‌های CDN پک استفاده کنید
- سرورهای رایگان قابل اعتماد نیستند

## 中文

- **Free (Pulse)** 最多导入 5 个 PulseConfigs 免费节点
- 关闭私人 DNS；关闭电池优化
- 严格审查时启用 TLS fragment / DoH；REALITY 失败时改试 CDN 类节点
- 免费节点不可信，勿用于敏感账号
