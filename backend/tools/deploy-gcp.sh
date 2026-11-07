#!/bin/zsh
# Deploy the backend to Cloud Run. Secrets go from the repo-root .env straight into the
# service and are never printed. The headset token stays in the ignored .env.cloud.
#   PROJECT=<id> ACCOUNT=<email> zsh backend/tools/deploy-gcp.sh
set -euo pipefail
cd "$(dirname "$0")/.."
PROJECT=${PROJECT:?set PROJECT}; ACCOUNT=${ACCOUNT:?set ACCOUNT}
REGION=${REGION:-us-central1}; SERVICE=${SERVICE:-loft-backend}
SA=loft-runtime@$PROJECT.iam.gserviceaccount.com
for f in src/*.js; do node --check "$f"; done  # never ship a server that can't start
# Under load --test-force-exit can end the run early, so "no failures" alone proves nothing.
RUN=$(npm test 2>&1); PASSED=$(echo "$RUN" | awk '/^# pass/{print $3}'); FAILED=$(echo "$RUN" | awk '/^# fail/{print $3}')
[ "$FAILED" = 0 ] && [ "${PASSED:-0}" -ge 99 ] || { echo "tests: $PASSED passed, $FAILED failed. Not deploying."; exit 1; }
g() { gcloud --project "$PROJECT" --account "$ACCOUNT" --quiet "$@"; }

g services enable speech.googleapis.com
# Agents run shell commands: this identity must be able to reach nothing else in the project.
g iam service-accounts describe "$SA" >/dev/null 2>&1 || g iam service-accounts create loft-runtime --display-name "Virtual Loft backend (no project access)"

CLOUD=../.env.cloud
[ -f "$CLOUD" ] || { umask 077; echo "LOFT_TOKEN=$(openssl rand -hex 24)" > "$CLOUD"; }
VARS=$(mktemp); trap 'rm -f "$VARS"' EXIT
python3 - "$VARS" <<'PY'
import json, sys
def read(p):
    out = {}
    for line in open(p):
        line = line.strip()
        if line and not line.startswith("#") and "=" in line:
            k, v = line.split("=", 1)
            out[k.strip()] = v.strip().strip("'\"")
    return out
env, cloud = read("../.env"), read("../.env.cloud")
vars = {k: env[k] for k in ("AGENT_API_KEY", "AGENT_BASE_URL", "AGENT_MODEL")}
vars.update(LOFT_TOKEN=cloud["LOFT_TOKEN"], STT_PROVIDER="google", LOFT_NO_SANDBOX="1", LOFT_DAILY_TASKS="15", LOFT_SEED_SITE="1", LOFT_PUBLIC="1", LOFT_RESET_HOURS="6")
with open(sys.argv[1], "w") as f:
    for k, v in vars.items():
        f.write(f"{k}: {json.dumps(v)}\n")
PY

# Its own build account, limited to the image repo, its source bucket and logs: no existing account changes.
BUILD=loft-build@$PROJECT.iam.gserviceaccount.com
g iam service-accounts describe "$BUILD" >/dev/null 2>&1 || { g iam service-accounts create loft-build --display-name "Virtual Loft image build"; sleep 10; }
g artifacts repositories add-iam-policy-binding cloud-run-source-deploy --location "$REGION" --member "serviceAccount:$BUILD" --role roles/artifactregistry.writer >/dev/null
g storage buckets add-iam-policy-binding "gs://${PROJECT}_cloudbuild" --member "serviceAccount:$BUILD" --role roles/storage.objectAdmin >/dev/null
g storage buckets add-iam-policy-binding "gs://${PROJECT}_cloudbuild" --member "serviceAccount:$BUILD" --role roles/storage.legacyBucketReader >/dev/null
g projects add-iam-policy-binding "$PROJECT" --member "serviceAccount:$BUILD" --role roles/logging.logWriter --condition None >/dev/null
IMAGE=$REGION-docker.pkg.dev/$PROJECT/cloud-run-source-deploy/$SERVICE:$(date +%Y%m%d-%H%M%S)
g builds submit . --region "$REGION" --tag "$IMAGE" --gcs-log-dir "gs://${PROJECT}_cloudbuild/logs" --service-account "projects/$PROJECT/serviceAccounts/$BUILD"

g run deploy "$SERVICE" --image "$IMAGE" --region "$REGION" --service-account "$SA" \
  --execution-environment gen2 --min-instances 1 --max-instances 1 --no-cpu-throttling \
  --timeout 3600 --memory 4Gi --cpu 2 --allow-unauthenticated --env-vars-file "$VARS"
URL=$(g run services describe "$SERVICE" --region "$REGION" --format 'value(status.url)')
grep -q '^LOFT_URL=' "$CLOUD" && sed -i '' "s|^LOFT_URL=.*|LOFT_URL=$URL|" "$CLOUD" || echo "LOFT_URL=$URL" >> "$CLOUD"
echo "deployed: $URL"
