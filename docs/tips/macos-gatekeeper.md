# macOS Gatekeeper

Unsigned desktop builds may be blocked on first launch.

**Option A â€” remove quarantine flag:**

```bash
xattr -cr /path/to/extracted/v2rayF-folder
```

**Option B â€” one-time open:** Right-click `run-v2rayF.sh` or the app bundle â†’ **Open** â†’ confirm.

This is expected for open-source releases not notarized through Apple Developer ID.