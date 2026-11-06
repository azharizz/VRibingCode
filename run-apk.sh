#!/bin/bash
# VIRTUAL LOFT - USE the APK on your PC (no headset needed)
#   ./run-apk.sh        -> 2D version WITH screen: you can see + tap + walk
#   ./run-apk.sh vr     -> Quest VR version (black screen without headset, for logs only)
#   ./run-apk.sh view   -> open the result + photos from inside the app
#   ./run-apk.sh stop   -> stop everything
#   ./run-apk.sh status -> what's running
cd "$(dirname "$0")"

SDK=$HOME/Library/Android/sdk
ADB=$SDK/platform-tools/adb
APP=com.defaultcompany.focusloft
ACT=com.unity3d.player.UnityPlayerGameActivity
APK2D=quest_loft/Builds/FocusLoft-2D.apk
APKVR=quest_loft/Builds/FocusLoft.apk

cmd=${1:-start}
APK=$APK2D
if [ "$cmd" = "vr" ]; then APK=$APKVR; cmd=start; MODE=VR; else MODE=2D; fi

status() {
  echo "--- devices ---"
  $ADB devices
  echo "--- app pid (empty = not running) ---"
  $ADB shell pidof $APP 2>/dev/null
}

if [ "$cmd" = "stop" ]; then
  echo "Stopping app + emulator..."
  $ADB shell am force-stop $APP 2>/dev/null
  pkill -f "qemu-system.*loftquest" 2>/dev/null
  echo "Stopped."
  exit 0
fi

if [ "$cmd" = "status" ]; then status; exit 0; fi

if [ "$cmd" = "view" ]; then
  OUT=/tmp/loft-apk-view
  mkdir -p $OUT
  $ADB pull /sdcard/Android/data/$APP/files/company-result.txt $OUT/company-result.txt 2>&1 | tail -1
  cat $OUT/company-result.txt
  echo ""
  for f in company-1-assign.ppm company-4-done.ppm gameview.ppm; do
    $ADB pull /sdcard/Android/data/$APP/files/$f $OUT/$f 2>&1 | tail -1
    ffmpeg -y -loglevel error -i $OUT/$f $OUT/$(basename $f .ppm).png
  done
  open $OUT/company-1-assign.png $OUT/company-4-done.png 2>/dev/null
  echo "Folder: $OUT"
  exit 0
fi

# --- start (default = 2D usable version WITH window) ---
[ -f "$APK" ] || { echo "Missing $APK"; exit 1; }

if ! $ADB devices 2>/dev/null | grep -q "emulator.*device$"; then
  echo "Booting emulator WITH screen ($MODE, ~1-2 min)..."
  echo "A phone window will pop up - leave it open."
  export ANDROID_SDK_ROOT=$SDK ANDROID_HOME=$SDK
  if [ "$MODE" = "VR" ]; then
    nohup $SDK/emulator/emulator -avd loftquest -no-window -no-audio -gpu swiftshader_indirect -no-snapshot -memory 3072 > /tmp/loftquest-boot.log 2>&1 &
  else
    # WITH screen so you can see + tap
    nohup $SDK/emulator/emulator -avd loftquest -gpu swiftshader_indirect -no-snapshot -memory 3072 > /tmp/loftquest-boot.log 2>&1 &
  fi
  $ADB wait-for-device
  for i in $(seq 1 30); do
    B=$($ADB shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')
    if [ "$B" = "1" ]; then echo "Emulator booted."; break; fi
    sleep 10
  done
else
  echo "Emulator already up."
fi

echo "Installing $APK..."
$ADB install -r $APK | tail -1
echo "Launching..."
$ADB shell am start -n $APP/$ACT
sleep 8
$ADB shell pidof $APP
echo ""
if [ "$MODE" = "2D" ]; then
  echo "LOOK AT THE EMULATOR WINDOW - the loft is there. Tap ASSIGN, tap pads, walk with WASD in Loft.app style."
  echo "If window didn't pop: open Android Studio > Device Manager > loftquest > Play, then run ./run-apk.sh again."
else
  echo "VR version: screen stays black without headset (normal). Run ./run-apk.sh view for inside photos."
fi
