# Virtual Loft speech server for Apple Silicon: Qwen3-ASR on MLX behind the OpenAI transcription
# API, so the backend needs only STT_PROVIDER=openai. Audio stays on this Mac. Listens on loopback.
#   backend/tools/setup-qwen.sh, then backend/tools/start-qwen.sh
import os
import re
import tempfile
import threading
import time

from fastapi import FastAPI, File, Form, HTTPException, UploadFile
from mlx_audio.stt.utils import load

MODEL = os.environ.get("QWEN_MODEL", "mlx-community/Qwen3-ASR-1.7B-8bit")
MAX_BYTES = 10 * 1024 * 1024

app = FastAPI()
model = load(MODEL)
lock = threading.Lock()  # one GPU job at a time


def run(path, prompt):
    kw = {"system_prompt": prompt} if prompt else {}
    with lock:
        out = model.generate(path, **kw)
    return str(getattr(out, "text", out)).strip()


@app.get("/health")
def health():
    return {"ok": True, "model": MODEL}


@app.post("/v1/audio/transcriptions")
async def transcribe(file: UploadFile = File(...), prompt: str = Form("")):
    audio = await file.read()
    if len(audio) > MAX_BYTES:
        raise HTTPException(413, "audio too large")
    with tempfile.NamedTemporaryFile(suffix=".wav") as f:
        f.write(audio)
        f.flush()
        t0 = time.time()
        text = run(f.name, prompt)
    # the model can prefix its language tag
    text = re.sub(r"^language\s+\w+\s*<asr_text>", "", text, flags=re.I).replace("<asr_text>", "").strip()
    return {"text": text, "seconds": round(time.time() - t0, 3)}
