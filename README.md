# Distillate

[![ci](https://github.com/Hyper-Dragon/Distillate/actions/workflows/ci.yml/badge.svg)](https://github.com/Hyper-Dragon/Distillate/actions/workflows/ci.yml)
[![release](https://github.com/Hyper-Dragon/Distillate/actions/workflows/release.yml/badge.svg)](https://github.com/Hyper-Dragon/Distillate/actions/workflows/release.yml)
[![image](https://img.shields.io/badge/ghcr.io-hyper--dragon%2Fdistillate-2496ed?logo=docker&logoColor=white)](https://github.com/Hyper-Dragon/Distillate/pkgs/container/distillate)
[![licence](https://img.shields.io/badge/licence-MIT-3da639)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512bd4?logo=dotnet&logoColor=white)](Directory.Build.props)

A self-hosted HTTP service that turns a web page into clean article content:
title, author, date, markdown, plain text, images and links.

```
  your script ──┐
  a note app  ──┼──► POST /process ──► distillate ──► Chromium ──► the page
  curl        ──┘                          │
                                           └──► markdown, text, images, links
```

Send it a URL. It loads the page in Chromium, pulls out the article, and returns
JSON. The request says which outputs you want; anything you leave out is not
produced.

It processes one page at a time. If a request arrives while it is busy, it answers
`503` with a `Retry-After` header instead of queueing the work.

There is no authentication, and it fetches any URL it is given, including
addresses on your own network. Read [Where it runs](#where-it-runs) before
exposing the port.

## What it does

- **Article extraction.** Chromium loads the page and scrolls it to trigger lazy
  images. SmartReader, a .NET port of Readability.js, then picks out the article
  body. Pages that build their content with JavaScript work because a real browser
  rendered them first.
- **Metadata.** Title, byline, author, excerpt, site name, language, publication
  date, featured image and reading time.
- **Outputs you choose.** Markdown with three image policies, plain text, cleaned
  HTML, source HTML, and inventories of images, links and assets. Each has a flag
  in the request, and only the ones you ask for are computed.
- **Images.** Optional PNG conversion with downscaling, a global byte budget for
  embedded images, and `excludeImageUrls` to drop logos and donate buttons before
  they are numbered or downloaded.
- **Live mode.** A browser UI with a streaming log, for when you would rather
  paste a URL than write a request. Set `MODE=live`.

It talks to the page you name and nothing else. No telemetry, no account.

Extraction quality depends on the page. The reader algorithm expects an article, so
a forum thread or a product listing may come back nearly empty. See
[Known issues](#known-issues).

## Where it runs

Any container host with enough memory for a browser. For one request at a time:

- about 1 GB of RAM, most of it Chromium
- one CPU core
- no disk and no state; restarting loses nothing

A Raspberry Pi 4 or 5 with 8 GB works. The example compose file has a commented
block for it: `GC_MODE=aggressive` and a smaller V8 heap. Those are starting
values, not measured results.

What it can reach matters more than where it runs. It fetches URLs from the
machine's network, so anything reachable from there is reachable through the
service. Bind the port to loopback (the example does) or put an authenticated
proxy in front.

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

Extract a page:

```bash
curl -s -X POST http://127.0.0.1:8080/process \
  -H 'Content-Type: application/json' \
  -d '{"url": "https://example.com/article"}' | jq -r .markdownNoImages
```

Use a page you have the right to fetch in place of `example.com`.

### With a compose file

```bash
cp .env.example .env
docker compose -f docker-compose.example.yml up -d
```

Every value in `.env.example` is already the default, so an empty `.env` deploys
fine. Two lines in that file matter: the port is published on loopback, and
`stop_grace_period` must be longer than `MAX_TIME_MS`, or a restart kills a request
that is still running.

### From source

Needs the .NET 10 SDK and a Chromium. See [docs/BUILDING.md](docs/BUILDING.md).

```bash
dotnet run --project src/Distillate.Web
```

## Modes

The `MODE` variable picks one of two modes. They are mutually exclusive.

### REST (default)

The JSON API.

- `GET /` — service info
- `GET /health` — health, version, free capacity
- `POST /process` — extract a URL

### Live

A web UI for browser use.

```bash
MODE=live dotnet run --project src/Distillate.Web
docker run -e MODE=live -p 127.0.0.1:8080:8080 ghcr.io/hyper-dragon/distillate:latest
```

- `GET /` — form with advanced options on the left, a curl builder for every
  request field on the right, and a live log below
- `GET /health` — health, version, free capacity
- `GET /live?url=<encoded-url>` — extract and display a page
- `GET /live/events?url=<encoded-url>` — server-sent events with the processing log

The curl builder writes `jq` commands that save each output to its own file. Form
settings are kept in `localStorage`, so going back to the form restores them.

Open the UI at `http://localhost:8080/`, or bookmark a direct link to
`http://localhost:8080/live?url=https://example.com/article`.

In live mode the REST endpoints are not registered, and the reverse is also true.
An unrecognised `MODE` stops the service at startup and lists the accepted values.

## Configuration

Every setting is an environment variable. The defaults below are what the code uses
when the variable is unset. `.env.example` is the same list as a file.

| Variable | Default | Description |
| -------- | ------- | ----------- |
| `MODE` | `rest` | `rest` or `live`. Anything else stops startup with an error. |
| `CONCURRENCY` | `1` | Maximum simultaneous extractions. |
| `BUSY_RETRY_AFTER_SECONDS` | `15` | `Retry-After` value sent with a capacity refusal. |
| `MAX_TIME_MS` | `180000` | Hard ceiling for one request, in milliseconds. Keep the container stop grace period longer than this. |
| `MAX_IMAGE_BYTES` | `209715200` | Total budget for images embedded as data URIs (200 MB). |
| `GC_MODE` | `normal` | `normal`, or `aggressive` to force a compacting gen2 collection after each request. |
| `STABILIZATION_MS` | `10000` | How long the DOM is given to settle after load. |
| `SCROLL_DELAY_MS` | `100` | Delay between scroll steps while lazy images load. |
| `MAX_V8_HEAP_SIZE_MB` | `512` | V8 heap ceiling for the browser. Lower it on small hosts. |
| `CHROMIUM_PATH` | `/usr/bin/chromium` | Chromium binary. If the path does not exist, Playwright's bundled browser is used. |
| `CHROMIUM_ARGS` | none | Extra Chromium arguments, space separated. |
| `PORT` | `8080` | HTTP port. `ASPNETCORE_URLS` takes precedence if set. |
| `MAX_URL_LENGTH` | `2048` | Longest accepted URL, in characters. |
| `USER_AGENT` | Chrome 120 UA | User-Agent used for page fetches and image downloads. |
| `LOG_LEVEL` | `Information` | `Trace`, `Debug`, `Information`, `Warning`, `Error` or `Critical`. Anything else stops startup with an error. |

[docs/SPEC.md](docs/SPEC.md) has the full specification.
[docs/NOTES.md](docs/NOTES.md) covers what to do when something goes wrong.

## API

### POST /process

**Request fields.** Only `url` is required.

| Field | Default | Description |
| ----- | ------- | ----------- |
| `url` | required | http or https URL |
| `engine` | `"smartreader"` | Reader engine. `"smartreader"` is the only one. |
| `timeoutMs` | server max | Timeout for this request, in milliseconds |
| `stabilizationMs` | server default | DOM settle time for this request |
| `userAgent` | server default | User-Agent override |
| `referer` | `null` | Referer header |
| `cookies` | `null` | Cookies to set before navigation |
| `clearCookies` | `false` | Disable cookie storage for this request |
| `includeSourceHtml` | `false` | HTML as the browser produced it, before extraction |
| `includeCleanHtmlEmbeddedImages` | `false` | Article HTML with images as base64 data URIs |
| `includeCleanHtmlLinkedImages` | `false` | Article HTML with images as local filenames |
| `includeCleanHtmlNoImages` | `false` | Article HTML with `<img>` removed |
| `includeMarkdownLinkedImages` | `false` | Markdown with images as local filenames |
| `includeMarkdownNoImages` | `true` | Markdown with images removed |
| `includePlainText` | `true` | Plain text |
| `includeAllLinksInPage` | `false` | Deduplicated `<a href>` URLs from the source HTML |
| `includeAllLinksMedia` | `false` | Deduplicated `<img>` and `<video>` URLs from the source HTML |
| `includeAllLinksAssets` | `false` | Deduplicated CSS and JS URLs from the source HTML |
| `includeAllInPageImages` | `false` | Images in the extracted article |
| `includeAllOutOfPageImages` | `false` | Images in the source HTML that are not in the article |
| `imageLinkRewrite` | `false` | Rewrite `src` to local filenames. Implies `includeAllInPageImages`. |
| `pngConversionRequired` | `false` | Convert images to PNG. Portrait images are capped at 1080 px high, landscape at 720 px, aspect ratio preserved. |
| `excludeImageUrls` | `null` | Case-insensitive substrings. Any image whose resolved URL contains one is removed before numbering, so it takes no `image-NNNN` slot and is never downloaded. Omit the field or send `[]` to keep everything. |
| `unicodeNormalization` | `"preserve"` | `preserve` keeps smart quotes and dashes, `ascii` converts them to plain ASCII, `latex` writes LaTeX equivalents. |

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

Returns status, version, build commit, mode and free capacity.

## Known issues

- **No authentication.** Anyone who can reach the port can use it, and it will
  fetch any URL, including addresses on your network. Keep the port on loopback or
  put an authenticated proxy in front. [SECURITY.md](SECURITY.md) lists this as
  out of scope for vulnerability reports.
- **Live mode serves fetched HTML.** `/live?url=...` puts the extracted article
  into a page on your own origin, so a hostile page runs its script there. Fine on
  loopback for one person; do not expose live mode to the internet.
- **One extraction at a time by default.** Raise `CONCURRENCY` for more. Each slot
  is a browser context, so memory scales with it.
- **One mode per process.** REST and live cannot both be served by one deployment.
- **No interaction with the page.** It will not click a consent button, fill in a
  login form or scroll by hand. Cookies, `referer` and `userAgent` cover some of
  that.
- **The reader algorithm expects an article.** A forum thread, product page or
  landing page may extract to very little. `success: true` means the pipeline ran,
  not that the page was an article.
- **`arm64` images need public-repository runners.** While this repository is
  private, releases publish `linux/amd64` only. Re-running the release workflow on
  the same tag after the repository is public adds `linux/arm64` to the same
  manifest.
- **Some analyser rules are suppressed rather than fixed.** See
  [CHANGELOG.md](CHANGELOG.md) under `Unreleased`.

## Testing

```bash
dotnet test Distillate.sln -c Release
```

249 tests. The integration tests serve their fixtures from a loopback listener, so
they fail for code reasons and not because a website changed. Real sites, consent
walls and long-run memory use are a manual checklist in
[docs/NOTES.md](docs/NOTES.md).

## Contributing

Issues and pull requests are welcome. [CONTRIBUTING.md](CONTRIBUTING.md) covers
building, testing and what a change should look like.
[CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) applies to all project spaces. Report
security problems through [SECURITY.md](SECURITY.md), not the issue tracker.

## Dependencies

| Package | Used for |
|---|---|
| [SmartReader](https://github.com/m-dehghani/SmartReader) | Article extraction. A .NET port of Readability.js. |
| [Microsoft.Playwright](https://playwright.dev/dotnet/) | Driving Chromium. A major version bump changes the browser. |
| [AngleSharp](https://anglesharp.github.io/) | HTML parsing and the DOM. Held at the smallest patched version because it parses untrusted HTML. |
| [ReverseMarkdown](https://github.com/mysticmind/reversemarkdown-net) | HTML to markdown. |
| [SkiaSharp](https://github.com/mono/SkiaSharp) | PNG conversion and downscaling. |

Dependabot watches NuGet, GitHub Actions and the container base image.

## Version history

[CHANGELOG.md](CHANGELOG.md). `GET /health` reports the running version and the
commit it was built from.

## Saying Thank You

This was written by me, initially for me. I put in the extra work to make it usable
by someone who isn't me, which is the tedious part. If you find it useful and want
to say thanks, you can [sponsor it on GitHub](https://github.com/sponsors/Hyper-Dragon)
or [bung a few quid in the hat](https://www.paypal.com/donate/?hosted_button_id=QNKADKV5BAM5C).
No pressure.

## Software Support

Report defects on the [issues page](https://github.com/Hyper-Dragon/Distillate/issues/new/choose).
The template asks for the build from `GET /health`, because with it the build is
known. Say which site you were extracting, and attach the saved HTML if the page
is not public — "it didn't work" is hard to act on.

## Licence

MIT. See [LICENSE](LICENSE).
