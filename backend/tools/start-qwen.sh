#!/usr/bin/env bash
# Starts the local speech server on 127.0.0.1:8124.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
export QWEN_MODEL="${QWEN_MODEL:-$ROOT/data/stt/qwen/model}"
cd "$ROOT/stt-server"
exec "$ROOT/data/stt/qwen/venv/bin/python" -m uvicorn qwen_mlx:app --host 127.0.0.1 --port 8124
