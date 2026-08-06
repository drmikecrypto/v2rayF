# Latency testing

**Test** (selected server) prefers a **proxy-path** probe when Xray is available (generate_204 through a temporary SOCKS), then falls back to TCP RTT.

**Test All** measures **TCP round-trip time** only, in parallel (bounded), so large lists stay agile — matching the node RTT most clients show (v2rayNG, Hiddify, Nekoray, etc.).

## Smart Connect

Smart Connect uses a TCP prefilter, then proxy-path probes on a shortlist. It **early-exits** after a few working proxy paths and bounds each probe so ranking cannot stall the connect button for minutes.

## Results

| Display | Meaning |
|---------|---------|
| `123 ms` | Successful measurement |
| `timeout` | No response within the probe budget |
| `—` | Test in progress or not run yet |

After **Test All**, results are saved with your server list.
