# Distillate Service Specification v1.1

## Purpose

Distillate is a capacity-bound, synchronous HTTP service that retrieves a URL using a real browser engine, extracts a high-quality "reader view" representation, and returns structured outputs suitable for archival, note-taking, and offline use.

The service is designed for **honest backpressure**, **predictable memory behavior**, and **operational sanity on low-power ARM systems**, especially Raspberry Pi-class hardware.

This is not a crawler, queue, or batch processor. It is a sharp, truthful tool.

---

## Changes from v1.0

The coarse `includeImages` / `includeSourceHtml` booleans are replaced by a granular, per-output inclusion model. Each distinct output is opt-in. The old `markdown`, `cleanHtml`, `rawText`, and `images` response fields are retired in favour of named variants. All changes are breaking; no backward-compatibility shims.

| v1.0 Field/Flag | v1.1 Replacement |
| --- | --- |
| `includeImages` (request) | `includeCleanHtmlEmbeddedImages` |
| `includeSourceHtml` (request) | `includeSourceHtml` (unchanged) |
| `rawText` (response) | `plainText` |
| `markdown` (response) | `markdownNoImages` / `markdownLinkedImages` |
| `cleanHtml` (response) | `cleanHtmlEmbeddedImages` / `cleanHtmlNoImages` / `cleanHtmlLinkedImages` |
| `images` (response) | `allInPageImages` / `allOutOfPageImages` |

---

## Core Design Principles

* No internal queuing � capacity limits are explicit and enforced at request time.
* Deterministic failure � if the service cannot process a request immediately, it says so.
* Fail-fast browser execution � pathological pages may crash the browser, and that is acceptable.
* Memory is treated as a leased resource, aggressively reclaimed between requests.
* The JSON response contract is stable; errors are data, not protocol.

---

## Technology Stack

* Language: C#
* Runtime: .NET 10.0
* Web Framework: ASP.NET Core Minimal APIs
* JSON: System.Text.Json only (no Newtonsoft)
* Browser Automation: Microsoft.Playwright (without bundled browsers)
* Browser Engine: System-installed Chromium
* Reader Engine: SmartReader (Mozilla Readability.js port — equivalent to Firefox Reader View)
* Article Cleaning: HtmlArticleCleaner (AngleSharp DOM-based structural post-processing)
* Markdown Conversion: ReverseMarkdown
* Image Conversion: SkiaSharp (Linux ARM64/amd64 compatible; System.Drawing.Common is explicitly excluded)

---

## Project Structure

```text
Distillate/
|   src/
|   --- Distillate.Core/           # Domain models, extraction logic, services
|   |   |   Configuration/         # DistillateOptions
|   |   |   Conversion/            # MarkdownConverter
|   |   |   Extraction/            # SmartReader engine
|   |   |   Images/                # ImageProcessor, LinkExtractor
|   |   |   Models/                # ProcessRequest, ProcessResponse
|   |   |   Services/              # IDistillateService, DistillateService
|   |-- Distillate.Web/            # HTTP API host
|-- tests/
|   |-- Distillate.Tests/          # Unit and integration tests
|-- Dockerfile
|-- docker-compose.yml
|-- SPEC.md
```

---

## Container and Runtime Environment

* Linux container (amd64 and arm64)
* Chromium installed via apt
* Container uses tini as PID 1 to prevent zombie processes
* Required Chromium flags:
  * `--no-sandbox`
  * `--disable-dev-shm-usage`
  * `--disable-gpu`
  * `--js-flags=--max-old-space-size=512` (configurable)

The init system is mandatory to prevent Chromium zombie processes from exhausting PID space.

---

## HTTP API

### Health Check

```text
GET /health
```

Returns:

```json
{
  "status": "healthy",
  "timestamp": "2024-01-15T10:30:00Z",
  "concurrency": {
    "max": 1,
    "available": 1
  }
}
```

### Process Endpoint

```text
POST /process
```

#### Request Body

```json
{
  "url": "https://example.com/article",
  "engine": "smartreader",
  "timeoutMs": 60000,
  "stabilizationMs": 5000,
  "userAgent": "Custom/1.0",
  "cookies": [
    {
      "name": "session",
      "value": "abc123",
      "domain": "example.com",
      "path": "/"
    }
  ],
  "includeSourceHtml": false,
  "includeCleanHtmlEmbeddedImages": false,
  "includeCleanHtmlLinkedImages": false,
  "includeCleanHtmlNoImages": false,
  "includeMarkdownLinkedImages": false,
  "includeMarkdownNoImages": true,
  "includePlainText": true,
  "includeAllLinksInPage": false,
  "includeAllLinksMedia": false,
  "includeAllLinksAssets": false,
  "includeAllInPageImages": false,
  "includeAllOutOfPageImages": false,
  "imageLinkRewrite": false,
  "pngConversionRequired": false
}
```

| Field | Type | Default | Description |
| ----- | ---- | ------- | ----------- |
| url | string | required | URL to process (http/https only) |
| engine | string | "smartreader" | Reader engine (only "smartreader" is supported) |
| timeoutMs | int? | null | Override timeout (capped by server max) |
| stabilizationMs | int? | null | Override DOM stabilization delay |
| userAgent | string? | null | Override User-Agent |
| cookies | array? | null | Cookies to set before navigation |
| includeSourceHtml | bool | false | Raw browser-captured HTML before reader extraction |
| includeCleanHtmlEmbeddedImages | bool | false | Reader HTML with images inlined as base64 data URIs |
| includeCleanHtmlLinkedImages | bool | false | Reader HTML with images as local filenames |
| includeCleanHtmlNoImages | bool | false | Reader HTML with all img elements stripped |
| includeMarkdownLinkedImages | bool | false | Markdown with image references as local filenames |
| includeMarkdownNoImages | bool | true | Markdown with image references stripped |
| includePlainText | bool | true | Plain text extracted by reader engine |
| includeAllLinksInPage | bool | false | Deduplicated href URLs from source HTML |
| includeAllLinksMedia | bool | false | Deduplicated img/video/source URLs from source HTML |
| includeAllLinksAssets | bool | false | Deduplicated CSS/JS asset URLs from source HTML |
| includeAllInPageImages | bool | false | Images present in the reader-extracted clean HTML |
| includeAllOutOfPageImages | bool | false | Images found in source HTML but absent from clean HTML |
| imageLinkRewrite | bool | false | Rewrite img src in linked outputs to local filenames. Implicitly forces includeAllInPageImages=true |
| pngConversionRequired | bool | false | Convert all fetched images to PNG via SkiaSharp. When false, images are returned in their original format |

#### Implicit Behaviour Rules

**`imageLinkRewrite` implies `includeAllInPageImages`:** When `imageLinkRewrite=true` and `includeAllInPageImages` is not explicitly set, the service treats it as `true` and sets the `ImageLinkRewriteImpliedInPageImages` completion flag. `includeAllOutOfPageImages` is not affected.

**Image deduplication:** Images are deduplicated by normalised source URL. The first occurrence in document order determines the index. Subsequent `<img>` elements referencing the same URL are rewritten to the same local filename. The `allInPageImages` and `allOutOfPageImages` arrays each contain one entry per unique source URL and maintain independent index sequences starting from 0.

**Local filename scheme:**

| Condition | Pattern |
| --- | --- |
| `pngConversionRequired=false` | `image-0000.jpg`, `image-0001.webp`, � (extension from HTTP response MIME type) |
| `pngConversionRequired=true` | `image-0000.png`, `image-0001.png`, � |

The `localName` field on each `ImageEntry` is the authoritative filename. Downstream tooling is responsible for saving files using that name.

**PNG conversion:** Uses SkiaSharp. `System.Drawing.Common` is explicitly excluded (GDI+ is unavailable on Linux ARM).

#### Response

```json
{
  "success": true,
  "requestedUrl": "https://example.com/article",
  "processedAt": "2024-01-15T10:30:00Z",
  "processingTimeMs": 1523.5,
  "title": "Article Title",
  "byline": "By Jane Doe",
  "author": "Jane Doe",
  "excerpt": "A brief summary of the article content.",
  "siteName": "Example News",
  "language": "en",
  "publicationDate": "2024-01-15T00:00:00",
  "featuredImage": "https://example.com/hero.jpg",
  "timeToRead": "00:05:00",
  "completionFlags": 2,
  "errors": null,
  "traceId": "abc123def456",

  "plainText": "Plain text content...",
  "sourceHtml": null,
  "cleanHtmlEmbeddedImages": null,
  "cleanHtmlLinkedImages": "<h1>Article Title</h1><img src=\"image-0000.jpg\">",
  "cleanHtmlNoImages": null,
  "markdownLinkedImages": "# Article Title\n\n![Alt text](image-0000.jpg)",
  "markdownNoImages": "# Article Title\n\nContent...",

  "allLinksInPage": ["https://example.com/page1", "https://example.com/page2"],
  "allLinksMedia": ["https://example.com/photo.jpg"],
  "allLinksAssets": ["https://example.com/style.css"],

  "allInPageImages": [
    {
      "index": 0,
      "localName": "image-0000.jpg",
      "originalUrl": "https://example.com/photo.jpg",
      "originalMimeType": "image/jpeg",
      "alt": "A photo",
      "sizeBytes": 45312,
      "data": "<base64 bytes>"
    }
  ],
  "allOutOfPageImages": null
}
```

| Field | Type | Null when | Description |
| ----- | ---- | --------- | ----------- |
| success | bool | � | false if extraction failed |
| requestedUrl | string | � | Echo of input URL |
| processedAt | DateTimeOffset | � | UTC timestamp of completion |
| processingTimeMs | double | � | Wall-clock processing time |
| title | string? | extraction failed | Page title from reader engine |
| byline | string? | not available | Byline text (e.g. "By Jane Doe") from reader engine |
| author | string? | not available | Author name extracted by reader engine |
| excerpt | string? | not available | Short summary/description from article metadata |
| siteName | string? | not available | Name of the originating site |
| language | string? | not available | Article language (e.g. "en") |
| publicationDate | DateTime? | not available | Publication date extracted from article metadata |
| featuredImage | string? | not available | URL of the article's featured/hero image |
| timeToRead | string | — | Estimated reading time (TimeSpan format, e.g. "00:05:00") |
| completionFlags | int | — | Bitmask (see below) |
| errors | string[]? | no errors | Structured error messages |
| traceId | string? | � | Trace ID, echoed or auto-generated |
| plainText | string? | not requested | Plain text from reader engine |
| sourceHtml | string? | not requested | Raw browser HTML |
| cleanHtmlEmbeddedImages | string? | not requested | Clean HTML, images as data URIs |
| cleanHtmlLinkedImages | string? | not requested | Clean HTML, images as local filenames |
| cleanHtmlNoImages | string? | not requested | Clean HTML, img elements stripped |
| markdownLinkedImages | string? | not requested | Markdown with image local filenames |
| markdownNoImages | string? | not requested | Markdown, images stripped |
| allLinksInPage | string[]? | not requested | Deduplicated href URLs from source HTML |
| allLinksMedia | string[]? | not requested | Deduplicated media URLs from source HTML |
| allLinksAssets | string[]? | not requested | Deduplicated asset URLs from source HTML |
| allInPageImages | ImageEntry[]? | not requested | Images in reader-extracted clean HTML |
| allOutOfPageImages | ImageEntry[]? | not requested | Images in source HTML absent from clean HTML |

#### ImageEntry

| Field | Type | Description |
| ----- | ---- | ----------- |
| index | int | Zero-based, unique per source URL within its array |
| localName | string | Authoritative filename: image-{index:D4}.{ext} |
| originalUrl | string? | Absolute source URL |
| originalMimeType | string? | MIME type from HTTP response headers |
| alt | string? | alt text from first occurrence in document order |
| sizeBytes | long | Byte length of data after any conversion |
| data | string | Base64-encoded image bytes |

#### Headers

| Header | Purpose |
| ------ | ------- |
| X-Trace-Id | Optional trace ID for distributed tracing (auto-generated if not provided) |

### Backpressure Behavior

* Concurrency is enforced via `SemaphoreSlim`
* Requests attempt immediate acquisition:
  * Success ? processing begins
  * Failure ? HTTP 503 Service Unavailable
* No waiting, no queuing

---

## Configuration (Environment Variables)

| Name | Default | Description |
| ---- | ------- | ----------- |
| CONCURRENCY | 1 | Max simultaneous requests |
| MAX_TIME_MS | 180000 | Hard timeout per request (3 minutes) |
| MAX_IMAGE_BYTES | 209715200 | Global image embedding budget (200MB) |
| GC_MODE | "normal" | GC strategy: "normal" or "aggressive" |
| STABILIZATION_MS | 10000 | DOM stabilization delay (10 seconds) |
| MAX_URL_LENGTH | 2048 | Maximum URL length accepted |
| USER_AGENT | Chrome 120 | Default User-Agent string |
| CHROMIUM_PATH | /usr/bin/chromium | Chromium executable path |
| CHROMIUM_ARGS | | Additional Chromium flags |
| PORT | 8080 | HTTP listen port |
| LOG_LEVEL | Information | Logging level |
| SCROLL_DELAY_MS | 100 | Delay between scroll steps |
| MAX_V8_HEAP_SIZE_MB | 512 | V8 JavaScript heap limit |

### Raspberry Pi Guidance (8GB)

* CONCURRENCY=1 ? Maximum stability (recommended)
* CONCURRENCY=3 ? Safe for simple pages
* CONCURRENCY=5 ? Risky on JS-heavy sites

Increase host swap to ~2GB for best results.

---

## DOM Stabilization Strategy

Modern sites aggressively lazy-load content. Before DOM capture:

1. Wait for initial page readiness (DOMContentLoaded)
2. Execute scripted scroll-to-bottom
3. Allow configurable stabilization delay
4. Capture final DOM snapshot

This ensures images and hydrated content are present.

---

## Processing Pipeline

```mermaid
Browser fetch ? sourceHtml captured
     ?
     ??? allLinksInPage   (deduplicated <a href> from sourceHtml)
     ??? allLinksMedia    (deduplicated <img>,<video>,<source> from sourceHtml)
     ??? allLinksAssets   (deduplicated <link>,<script> from sourceHtml)
     ?
     ??? Reader engine extraction (SmartReader)
              ?
              ??? HtmlArticleCleaner (DOM-based structural cleaning)
              ?       ?
              ?       ??? cleanHtml (cleaned article HTML)
              ?       ?
              ?       ??? cleanHtmlNoImages       (strip <img> elements)
              ?       ??? cleanHtmlLinkedImages   (rewrite src ? localName)
              ?       ??? cleanHtmlEmbeddedImages (embed as data URIs, budget enforced)
              ?
              ??? markdownNoImages       (converted from cleanHtmlNoImages)
              ??? markdownLinkedImages   (converted from cleanHtmlLinkedImages)
              ??? plainText              (from HtmlArticleCleaner.ExtractPlainText)
              ?
              ??? allInPageImages
              ?       (unique URLs from cleanHtml <img> elements)
              ?       (fetch ? optional PNG convert via SkiaSharp ? index ? localName)
              ?
              ??? allOutOfPageImages
                      (unique URLs in sourceHtml <img> NOT in cleanHtml)
                      (fetch ? optional PNG convert via SkiaSharp ? index ? localName)
```

---

## Image Handling and Memory Discipline

* `cleanHtmlEmbeddedImages` embeds images as data URIs incrementally
* A running byte budget is enforced (MAX_IMAGE_BYTES)
* When budget is exceeded:
  * Stop embedding further images
  * Continue all other extraction
  * Set `ImageTruncated` completion flag
* `allInPageImages` and `allOutOfPageImages` fetch images independently of the embedding budget
* Large allocations use pooled buffers (ArrayPool) to avoid LOH fragmentation
* PNG conversion uses SkiaSharp (no GDI+ / System.Drawing.Common)

---

## Garbage Collection Strategy

Server GC is enabled.

For `GC_MODE=aggressive`:

* LOH compaction is explicitly requested at request completion
* Full blocking GC is triggered

This is intentionally "anti-throughput" in favor of deterministic memory recovery between requests.

---

## CompletionFlags (Bitmask)

| Value | Flag | Description |
| ----- | ---- | ----------- |
| 0 | Complete | Processing completed successfully |
| 1 | ImageTruncated | Image budget exceeded during embedding |
| 2 | SourceHtmlOmitted | includeSourceHtml was false |
| 4 | _Reserved_ | _(was EngineFallbackUsed — removed)_ |
| 8 | ImageLinkRewriteApplied | imageLinkRewrite=true rewrites were applied |
| 16 | ImageLinkRewriteImpliedInPageImages | imageLinkRewrite implicitly forced includeAllInPageImages=true |
| 32 | AllInPageImagesPartial | One or more in-page images could not be fetched or decoded |
| 64 | AllOutOfPageImagesPartial | One or more out-of-page images could not be fetched or decoded |

Flags can be combined (e.g., 9 = ImageTruncated + ImageLinkRewriteApplied).

---

## Error Strategy

* Transport failure ? HTTP 503
* Processing failure ? HTTP 200 with `Success = false`
* Errors are returned as structured data in the `errors` array

This guarantees contract stability for automation pipelines.

---

## Running Locally

### Development

```bash
cd src/Distillate.Web
dotnet run
```

### Docker

```bash
docker-compose up --build
```

### Testing

```bash
dotnet test
```

### Example Request

```bash
curl -X POST http://localhost:8080/process \
  -H "Content-Type: application/json" \
  -d '{"url": "https://example.com"}'
```

---

## Non-Goals

* No request queue
* No persistence
* No authentication
* No retries
* No browser pooling
* No CORS (backend-to-backend only)

Distillate is a deterministic extraction service, not a platform.

---

## Summary

Distillate v1.0 is a deliberately conservative, operationally honest service. It trades peak throughput for correctness, stability, and clarity�especially on constrained ARM systems.

If the service accepts your request, it will genuinely try to fulfill it.
If it cannot, it will tell you immediately.

No lies. No zombies. No surprises.
