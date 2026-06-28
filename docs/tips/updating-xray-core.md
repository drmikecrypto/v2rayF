# Updating Xray core

Release zips pin a tested Xray build under `cores/`. To upgrade manually:

1. Download a matching platform build from [Xray-core releases](https://github.com/XTLS/Xray-core/releases).
2. Replace the binary in `cores/` (keep filenames expected by v2rayF).
3. Restart the app.

Mismatch between core and config features (e.g. new transport options) can cause start failures â€” check Xray logs in the app if connect fails after an upgrade.