# Operator notes

What you need when it misbehaves. The reference documentation is
[SPEC.md](SPEC.md); this is the page you open at 11pm.

## First question, always

```bash
curl -s http://localhost:8080/health
```

That answers "is it up", "which build is it" and "is it busy" in one line:

```json
{
  "status": "healthy",
  "version": "0.1.0",
  "build": "1a2b3c4",
  "mode": "rest",
  "concurrency": { "max": 1, "available": 1 }
}
```

`available: 0` with a request that never returns is not a hang — one extraction is
in flight and the next caller will get a `503`. That distinction is the whole point
of the field.

## Troubleshooting

| Symptom | What it usually is | What to do |
|---|---|---|
| Container exits immediately, no HTTP at all | A configuration value the service refuses. The log's last line names the variable and the accepted values. | Read the last log line. It says what to change. |
| `503` with `Retry-After` | At capacity. Not an error — the service is telling the truth. | Wait the stated seconds, or raise `CONCURRENCY` if the machine can take it. |
| Every request times out at `MAX_TIME_MS` | The page never reaches a stable DOM — infinite scroll, a live ticker, a consent dialog that keeps re-rendering. | Lower `STABILIZATION_MS`; send a per-request `timeoutMs`. |
| Works for one site, not another | Almost always the site, not the service. Bot detection, a login wall, or content rendered only after interaction. | Try `userAgent`, `cookies`, `referer` on the request. Check the site in a normal browser with scripts enabled. |
| Markdown is empty, `success: true` | The page has no article body the reader algorithm recognises — a forum thread, a listing page, a product page. | Check `plainText` and `includeSourceHtml`. Not every page is an article. |
| Images missing from markdown | The image policy. `includeMarkdownNoImages` defaults to **true**. | Send `includeMarkdownNoImages: false` with `includeMarkdownLinkedImages: true`, or `includeCleanHtmlEmbeddedImages: true`. |
| Junk images (logos, donate buttons) in the output | Nothing filters them by default. | `excludeImageUrls: ["/logo", "/donate"]` — case-insensitive substrings of the resolved URL; matched images never appear in any output. |
| Memory climbs during large pages | Base64 image embedding is the usual cause; `MAX_IMAGE_BYTES` caps the total, not per image. | Lower `MAX_IMAGE_BYTES`, raise `GC_MODE=aggressive`, or cap `MAX_V8_HEAP_SIZE_MB`. |
| `Could not launch browser` / exec format error | `CHROMIUM_PATH` points at a file that is not there or is the wrong architecture. | Unset `CHROMIUM_PATH` and the service falls back to Playwright's bundled Chromium. |
| Container restarts take forever, or the log shows a request cut off | Docker's default 10-second stop timeout is shorter than a running extraction. | Set `stop_grace_period` above `MAX_TIME_MS` (the example compose file uses 30s). |
| Port answers, but not from this service | Something else holds the port. On Windows, `wslrelay` binds `127.0.0.1:8080`. | Probe `/health` and read the body. If it is not this JSON, it is not this service. |

## The things only a human can check

Nothing in CI can prove these, and pretending a mock covers them would be worse
than saying so. Run them after a change to the extraction chain, and after a
Chromium or .NET base image bump.

| Check | Why it cannot be automated |
|---|---|
| A JS-heavy page (infinite scroll, client-rendered article) extracts its body | Depends on a real site's current behaviour; a fixture would encode a guess about it. |
| A page behind a consent wall extracts nothing broken | Requires a real cookie banner with real scripts. |
| Images arrive at the right resolution and aspect ratio with `pngConversionRequired` | Trivially automatable for size, not for "does it look right". |
| A 20-minute article returns in a sensible time on the target hardware | The number depends on the machine, the network and the page. Measure it there. |
| Warm container, first request after `docker restart`, ~100 consecutive requests | Memory growth over a long run is the one thing a 90-second test cannot see. |
| Live mode against a real page: form, streaming log, result rendering | It is a UI. Look at it. |

How to do the long-run check, and how the number in the README was measured:

```bash
docker restart distillate
for i in $(seq 1 100); do
  curl -s -o /dev/null -w '%{http_code} %{time_total}\n' \
    -X POST http://localhost:8080/process \
    -H 'Content-Type: application/json' \
    -d '{"url":"https://example.com/article"}'
done
docker stats --no-stream distillate
```

`example.com` is a placeholder, not a recommendation — use a page you have the
right to fetch.

## Reading the log

```bash
docker logs distillate 2>&1 | tail -50
```

The first line names the version and the build:

```
Distillate 0.1.0+1a2b3c4 starting with Concurrency=1, MaxTimeMs=180000, ...
```

Every request carries a trace id, echoed in the response as `traceId` and in the
log. Grep for it rather than for a timestamp; timestamps lie about which request
caused what.

## Changing configuration

Every variable is documented in `.env.example` and in the README table. Restart the
container after changing one — nothing is hot-reloaded, deliberately: a
`MAX_TIME_MS` that changes under a running extraction is a bug waiting to be
reported.

## Reverting to a previous version

Every release is tagged and the image is tagged with the same version:

```bash
docker compose -f docker-compose.example.yml pull
# then pin the tag in the compose file, e.g.
#   image: ghcr.io/hyper-dragon/distillate:0.1.0
```

`/health` on the running container tells you which one you are actually on, which
is why the version is in the payload rather than only in a label.
