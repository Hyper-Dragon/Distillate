# Distillate — Docker Build & Run Guide

Instructions for AI agents to build, run, and verify the Distillate container.

## Prerequisites

- Docker running in WSL (not Docker Desktop)
- The `docker-buildx` plugin may not be installed; use `Dockerfile.test` which strips the `--platform=$BUILDPLATFORM` directive for compatibility with the legacy builder

## Quick Reference

| Item | Value |
|---|---|
| Production Dockerfile | `Dockerfile` (requires `docker buildx`) |
| Test/Dev Dockerfile | `Dockerfile.test` (works without buildx) |
| Internal port | `8080` |
| Health endpoint | `GET /health` |
| Process endpoint | `POST /process` |
| Info/root endpoint | `GET /` |
| Image size | ~1.45 GB |

## Build

From the repo root (`/mnt/e/HyperDragon-Repos/Distillate` in WSL):

```bash
# Using Dockerfile.test (no buildx required)
docker build -f Dockerfile.test -t distillate-test:latest .

# Using production Dockerfile (requires docker buildx)
docker buildx build --platform linux/amd64 -t distillate-test:latest .
```

The build installs .NET 10.0 SDK, restores packages, installs Playwright + Chromium, publishes the app, then creates a runtime image with Xvfb and system Chromium. Expect a long initial build.

## Run

Pick any free host port and map it to container port `8080`. Do **not** use a port already in use by other containers — check with `docker ps` first.

```bash
docker run -d \
  --name distillate-test \
  -p <HOST_PORT>:8080 \
  distillate-test:latest
```

Example with port 8888:

```bash
docker run -d --name distillate-test -p 8888:8080 distillate-test:latest
```

## Environment Variables

All configuration is via environment variables. Override defaults with `-e` flags on `docker run`:

| Variable | Default | Description |
|---|---|---|
| `CONCURRENCY` | `1` | Max concurrent processing requests (503 backpressure when full) |
| `MAX_TIME_MS` | `180000` | Max processing time per request (ms) |
| `MAX_IMAGE_BYTES` | `209715200` | Max total image bytes per request (~200 MB) |
| `GC_MODE` | `normal` | GC mode (`normal` or `aggressive`) |
| `STABILIZATION_MS` | `10000` | Page stabilization wait time (ms) |
| `LOG_LEVEL` | `Information` | Logging level |
| `PORT` | `8080` | Internal listen port (rarely needs changing) |
| `CHROMIUM_PATH` | `/usr/bin/chromium-browser` | Path to Chromium binary in container |
| `MAX_URL_LENGTH` | `2048` | Max URL length accepted |
| `USER_AGENT` | Chrome 120 UA string | User-Agent header for page fetching |
| `SCROLL_DELAY_MS` | `100` | Delay between scroll steps (ms) |
| `MAX_V8_HEAP_SIZE_MB` | `512` | V8 heap size limit for Chromium |

## Verify

```bash
# Check container is running
docker ps --filter name=distillate-test

# Check health
curl http://localhost:<HOST_PORT>/health
# Expected: {"status":"healthy","timestamp":"...","concurrency":{"max":1,"available":1}}

# Check service info
curl http://localhost:<HOST_PORT>/

# Check logs
docker logs distillate-test
```

Startup is healthy when the logs show:
```
Distillate listening on port 8080
Now listening on: http://0.0.0.0:8080
Application started.
```

## Test a Request

```bash
curl -X POST http://localhost:<HOST_PORT>/process \
  -H "Content-Type: application/json" \
  -d '{
    "url": "https://example.com",
    "engine": "readability",
    "includeMarkdownNoImages": true,
    "includePlainText": true
  }'
```

## Stop & Clean Up

```bash
docker stop distillate-test
docker rm distillate-test
# Optionally remove the image:
docker rmi distillate-test:latest
```

## WSL Notes

- Docker commands must run inside WSL: `wsl bash -c "docker ..."`
- PowerShell terminal output from WSL can be garbled — write output to a file on the Windows mount and read it back: `> /mnt/e/HyperDragon-Repos/Distillate/.output.txt`
- The `docker-compose.yml` and `docker-compose.debug.yml` are reference examples; prefer direct `docker build` + `docker run` commands for testing
- Check for port conflicts before binding: `docker ps` to see what's already mapped
