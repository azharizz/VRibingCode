#!/usr/bin/env bash
# VIRTUAL LOFT — adb-install.sh
# Install a built loft APK to Quest 3/3S from Mac (no Quest Link on macOS).
# Usage: ./adb-install.sh [path/to/app.apk]   (default: ../Build/loft-focus.apk)
set -euo pipefail

APK="${1:-../Build/loft-focus.apk}"

echo "==> Checking adb..."
if ! command -v adb >/dev/null 2>&1; then
  echo "ERROR: 'adb' not found."
  echo "  Install via: brew install android-platform-tools"
  echo "  ...or use the adb bundled with Meta Quest Developer Hub 6.5.2 for Mac."
  exit 1
fi

echo "==> adb version:"
adb version

echo "==> Connected devices (accept the USB debugging prompt in-headset if empty):"
adb devices -l
if [ "$(adb devices | tail -n +2 | grep -c device)" -eq 0 ]; then
  echo "WARNING: no device listed. Enable Developer Mode, connect USB/Wi-Fi, then retry."
  exit 1
fi

if [ ! -f "$APK" ]; then
  echo "ERROR: APK not found: $APK"
  echo "  Build it first: Unity -> Android platform -> Build Settings -> Build."
  exit 1
fi

echo "==> Installing (-r reinstall, keep data): $APK"
adb install -r "$APK"

echo "==> Installed. App version on device:"
PKG=$(aapt dump badging "$APK" 2>/dev/null | sed -n "s/^package:.*name='\([^']*\)'.*/\1/p" || true)
if [ -n "${PKG:-}" ]; then
  adb shell dumpsys package "$PKG" | grep -m1 versionName || true
else
  echo "(aapt unavailable — version print skipped; install succeeded.)"
fi
echo "DONE. Open the app on Quest 3/3S (Unknown Sources / Library)."
