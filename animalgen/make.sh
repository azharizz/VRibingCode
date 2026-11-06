#!/bin/bash
# Build + test + preview one animal:   ./animalgen/make.sh fox
# Exit code != 0 if any animation/export test fails (the FBX is still written for inspection).
set -euo pipefail
cd "$(dirname "$0")/.."
A=${1:-fox}
if [ "$A" = all ]; then  # every recipe; exit 1 if any animal fails
  rc=0; for a in $(python3 -c "import sys; sys.path.insert(0,'animalgen'); from recipes import RECIPES; print(*RECIPES)"); do
    "$0" "$a" || rc=1; done; exit $rc
fi
B=${BLENDER:-/Applications/Blender.app/Contents/MacOS/Blender}
"$B" -b --factory-startup -P animalgen/build.py -- "$A" 2>&1 | grep -E "\[build\]|Error|Traceback" || true
"$B" -b "animalgen/out/$A/$A.blend" -P animalgen/test.py -- "$A" 2>&1 | grep -E "\[test\]|Traceback"
status=${PIPESTATUS[0]}
"$B" -b "animalgen/out/$A/$A.blend" -P animalgen/preview.py -- "$A" 2>&1 | grep -E "\[preview\]|Traceback" || true
echo "previews: animalgen/out/$A/ (turntable.png, <Clip>_sheet.png, <Clip>.mp4)"
exit "$status"
