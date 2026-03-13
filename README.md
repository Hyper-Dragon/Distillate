# Distillate

A capacity-bound, synchronous HTTP service that extracts "reader view" content from web pages using a real browser engine.

## Features

- **Honest Backpressure**: No internal queuing. If the service is at capacity, it returns 503 immediately.
- **Real Browser Rendering**: Uses Playwright with system-installed Chromium for JavaScript-heavy pages.
- **SmartReader Engine**: Uses Mozilla Readability.js port (same algorithm as Firefox Reader View) with DOM-based post-processing.
- **Article Metadata**: Extracts byline, author, excerpt, site name, language, publication date, featured image, and estimated reading time.
- **Image Embedding**: Converts images to base64 data URIs with configurable memory budget.
- **Markdown Output**: Clean markdown conversion for note-taking and archival.
- **ARM Optimized**: Designed for Raspberry Pi and other low-power ARM systems.

## Quick Start

### Running Locally (Development)

```bash
# Note: Requires Chromium to be installed on your system
cd src/Distillate.Web
dotnet run
```

### Running with Docker

```bash
docker-compose up --build
```

### Example Request

```bash
curl -X POST http://localhost:8080/process \
  -H "Content-Type: application/json" \
  -d '{"url": "https://example.com/article"}'
```

### Health Check

```bash
curl http://localhost:8080/health
```

## Configuration

All configuration is done via environment variables:

| Variable | Default | Description |
| -------- | ------- | ----------- |
| `CONCURRENCY` | `1` | Max simultaneous requests |
| `MAX_TIME_MS` | `180000` | Hard timeout per request (3 minutes) |
| `MAX_IMAGE_BYTES` | `209715200` | Global image embedding budget (200 MB) |
| `GC_MODE` | `normal` | GC strategy: `normal` or `aggressive` |
| `STABILIZATION_MS` | `10000` | DOM stabilization delay (10 seconds) |
| `LOG_LEVEL` | `Information` | Logging level |
| `PORT` | `8080` | HTTP listen port |
| `CHROMIUM_PATH` | `/usr/bin/chromium` | Path to Chromium binary |
| `MAX_URL_LENGTH` | `2048` | Maximum URL length accepted |
| `USER_AGENT` | Chrome 120 UA string | Default User-Agent for page fetching |
| `SCROLL_DELAY_MS` | `100` | Delay between scroll steps (ms) |
| `MAX_V8_HEAP_SIZE_MB` | `512` | V8 JavaScript heap size limit |

See [SPEC.md](SPEC.md) for complete documentation.

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
| `cookies` | `null` | Cookies to inject before navigation |
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
| `pngConversionRequired` | `false` | Convert all fetched images to PNG via SkiaSharp |

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

## Testing

```bash
dotnet test
```

## License

MIT
