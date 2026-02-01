# Distillate Service Specification v1.0

## Purpose

Distillate is a capacity-bound, synchronous HTTP service that retrieves a URL using a real browser engine, extracts a high-quality "reader view" representation, and returns structured outputs suitable for archival, note-taking, and offline use.

The service is designed for **honest backpressure**, **predictable memory behavior**, and **operational sanity on low-power ARM systems**, especially Raspberry Pi-class hardware.

This is not a crawler, queue, or batch processor. It is a sharp, truthful tool.

---

## Core Design Principles

* No internal queuing — capacity limits are explicit and enforced at request time.
* Deterministic failure — if the service cannot process a request immediately, it says so.
* Fail-fast browser execution — pathological pages may crash the browser, and that is acceptable.
* Memory is treated as a leased resource, aggressively reclaimed between requests.
* The JSON response contract is stable; errors are data, not protocol.

---

## Technology Stack

* Language: C#
* Runtime: .NET 9.0+
* Web Framework: ASP.NET Core Minimal APIs
* JSON: System.Text.Json only (no Newtonsoft)
* Browser Automation: Microsoft.Playwright (without bundled browsers)
* Browser Engine: System-installed Chromium
* Reader Engines:
  1. Readability (AngleSharp-based)
  2. SmartReader (Mozilla Readability port)
* Markdown Conversion: ReverseMarkdown

---

## Project Structure

```
Distillate/
|   src/
|   --- Distillate.Core/           # Domain models, extraction logic, services
|   |   |   Configuration/         # DistillateOptions
|   |   |   Conversion/            # MarkdownConverter
|   |   |   Extraction/            # Reader engines (Readability, SmartReader)
|   |   |   Images/                # ImageEmbedder with memory budgeting
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

```
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

```
POST /process
```

#### Request Body

```json
{
  "url": "https://example.com/article",
  "engine": "readability",
  "includeImages": true,
  "includeSourceHtml": false,
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
  ]
}
```

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| url | string | required | URL to process (http/https only) |
| engine | string | "readability" | Reader engine: "readability" or "smartreader" |
| includeImages | boolean | true | Embed images as base64 data URIs |
| includeSourceHtml | boolean | false | Include raw source HTML (debugging) |
| timeoutMs | int? | null | Override timeout (capped by server max) |
| stabilizationMs | int? | null | Override DOM stabilization delay |
| userAgent | string? | null | Override User-Agent |
| cookies | array? | null | Cookies to set before navigation |

#### Response

```json
{
  "success": true,
  "requestedUrl": "https://example.com/article",
  "processedAt": "2024-01-15T10:30:00Z",
  "processingTimeMs": 1523.5,
  "title": "Article Title",
  "rawText": "Plain text content...",
  "markdown": "# Article Title\n\nContent...",
  "cleanHtml": "<h1>Article Title</h1><p>Content...</p>",
  "sourceHtml": null,
  "completionFlags": 0,
  "errors": null,
  "traceId": "abc123def456"
}
```

#### Headers

| Header | Purpose |
|--------|---------|
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
|------|---------|-------------|
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

## Reader Extraction Pipeline

1. Source HTML captured from Chromium
2. Reader engine selected per request (default: readability)
3. Base URL set for proper link resolution
4. Relative URLs rewritten to absolute
5. Clean, self-contained HTML produced
6. Markdown generated from cleaned DOM
7. Plaintext extracted
8. If primary engine fails, fallback engine is tried

HTML output is valid HTML5.

---

## Image Handling and Memory Discipline

* Images are embedded as Data URIs in CleanHtml
* Images are added incrementally
* A running byte budget is enforced (MAX_IMAGE_BYTES)
* When budget is exceeded:
  * Stop embedding images
  * Continue text extraction
  * Mark response with ImageTruncated flag

Large allocations use pooled buffers (ArrayPool) to avoid LOH fragmentation.

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
|-------|------|-------------|
| 0 | Complete | Processing completed successfully |
| 1 | ImageTruncated | Image budget exceeded |
| 2 | SourceHtmlOmitted | Source HTML not included (default) |
| 4 | EngineFallbackUsed | Primary engine failed, fallback succeeded |

Flags can be combined (e.g., 5 = ImageTruncated + EngineFallbackUsed).

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

Distillate v1.0 is a deliberately conservative, operationally honest service. It trades peak throughput for correctness, stability, and clarity—especially on constrained ARM systems.

If the service accepts your request, it will genuinely try to fulfill it.
If it cannot, it will tell you immediately.

No lies. No zombies. No surprises.
