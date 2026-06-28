# GeoIP and GeoSite data

Desktop packages include `geoip.dat` and `geosite.dat` beside the Xray core under `cores/`.

Routing presets reference these files for country and domain rules. If you replace the Xray binary manually, keep geo files in sync with your Xray version to avoid rule parse errors.

Android APK bundles the same files internally.