# Distillate Service Usage Guide

## What is Distillate?

Distillate is a capacity-bound, synchronous HTTP service that retrieves a URL using a real browser engine, extracts a high-quality "reader view" representation, and returns structured outputs suitable for archival, note-taking, and offline use.

**Key characteristics:**
- **Honest backpressure** � if the service cannot process your request immediately, it tells you (HTTP 503).
- **No internal queuing** � capacity limits are explicit and enforced at request time.
- **Deterministic failure** � errors are returned as structured JSON data, not protocol errors.
- **Designed for constrained environments** � runs reliably on Raspberry Pi-class hardware.

---

## Endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/` | Service info and usage hint |
| GET | `/health` | Health check with concurrency status |
| POST | `/process` | Extract reader view from a URL |

---

## Backpressure Behavior

Distillate enforces a **strict concurrency limit** (default: 1 simultaneous request).

- When a request arrives, the service attempts to acquire a processing slot **immediately**.
- **If a slot is available**: processing begins.
- **If no slot is available**: the service returns **HTTP 503 Service Unavailable** immediately. No waiting, no queuing.

This design ensures:
- Predictable response times.
- No request pile-up or memory exhaustion under load.
- Clients can implement their own retry/backoff logic.

**Recommended client behavior:**
- On HTTP 503, wait and retry with exponential backoff.
- Do not flood the service with parallel requests.

---

## Request Format

### POST /process

**Headers:**
| Header | Required | Description |
|--------|----------|-------------|
| Content-Type | Yes | Must be `application/json` |
| X-Trace-Id | No | Optional trace ID for distributed tracing (auto-generated if not provided) |

**Request Body (JSON):**

```json
{
  "url": "https://example.com/article",
  "engine": "smartreader",
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
| url | string | **required** | URL to process (http/https only) |
| engine | string | "smartreader" | Reader engine (only `"smartreader"` is supported) |
| includeImages | boolean | true | Embed images as base64 data URIs in output |
| includeSourceHtml | boolean | false | Include raw source HTML (for debugging) |
| timeoutMs | int | null | Override timeout (capped by server max) |
| stabilizationMs | int | null | Override DOM stabilization delay |
| userAgent | string | null | Override User-Agent |
| cookies | array | null | Cookies to set before navigation |

---

## Response Format

**Success response (HTTP 200):**

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

**Error response (HTTP 200 with `success: false`):**

```json
{
  "success": false,
  "requestedUrl": "https://example.com/article",
  "processedAt": "2024-01-15T10:30:00Z",
  "processingTimeMs": 50.2,
  "completionFlags": 0,
  "errors": ["Failed to fetch page content"],
  "traceId": "abc123def456"
}
```

**Backpressure response (HTTP 503):**
- No body � the service is at capacity.
- Retry with exponential backoff.

---

## Completion Flags

The `completionFlags` field is a bitmask indicating processing status:

| Value | Flag | Description |
|-------|------|-------------|
| 0 | Complete | Processing completed successfully |
| 1 | ImageTruncated | Image budget exceeded, some images omitted |
| 2 | SourceHtmlOmitted | Source HTML not included (default behavior) |
| 4 | _Reserved_ | _(was EngineFallbackUsed — removed)_ |

Flags can be combined (e.g., `9` = ImageTruncated + ImageLinkRewriteApplied).

---

## Example curl Commands

### Health Check

```bash
curl http://localhost:8080/health
```

**Response:**
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

### Service Info

```bash
curl http://localhost:8080/
```

### Basic Article Extraction

```bash
curl -X POST http://localhost:8080/process \
  -H "Content-Type: application/json" \
  -d '{
    "url": "https://example.com/article",
    "engine": "smartreader",
    "includeImages": false,
    "includeSourceHtml": false
  }'
```

### Full Request with All Options

```bash
curl -X POST http://localhost:8080/process \
  -H "Content-Type: application/json" \
  -H "X-Trace-Id: my-trace-123" \
  -d '{
    "url": "https://example.com/article",
    "engine": "smartreader",
    "includeImages": true,
    "includeSourceHtml": true,
    "timeoutMs": 60000,
    "stabilizationMs": 5000,
    "userAgent": "MyApp/1.0",
    "cookies": [
      {
        "name": "session",
        "value": "abc123",
        "domain": "example.com",
        "path": "/"
      }
    ]
  }'
```

### Request with Custom Timeout

```bash
curl -X POST http://localhost:8080/process \
  -H "Content-Type: application/json" \
  -d '{
    "url": "https://slow-loading-site.com/page",
    "timeoutMs": 120000,
    "stabilizationMs": 15000
  }'
```

---

## PowerShell Example

```powershell
$body = @{
    url = "https://example.com/article"
    engine = "smartreader"
    includeImages = $false
    includeSourceHtml = $false
} | ConvertTo-Json

# Disable Expect: 100-continue (can cause issues with some servers)
[System.Net.ServicePointManager]::Expect100Continue = $false

$response = Invoke-RestMethod -Uri 'http://localhost:8080/process' `
    -Method Post `
    -ContentType 'application/json' `
    -Body $body

# Access response fields
$response.success
$response.title
$response.markdown
```

---

## Integration Notes

### Handling Backpressure (HTTP 503)

Implement exponential backoff when the service returns 503:

```python
import requests
import time

def extract_article(url, max_retries=5):
    base_delay = 1  # seconds
    
    for attempt in range(max_retries):
        response = requests.post(
            'http://localhost:8080/process',
            json={'url': url, 'includeImages': False}
        )
        
        if response.status_code == 503:
            delay = base_delay * (2 ** attempt)
            print(f"Service busy, retrying in {delay}s...")
            time.sleep(delay)
            continue
        
        return response.json()
    
    raise Exception("Service unavailable after max retries")
```

### Checking Concurrency Before Requesting

Use the health endpoint to check available capacity:

```bash
# Check if service has capacity
AVAILABLE=$(curl -s http://localhost:8080/health | jq '.concurrency.available')
if [ "$AVAILABLE" -gt 0 ]; then
    # Safe to make request
    curl -X POST http://localhost:8080/process ...
fi
```

### Timeouts

- Default server timeout: 180 seconds (3 minutes)
- Client-specified `timeoutMs` is capped by server maximum
- Set your HTTP client timeout slightly higher than the server timeout to receive error responses

---

## Environment Variables (Server Configuration)

These are configured on the server side, not by clients:

| Name | Default | Description |
|------|---------|-------------|
| CONCURRENCY | 1 | Max simultaneous requests |
| MAX_TIME_MS | 180000 | Hard timeout per request (3 minutes) |
| MAX_IMAGE_BYTES | 209715200 | Global image embedding budget (200MB) |
| STABILIZATION_MS | 10000 | DOM stabilization delay (10 seconds) |
| MAX_URL_LENGTH | 2048 | Maximum URL length accepted |

---

## Error Handling

- **HTTP 200 with `success: true`**: Request completed successfully.
- **HTTP 200 with `success: false`**: Request processed but extraction failed (check `errors` array).
- **HTTP 400**: Bad request (invalid URL, URL too long, etc.).
- **HTTP 503**: Service at capacity � retry with backoff.

The service guarantees **contract stability**: errors are always returned as structured JSON data in the `errors` array, never as protocol-level failures (except 503 for backpressure).

---

## Tips for Consumers

1. **Always check `success` field** � HTTP 200 does not guarantee successful extraction.
2. **Use `traceId`** � Pass your own `X-Trace-Id` header for distributed tracing.
3. **Handle `completionFlags`** � Check for partial results (e.g., truncated images).
4. **Set appropriate timeouts** � Complex pages may take 30-60+ seconds to render.
5. **Respect backpressure** � Don't retry immediately on 503; use exponential backoff.
6. **Prefer `includeImages: false`** for faster responses when images aren't needed.
