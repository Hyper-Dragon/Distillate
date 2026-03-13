#!/bin/bash
set -eo pipefail

LOG=/tmp/distillate-build.log
echo "[$(date)] Checking for docker buildx..." | tee "$LOG"

# Try to install buildx plugin if missing
if ! docker buildx version > /dev/null 2>&1; then
  echo "[$(date)] Installing docker-buildx-plugin via apt..." | tee -a "$LOG"
  apt-get update -qq 2>&1 | tail -1 | tee -a "$LOG"
  apt-get install -y docker-buildx-plugin 2>&1 | tail -5 | tee -a "$LOG" || {
    echo "[$(date)] apt install failed - building without BuildKit (fallback)" | tee -a "$LOG"
    USE_FALLBACK=1
  }
else
  echo "[$(date)] docker buildx already available" | tee -a "$LOG"
fi

echo "[$(date)] Starting Docker build..." | tee -a "$LOG"
cd /mnt/e/HyperDragon-Repos/Distillate

if [ "${USE_FALLBACK}" = "1" ]; then
  DOCKER_BUILDKIT=0 docker build \
    --build-arg BUILDPLATFORM=linux/amd64 \
    --build-arg TARGETARCH=amd64 \
    -t distillate-test:latest . 2>&1 | tee -a "$LOG"
  BUILD_EXIT=${PIPESTATUS[0]}
else
  DOCKER_BUILDKIT=1 docker build \
    --build-arg TARGETARCH=amd64 \
    --platform linux/amd64 \
    -t distillate-test:latest . 2>&1 | tee -a "$LOG"
  BUILD_EXIT=${PIPESTATUS[0]}
fi

echo "[$(date)] Build exit: $BUILD_EXIT" | tee -a "$LOG"
if [ "$BUILD_EXIT" != "0" ]; then
  echo "[$(date)] BUILD FAILED" | tee -a "$LOG"
  exit 1
fi

echo "[$(date)] Build succeeded. Starting container..." | tee -a "$LOG"
docker rm -f distillate-test 2>/dev/null || true

docker run -d \
  --name distillate-test \
  -p 0.0.0.0:8888:8080 \
  -e CONCURRENCY=2 \
  -e MAX_TIME_MS=180000 \
  -e MAX_IMAGE_BYTES=209715200 \
  -e GC_MODE=normal \
  -e STABILIZATION_MS=10000 \
  -e LOG_LEVEL=Information \
  -e PORT=8080 \
  -e CHROMIUM_PATH=/usr/bin/chromium \
  distillate-test:latest 2>&1 | tee -a "$LOG"

echo "[$(date)] Container started. Waiting for health..." | tee -a "$LOG"
for i in $(seq 1 24); do
  STATUS=$(docker inspect --format='{{.State.Health.Status}}' distillate-test 2>/dev/null || echo "unknown")
  echo "[$(date)] Health $i/24: $STATUS" | tee -a "$LOG"
  if [ "$STATUS" = "healthy" ]; then
    echo "[$(date)] READY on port 8888" | tee -a "$LOG"
    break
  fi
  sleep 5
done
