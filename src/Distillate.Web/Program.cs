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
        // HTML checkbox sends "on" when checked; absent means unchecked
        var clearCookiesParam = context.Request.Query["clearCookies"].ToString();
        var clearCookies = string.Equals(clearCookiesParam, "on", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(clearCookiesParam, "true", StringComparison.OrdinalIgnoreCase);

        // Use null (fall back to default) when the field is empty
        var effectiveUserAgent = string.IsNullOrEmpty(userAgentParam) ? null : userAgentParam;
        var effectiveReferer   = string.IsNullOrEmpty(refererParam)   ? null : refererParam;

        if (string.IsNullOrEmpty(url))
        {
            return Results.Content(LiveModeHtml("Please enter a URL.", userAgentParam, refererParam, clearCookies), "text/html");
        }

        if (url.Length > options.MaxUrlLength)
        {
            return Results.Content(LiveModeHtml($"URL exceeds maximum length of {options.MaxUrlLength} characters.", userAgentParam, refererParam, clearCookies), "text/html");
        }

        var traceId = Guid.NewGuid().ToString("N");

        // Honest backpressure: immediate check, no waiting
        if (!semaphore.Wait(0))
        {
            logger.LogWarning("Live request rejected due to capacity limit. TraceId: {TraceId}", traceId);
            return Results.Content(LiveModeHtml("Service is at capacity. Please try again shortly.", userAgentParam, refererParam, clearCookies), "text/html");
        }

        try
        {
            var request = new ProcessRequest
            {
                Url = url,
                UserAgent = effectiveUserAgent,
                Referer = effectiveReferer,
                ClearCookies = clearCookies,
                IncludeCleanHtmlEmbeddedImages = true,
                IncludeMarkdownNoImages = false,
                IncludePlainText = false
            };

            var result = await service.ProcessAsync(request, traceId, context.RequestAborted);

            if (!result.Success)
            {
                var errorMsg = result.Errors != null ? string.Join("; ", result.Errors) : "Processing failed.";
                return Results.Content(LiveModeHtml(errorMsg, userAgentParam, refererParam, clearCookies), "text/html");
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

static string LiveModeHtml(string? errorMessage = null, string? userAgentValue = null, string? refererValue = null, bool clearCookies = false)
{
    var errorDiv = errorMessage != null
        ? $"""<div class="error">{HttpUtility.HtmlEncode(errorMessage)}</div>"""
        : "";

    var userAgentAttr    = HtmlValueAttr(userAgentValue);
    var refererAttr      = HtmlValueAttr(refererValue);
    var clearCookiesAttr = clearCookies ? " checked" : "";

    return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>Distillate — Live Mode</title>
    <style>
        *, *::before, *::after { box-sizing: border-box; }
        body { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif; background: #f5f5f5; color: #333; margin: 0; padding: 2rem; display: flex; justify-content: center; align-items: flex-start; min-height: 100vh; }
        .container { max-width: 600px; width: 100%; background: #fff; border-radius: 8px; box-shadow: 0 2px 8px rgba(0,0,0,0.1); padding: 2rem; }
        h1 { margin: 0 0 0.25rem; font-size: 1.5rem; }
        p.subtitle { margin: 0 0 1.5rem; color: #666; font-size: 0.9rem; }
        form { display: flex; flex-direction: column; gap: 0.75rem; }
        .url-row { display: flex; gap: 0.5rem; }
        input[type="url"], input[type="text"] { padding: 0.6rem 0.75rem; border: 1px solid #ccc; border-radius: 4px; font-size: 1rem; width: 100%; }
        .url-row input[type="url"] { flex: 1; width: auto; }
        input[type="url"]:focus, input[type="text"]:focus { outline: none; border-color: #0066cc; box-shadow: 0 0 0 2px rgba(0,102,204,0.2); }
        button { padding: 0.6rem 1.25rem; background: #0066cc; color: #fff; border: none; border-radius: 4px; font-size: 1rem; cursor: pointer; white-space: nowrap; }
        button:hover { background: #0052a3; }
        label { font-size: 0.85rem; color: #555; margin-bottom: 0.15rem; display: block; }
        .field { display: flex; flex-direction: column; }
        .checkbox-field { display: flex; align-items: center; gap: 0.5rem; }
        .checkbox-field label { margin-bottom: 0; }
        details { margin-top: 0.25rem; }
        summary { font-size: 0.85rem; color: #666; cursor: pointer; user-select: none; }
        summary:hover { color: #0066cc; }
        .optional-fields { display: flex; flex-direction: column; gap: 0.75rem; margin-top: 0.75rem; }
        .error { margin-top: 1rem; padding: 0.75rem; background: #fef2f2; border: 1px solid #fecaca; border-radius: 4px; color: #b91c1c; font-size: 0.9rem; }
    </style>
</head>
<body>
    <div class="container">
        <h1>Distillate</h1>
        <p class="subtitle">Enter a URL to extract its reader view with embedded images.</p>
        <form method="get" action="/live">
            <div class="url-row">
                <input type="url" name="url" placeholder="https://example.com/article" required autofocus />
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
                    <div class="checkbox-field">
                        <input type="checkbox" id="clearCookies" name="clearCookies" value="on"{{clearCookiesAttr}}/>
                        <label for="clearCookies">Clear cookies (disable cookie storage for this request)</label>
                    </div>
                </div>
            </details>
        </form>
        {{errorDiv}}
    </div>
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
        body { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif; background: #f5f5f5; color: #333; margin: 0; padding: 0; }
        .toolbar { background: #fff; border-bottom: 1px solid #e0e0e0; padding: 0.75rem 2rem; display: flex; align-items: center; gap: 1rem; flex-wrap: wrap; position: sticky; top: 0; z-index: 10; }
        .toolbar a { color: #0066cc; text-decoration: none; font-size: 0.9rem; }
        .toolbar a:hover { text-decoration: underline; }
        .toolbar .meta { margin-left: auto; color: #999; font-size: 0.8rem; }
        .article { max-width: 800px; margin: 2rem auto; background: #fff; border-radius: 8px; box-shadow: 0 2px 8px rgba(0,0,0,0.1); padding: 2rem; }
        .article img { max-width: 100%; height: auto; border-radius: 4px; }
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
