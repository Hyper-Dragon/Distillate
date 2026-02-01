# Distillate

[![ci](https://github.com/Hyper-Dragon/Distillate/actions/workflows/ci.yml/badge.svg)](https://github.com/Hyper-Dragon/Distillate/actions/workflows/ci.yml)
[![release](https://github.com/Hyper-Dragon/Distillate/actions/workflows/release.yml/badge.svg)](https://github.com/Hyper-Dragon/Distillate/actions/workflows/release.yml)
[![image](https://img.shields.io/badge/ghcr.io-hyper--dragon%2Fdistillate-2496ed?logo=docker&logoColor=white)](https://github.com/Hyper-Dragon/Distillate/pkgs/container/distillate)
[![licence](https://img.shields.io/badge/licence-MIT-3da639)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512bd4?logo=dotnet&logoColor=white)](Directory.Build.props)

## 'Reader view as a service, with an honest answer when it is busy.'

Paste a URL, get back the article: metadata, plain text, markdown, images, links.
The extraction is done by a real browser running the same readability algorithm
Firefox uses, so a page that renders its body with JavaScript is not a special case.

```
  your script ──┐
  a note app  ──┼──► POST /process ──► distillate ──► Chromium ──► the page
  curl        ──┘                          │
                                           └──► markdown, text, images, links
```

**This is a service, not a library, and not a crawler.** It does one page at a time,
synchronously, and it will tell you when it cannot: at capacity it answers `503`
with a `Retry-After` header and a body naming the reason, rather than accepting work
it cannot do. There is no queue to hide behind.

It has **no authentication** and it fetches whatever URL it is given — that is the
job. Read [Where does it run](#where-does-it-run) before exposing the port.

## About

Distillate is a self-hosted web service that turns a URL into structured
reader-view content. You give it a URL and a list of the outputs you want; it
returns JSON with the article's metadata, the body as markdown, plain text or
cleaned HTML, and — if you asked — every image, link and asset it found.

What you get:

- **One request, every output.** Markdown with images embedded as data URIs, or
  linked to local filenames, or stripped; plain text; cleaned HTML; source HTML;
  the inventory of images, links and assets; debug information. Ask for what you
  want in the request body and nothing else is computed.
- **Images handled properly.** A global byte budget, optional PNG conversion with
  sane downscaling, and `excludeImageUrls` to drop site logos and donate buttons
  *before* they are numbered or downloaded, so they appear in no output and take no
  slot.
- **Honest backpressure.** Capacity is enforced at request time. A refusal is a
  `503` with `Retry-After` and a reason, which is a different thing from a timeout
  and tells a caller which one happened.
- **A live mode** (`MODE=live`) with a browser UI and a streaming log, for when a
  URL is easier to paste than to script.
- **No telemetry, no cloud, no account.** It talks to the page you named and
  nowhere else.

It works on the pages it has been tried on. Sites change, bot detection changes, and
the reader algorithm has opinions about what an article is. See
[Known Issues](#known-issues).

## Where does it run?

**Anywhere that can run a container, with enough memory for a browser.** One
extraction at a time needs roughly:

- **~1 GB RAM** for the container, of which the browser is most of it
- **1 CPU core** to be comfortable; it will run on less, slower
- No disk to speak of, and no persistent state — restarting loses nothing

A Raspberry Pi 4 or 5 with 8 GB is the shape of machine this was built for: the
example compose file carries a commented block for it (`GC_MODE=aggressive`, a
capped V8 heap, no `MAX_TIME_MS` change). No benchmark is published here — that
tuning is a starting point, not a measurement.

The one thing that matters is **what it can reach**. It runs on your network and
fetches URLs *from there*, so a page only available inside your LAN is fair game,
and so is anything else on that network. Bind it to loopback (the example does), or
put an authenticated proxy in front of it.

## Quick start

```bash
docker run -d \
  --name distillate \
  --restart unless-stopped \
  --stop-timeout 30 \
  -p 127.0.0.1:8080:8080 \
  ghcr.io/hyper-dragon/distillate:latest
```

```bash
curl -s http://127.0.0.1:8080/health
```

```json
{ "status": "healthy", "version": "0.1.0", "build": "1a2b3c4", "mode": "rest", ... }
```

Extract something:

```bash
curl -s -X POST http://127.0.0.1:8080/process \
  -H 'Content-Type: application/json' \
  -d '{"url": "https://example.com/article"}' | jq -r .markdownNoImages
```

That is a placeholder URL, not a recommendation — point it at a page you have the
right to fetch.

### With a compose file

```bash
cp .env.example .env
docker compose -f docker-compose.example.yml up -d
```

Every value in `.env.example` is already the default, so an empty `.env` is a valid
deployment. The compose file marks the two lines that are load-bearing: the port
published on loopback, and `stop_grace_period`, which must exceed `MAX_TIME_MS` or
a restart kills an extraction in flight.

### From source

Requires the .NET 10 SDK and a Chromium. See [docs/BUILDING.md](docs/BUILDING.md).

```bash
dotnet run --project src/Distillate.Web
```


## Modes

Distillate runs in one of two mutually exclusive modes, controlled by the `MODE` environment variable:

### REST Mode (default)

The default mode. Exposes a JSON API for programmatic access.

- `GET /` — Service info (JSON)
- `GET /health` — Health check (JSON)
- `POST /process` — Process a URL and return structured JSON

### Live Mode

An interactive dark-themed web UI for browser-based usage. Set `MODE=live` to enable.

```bash
# Running locally
MODE=live dotnet run --project src/Distillate.Web

# Running with Docker
docker run -e MODE=live -p 127.0.0.1:8080:8080 ghcr.io/hyper-dragon/distillate:latest
```

- `GET /` — Two-column web page: URL input form with advanced options on the left, curl command builder (all REST API fields) on the right, and a real-time console log panel below
- `GET /health` — Health check (JSON)
- `GET /live?url=<encoded-url>` — Directly extract and display reader view for a URL (form submissions also use this pattern)
- `GET /live/events?url=<encoded-url>` — Server-Sent Events stream for real-time processing log output

The curl builder includes controls for **all** `ProcessRequest` fields and generates `jq` commands to save multiple outputs to separate files. Form settings are persisted to `localStorage` so they are restored when navigating back from a result page.

Users can activate the live view in two ways:
1. **Form**: Navigate to `http://localhost:8080/` and enter a URL in the input box
2. **Direct link or bookmark**: Navigate to `http://localhost:8080/live?url=https://example.com/article`

**The two modes are mutually exclusive.** REST endpoints (`POST /process`) are not available in live mode, and live endpoints (`GET /` HTML page, `GET /live`) are not available in REST mode.

An unknown `MODE` stops the process at startup and names the values it accepts;
it does not silently run the other mode.

## Configuration

All configuration is environment variables. The defaults below are the values the
code uses when a variable is absent — they live in `DistillateOptions` and in
`Program.cs`, and `.env.example` is a copy of this table as a file.

| Variable | Default | Description |
| -------- | ------- | ----------- |
| `MODE` | `rest` | `rest` (JSON API) or `live` (web UI). Mutually exclusive; anything else is a startup error. |
| `CONCURRENCY` | `1` | Maximum simultaneous extractions. There is no internal queue. |
| `BUSY_RETRY_AFTER_SECONDS` | `15` | `Retry-After` value sent with a capacity refusal. |
| `MAX_TIME_MS` | `180000` | Hard ceiling for one request, milliseconds. Keep the container's stop grace period above it. |
| `MAX_IMAGE_BYTES` | `209715200` | Global budget for images embedded as data URIs (200 MB). |
| `GC_MODE` | `normal` | `normal`, or `aggressive` to force a compacting gen2 collection after each request. |
| `STABILIZATION_MS` | `10000` | How long to let the DOM settle after load before extracting. |
| `SCROLL_DELAY_MS` | `100` | Delay between scroll steps while lazy-loading images. |
| `MAX_V8_HEAP_SIZE_MB` | `512` | V8 heap ceiling for the browser. Lower it on memory-constrained hosts. |
| `CHROMIUM_PATH` | `/usr/bin/chromium` | Chromium binary. If the path does not resolve, Playwright's bundled browser is used instead. |
| `CHROMIUM_ARGS` | *(none)* | Extra Chromium command-line arguments, space separated. |
| `PORT` | `8080` | HTTP listen port. `ASPNETCORE_URLS` overrides it if set. |
| `MAX_URL_LENGTH` | `2048` | Longest accepted URL, characters. |
| `USER_AGENT` | Chrome 120 UA | Default User-Agent for page fetches and image downloads. |
| `LOG_LEVEL` | `Information` | `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`. Anything else is a startup error. |

See [docs/SPEC.md](docs/SPEC.md) for the full specification, and
[docs/NOTES.md](docs/NOTES.md) for what to do when something misbehaves.

## API

### POST /process

Process a URL and extract reader view content.

**Request fields (all optional except `url`):**

| Field | Default | Description |
| ----- | ------- | ----------- |
| `url` | required | URL to process (http/https only) |
| `engine` | `"smartreader"` | Reader engine (only `"smartreader"` is supported) |
| `timeoutMs` | server max | Per-request timeout override (ms) |
| `stabilizationMs` | server default | DOM stabilization delay override (ms) |
| `userAgent` | server default | User-Agent override |
| `referer` | `null` | HTTP Referer header override |
| `cookies` | `null` | Cookies to inject before navigation |
| `clearCookies` | `false` | Disable cookie storage for this request |
| `includeSourceHtml` | `false` | Raw browser HTML before reader extraction |
| `includeCleanHtmlEmbeddedImages` | `false` | Reader HTML with images as base64 data URIs |
| `includeCleanHtmlLinkedImages` | `false` | Reader HTML with images as local filenames |
| `includeCleanHtmlNoImages` | `false` | Reader HTML with all `<img>` elements stripped |
| `includeMarkdownLinkedImages` | `false` | Markdown with image references as local filenames |
| `includeMarkdownNoImages` | `true` | Markdown with image references stripped |
| `includePlainText` | `true` | Plain text output |
| `includeAllLinksInPage` | `false` | Deduplicated `<a href>` URLs from source HTML |
| `includeAllLinksMedia` | `false` | Deduplicated `<img>`/`<video>` URLs from source HTML |
| `includeAllLinksAssets` | `false` | Deduplicated CSS/JS asset URLs from source HTML |
| `includeAllInPageImages` | `false` | Images present in the extracted article HTML |
| `includeAllOutOfPageImages` | `false` | Images in source HTML but absent from article |
| `imageLinkRewrite` | `false` | Rewrite `src` attributes to local filenames (implies `includeAllInPageImages`) |
| `pngConversionRequired` | `false` | Convert all fetched images to PNG via SkiaSharp. Portrait images (height > width) are capped at 1080 px height; landscape images at 720 px height. Aspect ratio is preserved |
| `excludeImageUrls` | `null` | Case-insensitive substrings matched against each image's resolved absolute URL. Matches are removed from the document (and the out-of-page inventory) before numbering, so they appear in no output, take no `image-NNNN` slot and are never downloaded. Omit or send `[]` for unchanged behaviour |
| `unicodeNormalization` | `"preserve"` | Unicode punctuation handling: `"preserve"` (default, keeps smart quotes/dashes), `"ascii"` (converts to plain ASCII), or `"latex"` (LaTeX-idiomatic sequences) |

**Example request:**

```bash
curl -X POST http://localhost:8080/process \
  -H "Content-Type: application/json" \
  -d '{"url": "https://example.com/article"}'
```

**Example response:**

```json
{
  "success": true,
  "requestedUrl": "https://example.com/article",
  "processedAt": "2024-01-15T10:30:00Z",
  "processingTimeMs": 1523.5,
  "title": "Article Title",
  "byline": "By Jane Doe",
  "author": "Jane Doe",
  "excerpt": "A brief summary of the article.",
  "siteName": "Example News",
  "language": "en",
  "publicationDate": "2024-01-15T00:00:00",
  "featuredImage": "https://example.com/hero.jpg",
  "timeToRead": "00:05:00",
  "plainText": "Plain text content...",
  "markdownNoImages": "# Article Title\n\nContent...",
  "completionFlags": 0,
  "traceId": "abc123"
}
```

### GET /health

Returns service health and current capacity.

## Known Issues

The honest list. Each of these is a property of the current design or a limit that
has not been solved, not a bug waiting for a report.

- **No authentication.** Every endpoint is open to whoever can reach the port, and
  the service will fetch any URL it is given — including addresses on your own
  network. That is what a URL fetcher does. Publish the port on loopback, or put an
  authenticated proxy in front of it. [SECURITY.md](SECURITY.md) states this as
  out-of-scope-by-design so the conversation starts in the right place.
- **Live mode writes fetched HTML into the page it serves.** Browsing to
  `/live?url=...` and pointing it at a hostile page is a way to run that page's
  script on your own origin. Fine for a single-operator tool on loopback; do not run
  live mode as a shared, internet-facing service.
- **One extraction at a time by default.** Raising `CONCURRENCY` gives you more, and
  costs memory proportionally: each extraction is a browser context.
- **One mode per process.** `rest` and `live` are mutually exclusive; a single
  deployment cannot serve the API and the UI on different paths.
- **No interaction.** No clicking "accept cookies", no login forms, no scrolling
  done by hand. Cookies can be injected and a Referer/User-Agent set, which covers
  most of it, not all of it.
- **The reader algorithm has opinions.** A forum thread, a product page or a
  landing page may extract to very little. `success: true` means "the pipeline ran",
  not "this page was an article" — check the body.
- **`arm64` images are built on GitHub's native arm64 runners**, which are free only
  for public repositories. While this repository is private, releases publish
  `linux/amd64` only; re-running the release workflow on the same tag afterwards
  adds `linux/arm64` to the same manifest.
- **`LoggerMessage` analyser rules are suppressed, not fixed.** See
  [CHANGELOG.md](CHANGELOG.md) under `Unreleased`.

## Testing

```bash
dotnet test Distillate.sln -c Release
```

249 tests. The integration tests serve their fixtures from a loopback listener
rather than a third-party site, so they do not fail because a page changed. What no
test can cover — a real site's bot detection, a consent wall, memory over a long
run — is a written manual checklist in [docs/NOTES.md](docs/NOTES.md).

## Contributing

Issues and pull requests are welcome. [CONTRIBUTING.md](CONTRIBUTING.md) says how to
build it, what the gates are and what a good change looks like;
[CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) applies to all project spaces. Security
reports go through [SECURITY.md](SECURITY.md), not the issue tracker.

## Dependencies

Everything is a NuGet package or a container base image, and Dependabot watches
both. The load-bearing ones:

| Package | Why it is here |
|---|---|
| [SmartReader](https://github.com/m-dehghani/SmartReader) | The reader algorithm — a .NET port of Readability.js. |
| [Microsoft.Playwright](https://playwright.dev/dotnet/) | Drives Chromium. Pinned; a major bump changes the browser. |
| [AngleSharp](https://anglesharp.github.io/) | HTML parsing and DOM. Pinned to the smallest patched version — it parses untrusted HTML. |
| [ReverseMarkdown](https://github.com/mysticmind/reversemarkdown-net) | HTML to markdown. |
| [SkiaSharp](https://github.com/mono/SkiaSharp) | Optional PNG conversion and downscaling. |

## Version history

[CHANGELOG.md](CHANGELOG.md). The running version is in `GET /health`, together
with the commit it was built from.

## Licence

MIT — see [LICENSE](LICENSE).
