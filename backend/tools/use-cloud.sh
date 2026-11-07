#!/bin/zsh
# Point the Mac app at the cloud backend, or back at this machine, then restart it.
#   zsh backend/tools/use-cloud.sh on | off
cd "$(dirname "$0")/../.."
FILE="$HOME/Library/Application Support/com.DefaultCompany.quest-loft/loft-server.json"
case "${1:-}" in
  on)
    [ -f .env.cloud ] || { echo "no .env.cloud: run tools/deploy-gcp.sh first"; exit 1; }
    python3 - "$FILE" <<'PY'
import json, os, sys
v = dict(l.strip().split("=", 1) for l in open(".env.cloud") if "=" in l)
os.makedirs(os.path.dirname(sys.argv[1]), exist_ok=True)
json.dump({"url": v["LOFT_URL"], "token": v["LOFT_TOKEN"]}, open(sys.argv[1], "w"))
PY
    chmod 600 "$FILE"; echo "app -> cloud" ;;
  off) rm -f "$FILE"; echo "app -> this machine" ;;
  *) echo "usage: use-cloud.sh on|off"; exit 1 ;;
esac
pkill -f "Loft.app/Contents/MacOS"; sleep 2
open ~/Applications/Loft.app
