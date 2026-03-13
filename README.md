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
| `CONCURRENCY` | 1 | Max simultaneous requests |
| `MAX_TIME_MS` | 180000 | Request timeout (3 minutes) |
| `MAX_IMAGE_BYTES` | 209715200 | Image budget (200MB) |
| `GC_MODE` | normal | GC strategy: `normal` or `aggressive` |
| `STABILIZATION_MS` | 10000 | DOM stabilization delay |
| `LOG_LEVEL` | Information | Logging level |
| `PORT` | 8080 | HTTP listen port |

See [SPEC.md](SPEC.md) for complete documentation.

## API

### POST /process

Process a URL and extract reader view content.

**Request:**

```json
{
  "url": "https://example.com/article",
  "engine": "smartreader",
  "includePlainText": true,
  "includeMarkdownNoImages": true,
  "includeCleanHtmlNoImages": false,
  "includeSourceHtml": false
}
```

**Response:**

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
  "completionFlags": 2,
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
