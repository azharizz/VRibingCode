# Virtual Loft speech server: faster-whisper behind the OpenAI transcription API,
# so the backend only needs STT_PROVIDER=openai + STT_BASE_URL. Best on an NVIDIA GPU.
import hmac
import os
import tempfile

from fastapi import FastAPI, File, Form, Header, HTTPException, UploadFile
from faster_whisper import WhisperModel

MODEL = os.environ.get("WHISPER_MODEL", "large-v3-turbo")
DEVICE = os.environ.get("WHISPER_DEVICE", "auto")        # cuda on a GPU host, cpu otherwise
COMPUTE = os.environ.get("WHISPER_COMPUTE", "default")   # float16 on GPU, int8 on CPU
API_KEY = os.environ.get("STT_API_KEY", "")
MAX_BYTES = 10 * 1024 * 1024

app = FastAPI()
model = WhisperModel(MODEL, device=DEVICE, compute_type=COMPUTE)


@app.get("/health")
def health():
    return {"ok": True, "model": MODEL}


@app.post("/v1/audio/transcriptions")
async def transcribe(
    file: UploadFile = File(...),
    model_name: str = Form("", alias="model"),
    language: str = Form(""),
    prompt: str = Form(""),
    authorization: str = Header(""),
):
    if API_KEY and not hmac.compare_digest(authorization, f"Bearer {API_KEY}"):
        raise HTTPException(401, "bad api key")
    audio = await file.read()
    if len(audio) > MAX_BYTES:
        raise HTTPException(413, "audio too large")
    with tempfile.NamedTemporaryFile(suffix=".wav") as f:
        f.write(audio)
        f.flush()
        segments, _ = model.transcribe(
            f.name, language=language or None, initial_prompt=prompt or None, vad_filter=True, beam_size=5
        )
        return {"text": " ".join(s.text.strip() for s in segments)}
