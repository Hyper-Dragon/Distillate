using System.Runtime;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using Distillate.Core.Configuration;
using Distillate.Core.Conversion;
using Distillate.Core.Extraction;
using Distillate.Core.Models;
using Distillate.Core.Services;
using Distillate.Web;
using Microsoft.Extensions.Options;

// `--healthcheck` exists so the container can report its own health with nothing
// from the runtime: no shell, no curl, no wget. It has to run before the web host
// is built, and it must terminate rather than fall through to serving traffic.
if (args.Contains("--healthcheck"))
{
    Environment.Exit(RunHealthCheck());
}

var builder = WebApplication.CreateBuilder(args);

// Configuration that a typo would otherwise survive. MODE=Live and MODE=live
// used to be the same thing and MODE=Livee silently ran the REST API; LOG_LEVEL
// used to die inside Enum.Parse with a stack trace instead of naming the value.
var mode = ReadMode();
var logLevel = ReadLogLevel();

// Configure options from environment variables
builder.Services.Configure<DistillateOptions>(options =>
{
    options.Concurrency = GetEnvInt("CONCURRENCY", 1);
    options.MaxTimeMs = GetEnvInt("MAX_TIME_MS", 180_000);
    options.MaxImageBytes = GetEnvLong("MAX_IMAGE_BYTES", 200 * 1024 * 1024);
    options.GcMode = Environment.GetEnvironmentVariable("GC_MODE") ?? "normal";
    options.StabilizationMs = GetEnvInt("STABILIZATION_MS", 10_000);
    options.MaxUrlLength = GetEnvInt("MAX_URL_LENGTH", 2048);
    options.BusyRetryAfterSeconds = GetEnvInt("BUSY_RETRY_AFTER_SECONDS", 15);
    options.UserAgent = Environment.GetEnvironmentVariable("USER_AGENT")
        ?? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
    options.ChromiumPath = Environment.GetEnvironmentVariable("CHROMIUM_PATH") ?? "/usr/bin/chromium";
    options.ChromiumArgs = Environment.GetEnvironmentVariable("CHROMIUM_ARGS");
    options.Port = GetEnvInt("PORT", 8080);
    options.LogLevel = logLevel.ToString();
    options.ScrollDelayMs = GetEnvInt("SCROLL_DELAY_MS", 100);
    options.MaxV8HeapSizeMb = GetEnvInt("MAX_V8_HEAP_SIZE_MB", 512);
    options.Mode = mode;
});

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(logLevel);


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

Log.Starting(logger, BuildInfo.Full, options.Concurrency, options.MaxTimeMs, options.MaxImageBytes, options.GcMode, options.Mode);

// Health check endpoint (available in both modes). The version and build are in
// the payload on purpose: it is the one endpoint every deployment can reach, so a
// bug report that includes it names the exact binary that produced it.
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    version = BuildInfo.Version,
    build = BuildInfo.Commit,
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
    app.MapGet("/", () => Results.Content(LiveModePages.Index(), "text/html"));

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
        var effectiveReferer = string.IsNullOrEmpty(refererParam) ? null : refererParam;

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
            return Results.Content(LiveModePages.Index("Please enter a URL.", userAgentParam, refererParam, cookiesParam, clearCookies, pngConvert), "text/html");
        }

        if (url.Length > options.MaxUrlLength)
        {
            return Results.Content(LiveModePages.Index($"URL exceeds maximum length of {options.MaxUrlLength} characters.", userAgentParam, refererParam, cookiesParam, clearCookies, pngConvert), "text/html");
        }

        var traceId = Guid.NewGuid().ToString("N");

        // Honest backpressure: immediate check, no waiting
        if (!semaphore.Wait(0))
        {
            logger.LogWarning("Live request rejected due to capacity limit. TraceId: {TraceId}", traceId);
            return Results.Content(LiveModePages.Index("Service is at capacity. Please try again shortly.", userAgentParam, refererParam, cookiesParam, clearCookies, pngConvert), "text/html");
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
                return Results.Content(LiveModePages.Index(errorMsg, userAgentParam, refererParam, cookiesParam, clearCookies, pngConvert), "text/html");
            }

            // Return the clean HTML with embedded images directly.
            // This HTML has been through SmartReader (Mozilla Readability) and HtmlArticleCleaner,
            // which strip scripts, styles, forms, and other non-article elements.
            var html = result.CleanHtmlEmbeddedImages ?? "<p>No content could be extracted.</p>";
            var title = HttpUtility.HtmlEncode(result.Title ?? "Distillate — Reader View");
            return Results.Content(LiveModePages.Result(title, html, url, result.ProcessingTimeMs), "text/html");
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

    // Live events endpoint — Server-Sent Events stream for real-time processing log
    app.MapGet("/live/events", async (HttpContext context, IDistillateService service) =>
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers["X-Accel-Buffering"] = "no";

        async Task SendSseEvent(string eventType, string data)
        {
            var sanitized = data.Replace("\n", " ").Replace("\r", "");
            await context.Response.WriteAsync($"event: {eventType}\ndata: {sanitized}\n\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
        }

        try
        {
            await SendSseEvent("log", $"[{DateTime.UtcNow:HH:mm:ss.fff}] Validating parameters...");

            var url = context.Request.Query["url"].ToString().Trim();
            var userAgentParam = context.Request.Query["userAgent"].ToString().Trim();
            var refererParam = context.Request.Query["referer"].ToString().Trim();
            var cookiesParam = context.Request.Query["cookies"].ToString().Trim();
            var clearCookiesParam = context.Request.Query["clearCookies"].ToString();
            var clearCookies = string.Equals(clearCookiesParam, "on", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(clearCookiesParam, "true", StringComparison.OrdinalIgnoreCase);
            var pngConvertParam = context.Request.Query["pngConvert"].ToString();
            var pngConvert = string.Equals(pngConvertParam, "on", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(pngConvertParam, "true", StringComparison.OrdinalIgnoreCase);

            var effectiveUserAgent = string.IsNullOrEmpty(userAgentParam) ? null : userAgentParam;
            var effectiveReferer = string.IsNullOrEmpty(refererParam) ? null : refererParam;

            CookieEntry[]? parsedCookies = null;
            if (!string.IsNullOrWhiteSpace(cookiesParam))
            {
                var entries = cookiesParam
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(line =>
                    {
                        var eqIndex = line.IndexOf('=');
                        if (eqIndex <= 0) return null;
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
                await SendSseEvent("log", $"[{DateTime.UtcNow:HH:mm:ss.fff}] ERROR: No URL provided");
                await SendSseEvent("fail", "Please enter a URL.");
                return;
            }

            if (url.Length > options.MaxUrlLength)
            {
                await SendSseEvent("log", $"[{DateTime.UtcNow:HH:mm:ss.fff}] ERROR: URL exceeds maximum length of {options.MaxUrlLength} characters");
                await SendSseEvent("fail", $"URL exceeds maximum length of {options.MaxUrlLength} characters.");
                return;
            }

            await SendSseEvent("log", $"[{DateTime.UtcNow:HH:mm:ss.fff}] URL validated: {url}");

            var traceId = Guid.NewGuid().ToString("N");
            await SendSseEvent("log", $"[{DateTime.UtcNow:HH:mm:ss.fff}] TraceId: {traceId}");
            await SendSseEvent("log", $"[{DateTime.UtcNow:HH:mm:ss.fff}] Acquiring processing slot...");

            if (!semaphore.Wait(0))
            {
                logger.LogWarning("Live SSE request rejected due to capacity limit. TraceId: {TraceId}", traceId);
                await SendSseEvent("log", $"[{DateTime.UtcNow:HH:mm:ss.fff}] ERROR: Service at capacity");
                await SendSseEvent("fail", "Service is at capacity. Please try again shortly.");
                return;
            }

            try
            {
                await SendSseEvent("log", $"[{DateTime.UtcNow:HH:mm:ss.fff}] Processing slot acquired");
                await SendSseEvent("log", $"[{DateTime.UtcNow:HH:mm:ss.fff}] Starting extraction...");

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
                    await SendSseEvent("log", $"[{DateTime.UtcNow:HH:mm:ss.fff}] ERROR: {errorMsg}");
                    await SendSseEvent("fail", errorMsg);
                }
                else
                {
                    await SendSseEvent("log", $"[{DateTime.UtcNow:HH:mm:ss.fff}] Extraction complete ({result.ProcessingTimeMs:F0}ms)");

                    var html = result.CleanHtmlEmbeddedImages ?? "<p>No content could be extracted.</p>";
                    var title = HttpUtility.HtmlEncode(result.Title ?? "Distillate — Reader View");
                    var resultHtml = LiveModePages.Result(title, html, url, result.ProcessingTimeMs);
                    var base64Html = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(resultHtml));
                    await SendSseEvent("result", base64Html);
                }
            }
            finally
            {
                semaphore.Release();

                if (options.IsAggressiveGc)
                {
                    GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect(2, GCCollectionMode.Forced, true, true);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — ignore
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
        version = BuildInfo.Version,
        build = BuildInfo.Commit,
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

        // Honest backpressure: immediate check, no waiting. The refusal carries a body and a Retry-After
        // so the caller can tell "busy, come back shortly" from "unreachable" — those need different
        // handling, and the second one is silent when this container is killed rather than answering.
        if (!semaphore.Wait(0))
        {
            logger.LogWarning("Request rejected due to capacity limit. TraceId: {TraceId}", traceId);

            context.Response.Headers.RetryAfter = options.BusyRetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);

            return Results.Json(new ProcessResponse
            {
                Success = false,
                RequestedUrl = request.Url,
                ProcessedAt = DateTimeOffset.UtcNow,
                ProcessingTimeMs = 0,
                CompletionFlags = 0,
                Errors = [$"Service at capacity (concurrency {options.Concurrency}); retry after {options.BusyRetryAfterSeconds}s"],
                TraceId = traceId
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
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
    Log.ListeningOnPort(logger, port);
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

// Configuration errors name the variable, the value it got and the values it will
// accept. Anything less ("invalid config", or a stack trace out of Enum.Parse) is
// a support ticket rather than a fix.
static string ReadMode()
{
    var raw = Environment.GetEnvironmentVariable("MODE");
    if (string.IsNullOrWhiteSpace(raw))
        return "rest";

    var value = raw.Trim().ToLowerInvariant();
    if (value is "rest" or "live")
        return value;

    Fail($"MODE must be one of: rest, live — got '{raw}'.");
    return "rest"; // unreachable; Fail terminates the process.
}

static LogLevel ReadLogLevel()
{
    var raw = Environment.GetEnvironmentVariable("LOG_LEVEL");
    if (string.IsNullOrWhiteSpace(raw))
        return LogLevel.Information;

    if (Enum.TryParse<LogLevel>(raw.Trim(), ignoreCase: true, out var level) && Enum.IsDefined(level))
        return level;

    Fail($"LOG_LEVEL must be one of: {string.Join(", ", Enum.GetNames<LogLevel>())} — got '{raw}'.");
    return LogLevel.Information; // unreachable; Fail terminates the process.
}

static void Fail(string message)
{
    Console.Error.WriteLine($"distillate: configuration error: {message}");
    Environment.Exit(2);
}

// The container's own health probe. Deliberately independent of the runtime: it
// speaks plain HTTP with HttpClient, so it works on an image with no shell and no
// curl. It asserts the marker in the response body rather than the status code,
// because any 200 is not necessarily this service's 200.
static int RunHealthCheck()
{
    var port = GetEnvInt("PORT", 8080);

    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var response = http.GetAsync($"http://127.0.0.1:{port}/health").GetAwaiter().GetResult();
        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

        if (!response.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"unhealthy: HTTP {(int)response.StatusCode} from /health — {body}");
            return 1;
        }

        if (!body.Contains("\"status\":\"healthy\"", StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"unhealthy: /health answered, but not from this service — {body}");
            return 1;
        }

        Console.WriteLine("healthy");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"unhealthy: {ex.GetType().Name}: {ex.Message}");
        return 1;
    }
}

internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Distillate {Version} starting with Concurrency={Concurrency}, MaxTimeMs={MaxTimeMs}, MaxImageBytes={MaxImageBytes}, GcMode={GcMode}, Mode={Mode}")]
    public static partial void Starting(ILogger logger, string version, int concurrency, int maxTimeMs, long maxImageBytes, string gcMode, string mode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Distillate listening on port {Port}")]
    public static partial void ListeningOnPort(ILogger logger, int port);
}
