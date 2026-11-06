#!/usr/bin/env bash
# Local speech-to-text for the voice API: whisper.cpp + a multilingual ggml model.
# Everything lands in backend/data/stt (gitignored). Usage: tools/setup-stt.sh [model]
# model: tiny | base | small (default) | medium | large-v3-turbo-q5_0 ...  (multilingual only)
set -euo pipefail
MODEL="${1:-small}"
DIR="$(cd "$(dirname "$0")/.." && pwd)/data/stt"
mkdir -p "$DIR/bin"

# 1) the whisper-cli binary: reuse one on PATH, else `brew install whisper-cpp`, else build from source.
#    (On macOS 14 and older Homebrew has no bottles and compiles llvm+rust, hours; source build is ~1 min.)
if [ -x "$DIR/bin/whisper-cli" ]; then
  echo "whisper-cli already at $DIR/bin"
elif command -v whisper-cli >/dev/null 2>&1; then
  echo "using $(command -v whisper-cli)"
else
  if command -v brew >/dev/null 2>&1 && [ "$(sw_vers -productVersion 2>/dev/null | cut -d. -f1)" -ge 15 ] 2>/dev/null; then
    brew install whisper-cpp
  else
    command -v git >/dev/null || { echo "git is required"; exit 1; }
    if ! command -v cmake >/dev/null 2>&1; then
      echo "no cmake: installing it into a throwaway venv"
      python3 -m venv "$DIR/.cmakevenv" && "$DIR/.cmakevenv/bin/pip" install -q cmake
      export PATH="$DIR/.cmakevenv/bin:$PATH"
    fi
    SRC="$(mktemp -d)"
    git clone -q --depth 1 https://github.com/ggml-org/whisper.cpp "$SRC/w"
    cmake -S "$SRC/w" -B "$SRC/w/build" -DCMAKE_BUILD_TYPE=Release -DBUILD_SHARED_LIBS=OFF -DWHISPER_BUILD_TESTS=OFF >/dev/null
    cmake --build "$SRC/w/build" -j"$(sysctl -n hw.ncpu 2>/dev/null || nproc)" --target whisper-cli
    cp "$SRC/w/build/bin/whisper-cli" "$DIR/bin/"
    rm -rf "$SRC" "$DIR/.cmakevenv"
  fi
fi

# 2) the model (multilingual: do NOT use the *.en models, Indonesian needs the multilingual ones)
FILE="$DIR/ggml-$MODEL.bin"
if [ -s "$FILE" ]; then
  echo "model already at $FILE"
else
  curl -L --fail --progress-bar -o "$FILE.part" "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-$MODEL.bin"
  mv "$FILE.part" "$FILE"
fi
echo
echo "done. STT_PROVIDER=local (default) will use:"
echo "  binary: $( [ -x "$DIR/bin/whisper-cli" ] && echo "$DIR/bin/whisper-cli" || command -v whisper-cli )"
echo "  model:  $FILE   (set STT_MODEL_PATH to use another)"
