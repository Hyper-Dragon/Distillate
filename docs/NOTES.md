# Operator notes

The reference documentation is [SPEC.md](SPEC.md). This page is for when something
is wrong.

## Start here

```bash
curl -s http://localhost:8080/health
```

```json
{
  "status": "healthy",
  "version": "0.1.0",
  "build": "1a2b3c4",
  "mode": "rest",
  "concurrency": { "max": 1, "available": 0 }
}
```

That answers three questions: is it up, which build is it, and is it busy.
`available: 0` means one extraction is running. A request that arrives now gets a
`503`, not a hang.

## Troubleshooting

| Symptom | Likely cause | What to do |
|---|---|---|
| Container exits immediately | A configuration value it will not accept. The last log line names the variable and the values it takes. | Read the last line of the log. |
| `503` with `Retry-After` | At capacity. | Wait the stated seconds, or raise `CONCURRENCY` if the host has memory for it. |
| Every request hits `MAX_TIME_MS` | The page never settles: infinite scroll, a live ticker, a consent dialog that keeps re-rendering. | Lower `STABILIZATION_MS`, or send a per-request `timeoutMs`. |
| Works on one site, not another | Usually the site. Bot detection, a login wall, or content that only appears after interaction. | Try `userAgent`, `cookies` and `referer`. Load the page in a browser and check what it does. |
| Markdown is empty, `success: true` | The page has no article body the algorithm recognises. A forum thread, a listing, a product page. | Look at `plainText` and `includeSourceHtml`. Not every page is an article. |
| Images missing from markdown | The default policy strips them. `includeMarkdownNoImages` defaults to `true`. | Send `includeMarkdownNoImages: false` with `includeMarkdownLinkedImages: true`, or use `includeCleanHtmlEmbeddedImages`. |
| Logos and donate buttons in the output | Nothing filters images by default. | `excludeImageUrls: ["/logo", "/donate"]`. Matches are case-insensitive substrings of the resolved URL. |
| Memory grows on large pages | Base64 image embedding. `MAX_IMAGE_BYTES` caps the total, not each image. | Lower `MAX_IMAGE_BYTES`, set `GC_MODE=aggressive`, or lower `MAX_V8_HEAP_SIZE_MB`. |
| Browser fails to launch | `CHROMIUM_PATH` points at something that is missing or the wrong architecture. | Unset `CHROMIUM_PATH`. The service then uses Playwright's bundled browser. |
| A request stops halfway through a restart | The stop grace period is shorter than the extraction. | Set `stop_grace_period` above `MAX_TIME_MS`. The example compose file uses 30s. |
| The port answers but not with this service | Something else holds the port. On Windows, `wslrelay` binds `127.0.0.1:8080`. | Check the body of `/health`. If it is not this JSON, it is another service. |

## Checks that need a person

No test can cover these. Run them after a change to the extraction chain, and
after a Chromium or .NET base image upgrade.

| Check | Why it is manual |
|---|---|
| A JavaScript-heavy page returns its article | Depends on how a real site behaves today. A fixture would only encode a guess. |
| A page with a consent wall returns something usable | Needs a real banner with real scripts. |
| Images look right with `pngConversionRequired` | Size is easy to assert; "correct" is not. |
| A long article finishes in reasonable time on the target hardware | Depends on the machine, the network and the page. |
| Memory after 100 requests on a warm container | A 90-second test cannot see a slow leak. |
| Live mode against a real page: form, log, result | It is a UI. Look at it. |

To measure the long-run check:

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

Use a page you have the right to fetch in place of that URL.

## Reading the log

```bash
docker logs distillate 2>&1 | tail -50
```

The first line names the build:

```
Distillate 0.1.0+1a2b3c4 starting with Concurrency=1, MaxTimeMs=180000, ...
```

Each request has a trace id. It appears in the response as `traceId` and in the
log. Search for it rather than for a time; timestamps do not say which request
caused what.

## Changing configuration

Every variable is in `.env.example` and in the README table. Restart the container
after changing one. Nothing is reloaded at runtime, so a `MAX_TIME_MS` that
changes during an extraction cannot happen.

## Rolling back

Each release is tagged, and the image carries the same tag:

```yaml
image: ghcr.io/hyper-dragon/distillate:0.1.0
```

`/health` on the running container tells you which one you are on.
