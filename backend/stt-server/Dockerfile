# CPU image for testing. For a GPU host use an nvidia/cuda runtime base and WHISPER_DEVICE=cuda, WHISPER_COMPUTE=float16.
FROM python:3.12-slim
WORKDIR /app
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
COPY app.py .
ENV WHISPER_MODEL=large-v3-turbo WHISPER_DEVICE=cpu WHISPER_COMPUTE=int8 HF_HOME=/models
RUN useradd -m stt && mkdir /models && chown stt /models
USER stt
VOLUME /models
EXPOSE 8000
CMD ["uvicorn", "app:app", "--host", "0.0.0.0", "--port", "8000"]
