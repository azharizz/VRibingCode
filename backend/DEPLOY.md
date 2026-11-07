# Deploying the backend (cloud notes)

## What is live

Cloud Run service `loft-backend` (us-central1), deployed and tested with:

```bash
PROJECT=<id> ACCOUNT=<email> zsh backend/tools/deploy-gcp.sh
```

- **Keys** go from the repo-root `.env` into the service and are never printed. The
  URL and headset token stay in the ignored `.env.cloud`. Never commit that file.
- **`loft-runtime`** runs the service and holds no project roles, because agents run
  shell commands in a project that also hosts other services. Keep it that way.
- **`loft-build`** builds the image: its image repo, source bucket and logs only.
- **Speech** is `STT_PROVIDER=google`: the service account, no key, about 1.3 s a clip.
- **Public mode (`LOFT_PUBLIC=1`):** anyone with the APK is in one shared room with no login. Agents
  get no shell, folders can't be opened, workspaces can't be removed, and the browser can't reach
  private addresses. `POST /projects` also accepts `dev` and `setup` commands, so it must stay blocked.
- **`LOFT_RESET_HOURS=6`:** the server exits clean when idle after 6 hours, and Cloud Run starts a fresh room.
- **`LOFT_DAILY_TASKS=15`:** the counter lives in memory, so this is per room: 15 every 6 hours, about 60 a day.
  Also set a limit on the AI key.
- **Disk is in memory.** A restart resets to a fresh team and starter site, which is
  what a judged demo wants, but real work there is not kept. One instance is always
  on (`--min-instances 1`), so it costs money until you delete the service:
  `gcloud run services delete loft-backend --region us-central1`.

Mac app: `zsh backend/tools/use-cloud.sh on` points it at the cloud, `off` back at this machine.

The app drops a silent event stream after 40 s and reconnects, so it survives the 6-hour reset. A redeploy
is different: the old revision stays up for open connections and keeps pinging, so restart the app after one.

Quest build with the cloud baked in: put `{"url":...,"token":...}` in
`Assets/Resources/loft-server.json` of the build copy only, then run
`LoftQuestBuild.BuildApk`. The APK then carries the token, so keep it out of git.

The notes below were written before that deploy.

## What matters before picking a platform

- **Agents run shell commands** (`run_command`) and edit files. The container is
  the sandbox: give it only the secrets it needs, and nothing else worth stealing.
  Shell tools already drop `*KEY*/*TOKEN*/*SECRET*` env vars, but the LLM key is
  still in the Node process.
- **State is on disk**: projects, per-task git worktrees, roles, previews
  (`LOFT_DATA_DIR`, default `/data` in the image). Team state (agents, tasks) is
  in memory only. A serverless container with no volume loses worktrees on every
  restart, so tasks in review vanish. Mount a persistent volume on `/data`.
- **One instance only.** The agent roster and SSE clients live in one process.
  Set max instances to 1, and (on Cloud Run) min instances to 1 so a running
  task is not killed by scale-to-zero. CPU must stay allocated while idle
  (Cloud Run: "CPU always allocated"), because agents work between requests.
- **SSE (`GET /events`) is a long-lived connection.** Use the longest request
  timeout the platform allows (Cloud Run: 3600 s; the headset reconnects, and the
  server sends a snapshot first). Load balancers that buffer or idle-kill (ALB
  default 60 s) need a raised idle timeout; the server pings every 15 s.
- **`LOFT_TOKEN` and HTTPS are required.** The server refuses to listen on a
  non-loopback host without a token. Use a long random token
  (`openssl rand -hex 24`) and only expose it over HTTPS (Quest builds need
  `https://` for non-LAN hosts anyway). Do not put it in the image.
- **Voice**: use the managed STT provider, not local whisper. The image has no
  whisper binary or model. Set `STT_PROVIDER=openai` with `STT_BASE_URL`,
  `STT_API_KEY`, `STT_MODEL` (any OpenAI-compatible `/audio/transcriptions`).
  Audio goes up as raw WAV, max 10 MB. Nothing in the app changes, config only.
- **Preview screenshots** need Chromium: `docker build --build-arg PREVIEWS=1 .`
  (+400 MB, wants 1 GB+ RAM). Skip it if you do not need the PREVIEW screen.
- Give it enough memory: 1 GiB is the floor, 2 GiB if agents run builds or Chromium.

## Local container smoke test

```bash
cd backend
docker build -t loft-backend .
docker run --rm -p 4101:4101 -v loft-data:/data \
  -e LOFT_TOKEN=$(openssl rand -hex 24) \
  -e AGENT_API_KEY -e AGENT_BASE_URL -e AGENT_MODEL \
  -e STT_BASE_URL -e STT_API_KEY -e STT_MODEL loft-backend
```

## GCP Cloud Run

Cloud Run's disk is in-memory and ephemeral. Persist `/data` with a Cloud Storage
FUSE volume (works, but slow for git with many small files) or, better, run on
a small Compute Engine VM (see AWS EC2 below, same idea) if you rely on worktrees.

```bash
gcloud run deploy loft-backend --source . --region <region> \
  --execution-environment gen2 \
  --min-instances 1 --max-instances 1 --no-cpu-throttling \
  --timeout 3600 --memory 2Gi --cpu 2 \
  --allow-unauthenticated \
  --set-secrets AGENT_API_KEY=agent-api-key:latest,LOFT_TOKEN=loft-token:latest,STT_API_KEY=stt-api-key:latest \
  --set-env-vars AGENT_BASE_URL=...,AGENT_MODEL=...,STT_BASE_URL=...,STT_MODEL=whisper-1 \
  --add-volume name=data,type=cloud-storage,bucket=<bucket> \
  --add-volume-mount volume=data,mount-path=/data
```

`--allow-unauthenticated` only lifts Google's IAM gate: the app still demands
`LOFT_TOKEN`. Cloud Run terminates HTTPS for you.

## AWS

Best fit is a single small **EC2** instance (or Lightsail) running the container
with a local disk, because of the disk and single-instance needs:

1. `t3.small` or larger, 20 GB gp3, Docker installed.
2. `docker run -d --restart=always -p 4101:4101 -v /var/loft:/data --env-file loft.env <image>`
   with `loft.env` holding `LOFT_TOKEN`, `AGENT_*`, `STT_*` (file mode 600, or fetch from SSM Parameter Store).
3. HTTPS: put Caddy/nginx in front, or an ALB with an ACM certificate and the
   idle timeout raised to 3600 s. Security group: 443 only.

ECS Fargate also works: one task, EFS mounted on `/data`, ALB idle timeout 3600 s.
EFS is slow for git; if tasks feel sluggish, prefer EC2 with EBS.

## Quest on a LAN Mac (no cloud)

```bash
HOST=0.0.0.0 LOFT_TOKEN=$(openssl rand -hex 24) npm run serve
```

The Quest reaches `http://<mac-lan-ip>:4101` over Wi-Fi. Same network, firewall
allowing Node. This is plain HTTP on your LAN: fine at home, never on a public
network. Local whisper keeps working here (`STT_PROVIDER=local`).

## Speech-to-text in the cloud

`stt-server/` is faster-whisper behind the OpenAI transcription API. Run it next to the backend, then set
`STT_PROVIDER=openai`, `STT_BASE_URL=http://<host>:8000/v1`, `STT_API_KEY=<same as the server>`, `STT_MODEL=large-v3-turbo`.

Use a GPU host. Measured on a Mac in Docker with CPU only: about 20 s per short clip, too slow for voice.
For a GPU, change the Dockerfile base to an `nvidia/cuda` runtime image, then set `WHISPER_DEVICE=cuda` and
`WHISPER_COMPUTE=float16`. GPU latency has not been measured yet. A hosted speech API needs no server at all.

## Comparing speech models

`STT_SAVE_SAMPLES=1` keeps recordings in `data/stt/samples` (local only, off by default). Correct each `.txt`, then run
`node tools/stt-compare.js`. Add `--url http://host:8000/v1` to include a server.

Synthetic clips, 2026-11-06, M2 Mac: whisper.cpp `small` 7.6% word error at 1.1 s, `large-v3-turbo` 9.2% at 3.8 s.
Real voices may rank differently.

## New endpoints

Boards `GET/PUT /boards?scene=`, notes `/notes`, files `/projects/:id/tree` and `/file`, `GET /tasks/:id/diff`,
`POST /agents/:id/chat`, `/suggestions`, and clickable previews `/preview/sessions`. All need `LOFT_TOKEN` when set.
Clickable previews need Playwright: build with `--build-arg PREVIEWS=1`.

## 360 video

`POST /cinema {url}` downloads a public video, converts it to H.264 with ffmpeg, and serves it at `/cinema/<id>/video.mp4`. It needs `ffmpeg` (in the Docker image) and disk space: 1.5 GB limit per download, last 3 videos kept in `data/cinema`. Those video URLs skip the token, because a video player cannot send one. The long random id is the permission.

Only real video files work. YouTube and similar sites stream protected data with no file, so they stay on the flat Browser board.

## Qwen3-ASR on a Mac

`tools/setup-qwen.sh` once, then `tools/start-qwen.sh` and `npm run serve:qwen`. It runs on the Apple GPU through MLX, audio stays on the machine, and it listens on loopback only. It needs about 3 GB of disk.

On the first real recordings it clearly beat whisper.cpp `small`, at about 0.7 s per clip. For other hosts use `stt-server/app.py` or a hosted speech API.
