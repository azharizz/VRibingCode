#!/usr/bin/env bash
# Local Qwen3-ASR for Apple Silicon. Everything lands in backend/data/stt/qwen (gitignored).
set -euo pipefail
DIR="$(cd "$(dirname "$0")/.." && pwd)/data/stt/qwen"
mkdir -p "$DIR"
PY="$(command -v python3.12 || command -v python3)"
[ -x "$DIR/venv/bin/python" ] || "$PY" -m venv "$DIR/venv"
"$DIR/venv/bin/pip" -q install -U pip
"$DIR/venv/bin/pip" -q install mlx-audio soundfile fastapi uvicorn python-multipart
# curl: the Hugging Face client crawled here, and curl can resume a broken download
BASE=https://huggingface.co/mlx-community/Qwen3-ASR-1.7B-8bit/resolve/main
mkdir -p "$DIR/model"
for f in chat_template.json config.json generation_config.json merges.txt model.safetensors.index.json preprocessor_config.json tokenizer_config.json vocab.json model.safetensors; do
  curl -L --fail -C - --retry 5 -o "$DIR/model/$f" "$BASE/$f"
done
echo "done. Run backend/tools/start-qwen.sh"
