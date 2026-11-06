# Virtual Loft backend. Build from backend/:  docker build -t loft-backend .
# Run: see DEPLOY.md. Needs LOFT_TOKEN, AGENT_* and a volume on /data (agents keep state on disk).
FROM node:22-slim

# git: every task runs in a git worktree. Agents run shell commands, so add what your projects need (python3, build-essential...).
RUN apt-get update && apt-get install -y --no-install-recommends git ca-certificates && rm -rf /var/lib/apt/lists/*

# Optional preview screenshots (adds ~400 MB): docker build --build-arg PREVIEWS=1 .
ARG PREVIEWS=0
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --omit=dev
RUN if [ "$PREVIEWS" = "1" ]; then npx playwright install --with-deps chromium; fi
COPY src ./src
COPY tools ./tools

ENV NODE_ENV=production \
    HOST=0.0.0.0 \
    LOFT_DATA_DIR=/data \
    WORKSPACE_DIR=/data/workspace \
    PROJECTS_DIR=/data/projects \
    STT_PROVIDER=openai
RUN mkdir -p /data && chown -R node:node /data /app
USER node
RUN git config --global --add safe.directory '*'
VOLUME /data
EXPOSE 4101
# PORT is set by Cloud Run; locally it defaults to 4101.
CMD ["node", "src/server.js"]
