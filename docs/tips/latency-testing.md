# Latency testing

**Test** (selected server) and **Test All** measure **proxy-path RTT to `www.google.com`** when Xray is available (HTTP through a temporary local SOCKS). If the proxy path fails, the result falls back to TCP connect RTT to the node's address/port.

Probe URLs (first success wins):

1. `https://www.google.com/generate_204`
2. `https://www.google.com/`

## Smart Connect

Smart Connect uses a TCP prefilter, then proxy-path probes to Google on a shortlist. It **early-exits** after a few working proxy paths and bounds each probe so ranking cannot stall the connect button for minutes.

## Results

| Display | Meaning |
|---------|---------|
| `123 ms` | Successful measurement |
| `timeout` | No response within the probe budget |
| `—` | Test in progress or not run yet |

After **Test All**, results are saved with your server list.
