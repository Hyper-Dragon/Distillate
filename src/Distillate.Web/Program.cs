using System.Runtime;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using Distillate.Core.Configuration;
using Distillate.Core.Conversion;
using Distillate.Core.Extraction;
using Distillate.Core.Models;
using Distillate.Core.Services;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Configure options from environment variables
builder.Services.Configure<DistillateOptions>(options =>
{
    options.Concurrency = GetEnvInt("CONCURRENCY", 1);
    options.MaxTimeMs = GetEnvInt("MAX_TIME_MS", 180_000);
    options.MaxImageBytes = GetEnvLong("MAX_IMAGE_BYTES", 200 * 1024 * 1024);
    options.GcMode = Environment.GetEnvironmentVariable("GC_MODE") ?? "normal";
    options.StabilizationMs = GetEnvInt("STABILIZATION_MS", 10_000);
    options.MaxUrlLength = GetEnvInt("MAX_URL_LENGTH", 2048);
    options.UserAgent = Environment.GetEnvironmentVariable("USER_AGENT") 
        ?? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
    options.ChromiumPath = Environment.GetEnvironmentVariable("CHROMIUM_PATH") ?? "/usr/bin/chromium";
    options.ChromiumArgs = Environment.GetEnvironmentVariable("CHROMIUM_ARGS");
    options.Port = GetEnvInt("PORT", 8080);
    options.LogLevel = Environment.GetEnvironmentVariable("LOG_LEVEL") ?? "Information";
    options.ScrollDelayMs = GetEnvInt("SCROLL_DELAY_MS", 100);
    options.MaxV8HeapSizeMb = GetEnvInt("MAX_V8_HEAP_SIZE_MB", 512);
    options.Mode = Environment.GetEnvironmentVariable("MODE") ?? "rest";
});

// Configure logging
var logLevel = Environment.GetEnvironmentVariable("LOG_LEVEL") ?? "Information";
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(Enum.Parse<LogLevel>(logLevel, ignoreCase: true));

// Configure JSON serialization
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// Register services
builder.Services.AddSingleton<ReaderEngineFactory>();
builder.Services.AddSingleton<MarkdownConverter>();
builder.Services.AddScoped<IDistillateService, DistillateService>();

// Configure HttpClient for image downloading.
// "ImageDownloader" uses normal cookie behaviour (default).
// "ImageDownloaderNoCookies" sets UseCookies=false for requests where ClearCookies=true.
builder.Services.AddHttpClient("ImageDownloader", (sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<DistillateOptions>>().Value;
    client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHttpClient("ImageDownloaderNoCookies", (sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<DistillateOptions>>().Value;
    client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
    client.Timeout = TimeSpan.FromSeconds(30);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    UseCookies = false
});

// Enable Server GC
GCSettings.LatencyMode = GCLatencyMode.Batch;

var app = builder.Build();

// Get options for semaphore setup
var options = app.Services.GetRequiredService<IOptions<DistillateOptions>>().Value;
var semaphore = new SemaphoreSlim(options.Concurrency, options.Concurrency);
var logger = app.Services.GetRequiredService<ILogger<Program>>();

logger.LogInformation("Distillate starting with Concurrency={Concurrency}, MaxTimeMs={MaxTimeMs}, MaxImageBytes={MaxImageBytes}, GcMode={GcMode}, Mode={Mode}",
    options.Concurrency, options.MaxTimeMs, options.MaxImageBytes, options.GcMode, options.Mode);

// Health check endpoint (available in both modes)
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTimeOffset.UtcNow,
    mode = options.Mode,
    concurrency = new
    {
        max = options.Concurrency,
        available = semaphore.CurrentCount
    }
}));

if (options.IsLiveMode)
{
    logger.LogInformation("Starting in LIVE mode — interactive web UI");

    // Root endpoint serves the live mode HTML page
    app.MapGet("/", () => Results.Content(LiveModeHtml(), "text/html"));

    // Live processing endpoint — accepts a URL via query parameter (?url=...) or form GET,
    // enabling direct links, bookmarks, and browser integrations such as:
    //   http://localhost:8080/live?url=https://example.com/article
    app.MapGet("/live", async (HttpContext context, IDistillateService service) =>
    {
        var url = context.Request.Query["url"].ToString().Trim();
        var userAgentParam = context.Request.Query["userAgent"].ToString().Trim();
        var refererParam = context.Request.Query["referer"].ToString().Trim();
        var cookiesParam = context.Request.Query["cookies"].ToString().Trim();
        // HTML checkboxes send "on" when checked; absent means unchecked
        var clearCookiesParam = context.Request.Query["clearCookies"].ToString();
        var clearCookies = string.Equals(clearCookiesParam, "on", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(clearCookiesParam, "true", StringComparison.OrdinalIgnoreCase);
        var pngConvertParam = context.Request.Query["pngConvert"].ToString();
        var pngConvert = string.Equals(pngConvertParam, "on", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(pngConvertParam, "true", StringComparison.OrdinalIgnoreCase);

        // Use null (fall back to default) when the field is empty
        var effectiveUserAgent = string.IsNullOrEmpty(userAgentParam) ? null : userAgentParam;
        var effectiveReferer   = string.IsNullOrEmpty(refererParam)   ? null : refererParam;

        // Parse cookies from "name=value" lines (one per line)
        CookieEntry[]? parsedCookies = null;
        if (!string.IsNullOrWhiteSpace(cookiesParam))
        {
            var entries = cookiesParam
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line =>
                {
                    var eqIndex = line.IndexOf('=');
                    if (eqIndex <= 0) return null; // skip lines with no '=' (-1) or empty name (0)
                    return new CookieEntry
                    {
                        Name = line[..eqIndex].Trim(),
                        Value = line[(eqIndex + 1)..].Trim()
                    };
                })
                .Where(c => c != null)
                .Cast<CookieEntry>()
                .ToArray();

            if (entries.Length > 0)
                parsedCookies = entries;
        }

        if (string.IsNullOrEmpty(url))
        {
            return Results.Content(LiveModeHtml("Please enter a URL.", userAgentParam, refererParam, cookiesParam, clearCookies, pngConvert), "text/html");
        }

        if (url.Length > options.MaxUrlLength)
        {
            return Results.Content(LiveModeHtml($"URL exceeds maximum length of {options.MaxUrlLength} characters.", userAgentParam, refererParam, cookiesParam, clearCookies, pngConvert), "text/html");
        }

        var traceId = Guid.NewGuid().ToString("N");

        // Honest backpressure: immediate check, no waiting
        if (!semaphore.Wait(0))
        {
            logger.LogWarning("Live request rejected due to capacity limit. TraceId: {TraceId}", traceId);
            return Results.Content(LiveModeHtml("Service is at capacity. Please try again shortly.", userAgentParam, refererParam, cookiesParam, clearCookies, pngConvert), "text/html");
        }

        try
        {
            var request = new ProcessRequest
            {
                Url = url,
                UserAgent = effectiveUserAgent,
                Referer = effectiveReferer,
                Cookies = parsedCookies,
                ClearCookies = clearCookies,
                PngConversionRequired = pngConvert,
                IncludeCleanHtmlEmbeddedImages = true,
                IncludeMarkdownNoImages = false,
                IncludePlainText = false
            };

            var result = await service.ProcessAsync(request, traceId, context.RequestAborted);

            if (!result.Success)
            {
                var errorMsg = result.Errors != null ? string.Join("; ", result.Errors) : "Processing failed.";
                return Results.Content(LiveModeHtml(errorMsg, userAgentParam, refererParam, cookiesParam, clearCookies, pngConvert), "text/html");
            }

            // Return the clean HTML with embedded images directly.
            // This HTML has been through SmartReader (Mozilla Readability) and HtmlArticleCleaner,
            // which strip scripts, styles, forms, and other non-article elements.
            var html = result.CleanHtmlEmbeddedImages ?? "<p>No content could be extracted.</p>";
            var title = HttpUtility.HtmlEncode(result.Title ?? "Distillate — Reader View");
            return Results.Content(LiveResultHtml(title, html, url, result.ProcessingTimeMs), "text/html");
        }
        finally
        {
            semaphore.Release();

            // Aggressive GC if configured
            if (options.IsAggressiveGc)
            {
                GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Forced, true, true);
            }
        }
    });
}
else
{
    logger.LogInformation("Starting in REST mode — JSON API");

    // Root info endpoint
    app.MapGet("/", () => Results.Ok(new
    {
        service = "Distillate",
        version = "v1.1",
        description = "Capacity-bound reader view extraction service",
        mode = "rest",
        endpoints = new
        {
            health = "GET /health",
            process = "POST /process"
        },
        example = new
        {
            url = "https://example.com/article",
            engine = "smartreader",
            includeMarkdownNoImages = true,
            includePlainText = true,
            includeCleanHtmlNoImages = false,
            includeCleanHtmlLinkedImages = false,
            includeCleanHtmlEmbeddedImages = false,
            includeMarkdownLinkedImages = false,
            includeSourceHtml = false,
            includeAllLinksInPage = false,
            includeAllLinksMedia = false,
            includeAllLinksAssets = false,
            includeAllInPageImages = false,
            includeAllOutOfPageImages = false,
            imageLinkRewrite = false,
            pngConversionRequired = false
        }
    }));

    // Main processing endpoint with honest backpressure
    app.MapPost("/process", async (ProcessRequest request, IDistillateService service, HttpContext context) =>
    {
        var traceId = context.Request.Headers["X-Trace-Id"].FirstOrDefault() ?? Guid.NewGuid().ToString("N");
        
        // Validate URL length
        if (request.Url.Length > options.MaxUrlLength)
        {
            return Results.BadRequest(new ProcessResponse
            {
                Success = false,
                RequestedUrl = request.Url,
                ProcessedAt = DateTimeOffset.UtcNow,
                ProcessingTimeMs = 0,
                CompletionFlags = 0,
                Errors = [$"URL exceeds maximum length of {options.MaxUrlLength} characters"],
                TraceId = traceId
            });
        }

        // Honest backpressure: immediate check, no waiting
        if (!semaphore.Wait(0))
        {
            logger.LogWarning("Request rejected due to capacity limit. TraceId: {TraceId}", traceId);
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        try
        {
            var result = await service.ProcessAsync(request, traceId, context.RequestAborted);
            return Results.Ok(result);
        }
        finally
        {
            semaphore.Release();

            // Aggressive GC if configured
            if (options.IsAggressiveGc)
            {
                GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Forced, true, true);
            }
        }
    });
}

// Configure port (only override if ASPNETCORE_URLS isn't set)
var urlsEnv = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
if (string.IsNullOrWhiteSpace(urlsEnv))
{
    var port = options.Port;
    app.Urls.Clear();
    app.Urls.Add($"http://0.0.0.0:{port}");
    logger.LogInformation("Distillate listening on port {Port}", port);
}
else
{
    logger.LogInformation("Distillate listening on {Urls}", urlsEnv);
}

app.Run();

// Helper methods
static int GetEnvInt(string name, int defaultValue)
{
    var value = Environment.GetEnvironmentVariable(name);
    return int.TryParse(value, out var result) ? result : defaultValue;
}

static long GetEnvLong(string name, long defaultValue)
{
    var value = Environment.GetEnvironmentVariable(name);
    return long.TryParse(value, out var result) ? result : defaultValue;
}

static string LiveModeHtml(string? errorMessage = null, string? userAgentValue = null, string? refererValue = null, string? cookiesValue = null, bool clearCookies = false, bool pngConvert = false)
{
    var errorDiv = errorMessage != null
        ? $"""<div class="error">{HttpUtility.HtmlEncode(errorMessage)}</div>"""
        : "";

    var userAgentAttr    = HtmlValueAttr(userAgentValue);
    var refererAttr      = HtmlValueAttr(refererValue);
    var cookiesContent   = HttpUtility.HtmlEncode(cookiesValue ?? "");
    var clearCookiesAttr = clearCookies ? " checked" : "";
    var pngConvertAttr   = pngConvert ? " checked" : "";

    return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>Distillate — Live Mode</title>
    <style>
        *, *::before, *::after { box-sizing: border-box; }
        body { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif; background: #1a1a2e; color: #e0e0e0; margin: 0; padding: 2rem; display: flex; justify-content: center; align-items: flex-start; min-height: 100vh; }
        .container { max-width: 680px; width: 100%; background: #16213e; border-radius: 12px; box-shadow: 0 4px 24px rgba(0,0,0,0.4); padding: 2rem; border: 1px solid #0f3460; }
        h1 { margin: 0 0 0.25rem; font-size: 1.6rem; color: #e94560; letter-spacing: 0.02em; }
        p.subtitle { margin: 0 0 1.5rem; color: #8899aa; font-size: 0.9rem; }
        form { display: flex; flex-direction: column; gap: 0.75rem; }
        .url-row { display: flex; gap: 0.5rem; }
        input[type="url"], input[type="text"], textarea { padding: 0.6rem 0.75rem; border: 1px solid #0f3460; border-radius: 6px; font-size: 1rem; width: 100%; background: #1a1a2e; color: #e0e0e0; transition: border-color 0.2s, box-shadow 0.2s; }
        textarea { font-family: "SF Mono", "Fira Code", "Fira Mono", Menlo, Consolas, monospace; font-size: 0.85rem; resize: vertical; }
        .url-row input[type="url"] { flex: 1; width: auto; }
        input[type="url"]:focus, input[type="text"]:focus, textarea:focus { outline: none; border-color: #e94560; box-shadow: 0 0 0 2px rgba(233,69,96,0.25); }
        input::placeholder, textarea::placeholder { color: #556677; }
        button { padding: 0.6rem 1.5rem; background: #e94560; color: #fff; border: none; border-radius: 6px; font-size: 1rem; font-weight: 600; cursor: pointer; white-space: nowrap; transition: background 0.2s, transform 0.1s; }
        button:hover { background: #c73652; }
        button:active { transform: scale(0.98); }
        label { font-size: 0.85rem; color: #8899aa; margin-bottom: 0.15rem; display: block; }
        .field { display: flex; flex-direction: column; }
        .checkbox-field { display: flex; align-items: center; gap: 0.5rem; }
        .checkbox-field label { margin-bottom: 0; }
        input[type="checkbox"] { accent-color: #e94560; width: 16px; height: 16px; }
        details { margin-top: 0.25rem; }
        summary { font-size: 0.85rem; color: #8899aa; cursor: pointer; user-select: none; padding: 0.25rem 0; }
        summary:hover { color: #e94560; }
        .optional-fields { display: flex; flex-direction: column; gap: 0.75rem; margin-top: 0.75rem; }
        .error { margin-top: 1rem; padding: 0.75rem; background: rgba(233,69,96,0.12); border: 1px solid rgba(233,69,96,0.35); border-radius: 6px; color: #f5a0b0; font-size: 0.9rem; }
        .curl-section { margin-top: 1.25rem; }
        .curl-section label { font-size: 0.85rem; color: #8899aa; margin-bottom: 0.35rem; display: flex; align-items: center; gap: 0.5rem; }
        .curl-section textarea { background: #111827; color: #9ca3af; border: 1px solid #0f3460; font-size: 0.8rem; line-height: 1.5; }
        .curl-section .copy-btn { background: transparent; border: 1px solid #0f3460; color: #8899aa; padding: 0.2rem 0.6rem; font-size: 0.75rem; border-radius: 4px; cursor: pointer; font-weight: 400; transition: border-color 0.2s, color 0.2s; }
        .curl-section .copy-btn:hover { border-color: #e94560; color: #e94560; }
        .separator { border: none; border-top: 1px solid #0f3460; margin: 0.5rem 0 0; }
    </style>
</head>
<body>
    <div class="container">
        <h1>Distillate</h1>
        <p class="subtitle">Enter a URL to extract its reader view with embedded images.</p>
        <form method="get" action="/live" id="distillateForm">
            <div class="url-row">
                <input type="url" id="url" name="url" placeholder="https://example.com/article" required autofocus />
                <button type="submit">Extract</button>
            </div>
            <details>
                <summary>Advanced options</summary>
                <div class="optional-fields">
                    <div class="field">
                        <label for="userAgent">User-Agent (leave blank for default)</label>
                        <input type="text" id="userAgent" name="userAgent" placeholder="Mozilla/5.0 …"{{userAgentAttr}}/>
                    </div>
                    <div class="field">
                        <label for="referer">Referer (leave blank for none)</label>
                        <input type="text" id="referer" name="referer" placeholder="https://example.com/"{{refererAttr}}/>
                    </div>
                    <div class="field">
                        <label for="cookies">Cookies (one per line: name=value)</label>
                        <textarea id="cookies" name="cookies" rows="3" placeholder="session=abc123&#10;token=xyz789">{{cookiesContent}}</textarea>
                    </div>
                    <hr class="separator" />
                    <div class="checkbox-field">
                        <input type="checkbox" id="clearCookies" name="clearCookies" value="on"{{clearCookiesAttr}}/>
                        <label for="clearCookies">Clear cookies (disable cookie storage for this request)</label>
                    </div>
                    <div class="checkbox-field">
                        <input type="checkbox" id="pngConvert" name="pngConvert" value="on"{{pngConvertAttr}}/>
                        <label for="pngConvert">Convert images to PNG</label>
                    </div>
                </div>
            </details>
        </form>
        {{errorDiv}}
        <div class="curl-section">
            <label for="curlOutput">Equivalent REST API curl command <button type="button" class="copy-btn" onclick="navigator.clipboard.writeText(document.getElementById('curlOutput').value)">Copy</button></label>
            <textarea id="curlOutput" rows="6" readonly></textarea>
        </div>
    </div>
    <script>
    (function() {
        function buildCurl() {
            var url = document.getElementById('url').value.trim();
            var ua  = document.getElementById('userAgent').value.trim();
            var ref = document.getElementById('referer').value.trim();
            var ck  = document.getElementById('cookies').value.trim();
            var cc  = document.getElementById('clearCookies').checked;
            var png = document.getElementById('pngConvert').checked;

            var body = { url: url || 'https://example.com/article', includeCleanHtmlEmbeddedImages: true, includePlainText: false, includeMarkdownNoImages: false };
            if (ua)  body.userAgent = ua;
            if (ref) body.referer = ref;
            if (cc)  body.clearCookies = true;
            if (png) body.pngConversionRequired = true;
            if (ck) {
                var cookies = ck.split('\n').map(function(line) {
                    var eq = line.indexOf('=');
                    if (eq <= 0) return null; // skip lines with no '=' or empty name
                    return { name: line.substring(0, eq).trim(), value: line.substring(eq + 1).trim() };
                }).filter(Boolean);
                if (cookies.length > 0) body.cookies = cookies;
            }

            var json = JSON.stringify(body, null, 2);
            var cmd = "curl -X POST http://localhost:8080/process \\\n  -H \"Content-Type: application/json\" \\\n  -d '" + json + "'";
            document.getElementById('curlOutput').value = cmd;
        }

        ['url','userAgent','referer','cookies'].forEach(function(id) {
            document.getElementById(id).addEventListener('input', buildCurl);
        });
        ['clearCookies','pngConvert'].forEach(function(id) {
            document.getElementById(id).addEventListener('change', buildCurl);
        });

        buildCurl();
    })();
    </script>
</body>
</html>
""";
}

static string LiveResultHtml(string title, string articleHtml, string sourceUrl, double processingTimeMs)
{
    return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>{{title}}</title>
    <style>
        *, *::before, *::after { box-sizing: border-box; }
        body { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif; background: #1a1a2e; color: #e0e0e0; margin: 0; padding: 0; }
        .toolbar { background: #16213e; border-bottom: 1px solid #0f3460; padding: 0.75rem 2rem; display: flex; align-items: center; gap: 1rem; flex-wrap: wrap; position: sticky; top: 0; z-index: 10; }
        .toolbar a { color: #e94560; text-decoration: none; font-size: 0.9rem; transition: color 0.2s; }
        .toolbar a:hover { color: #f5a0b0; text-decoration: underline; }
        .toolbar .meta { margin-left: auto; color: #556677; font-size: 0.8rem; }
        .article { max-width: 800px; margin: 2rem auto; background: #16213e; border-radius: 12px; box-shadow: 0 4px 24px rgba(0,0,0,0.4); padding: 2rem; border: 1px solid #0f3460; }
        .article img { max-width: 100%; height: auto; border-radius: 6px; }
        .article a { color: #e94560; }
        .article a:hover { color: #f5a0b0; }
    </style>
</head>
<body>
    <div class="toolbar">
        <a href="/">&#8592; New extraction</a>
        <a href="{{HttpUtility.HtmlAttributeEncode(sourceUrl)}}" target="_blank" rel="noopener">Original page &#8599;</a>
        <span class="meta">{{processingTimeMs:F0}} ms</span>
    </div>
    <div class="article">
        {{articleHtml}}
    </div>
</body>
</html>
""";
}

// Returns a ` value="..."` HTML attribute string for the given value, or an empty string when value is null.
static string HtmlValueAttr(string? value) =>
    value != null ? $""" value="{HttpUtility.HtmlAttributeEncode(value)}" """ : "";

// Expose Program class for testing
public partial class Program { }
