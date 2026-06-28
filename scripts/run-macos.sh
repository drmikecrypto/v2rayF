#!/usr/bin/env bash
set -euo pipefail
DIR="$(cd "$(dirname "$0")" && pwd)"
xattr -cr "$DIR" 2>/dev/null || true
chmod +x "$DIR/cores/xray" "$DIR/v2rayF" "$0" 2>/dev/null || true
exec "$DIR/v2rayF" "$@"
