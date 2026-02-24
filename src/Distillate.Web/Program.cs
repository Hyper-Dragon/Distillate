using System.Runtime;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    options.ChromiumPath = Environment.GetEnvironmentVariable("CHROMIUM_PATH") ?? "/usr/bin/chromium-browser";
    options.ChromiumArgs = Environment.GetEnvironmentVariable("CHROMIUM_ARGS");
    options.Port = GetEnvInt("PORT", 8080);
    options.LogLevel = Environment.GetEnvironmentVariable("LOG_LEVEL") ?? "Information";
    options.ScrollDelayMs = GetEnvInt("SCROLL_DELAY_MS", 100);
    options.MaxV8HeapSizeMb = GetEnvInt("MAX_V8_HEAP_SIZE_MB", 512);
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

// Configure HttpClient for image downloading
builder.Services.AddHttpClient("ImageDownloader", (sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<DistillateOptions>>().Value;
    client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Enable Server GC
GCSettings.LatencyMode = GCLatencyMode.Batch;

var app = builder.Build();

// Get options for semaphore setup
var options = app.Services.GetRequiredService<IOptions<DistillateOptions>>().Value;
var semaphore = new SemaphoreSlim(options.Concurrency, options.Concurrency);
var logger = app.Services.GetRequiredService<ILogger<Program>>();

logger.LogInformation("Distillate starting with Concurrency={Concurrency}, MaxTimeMs={MaxTimeMs}, MaxImageBytes={MaxImageBytes}, GcMode={GcMode}",
    options.Concurrency, options.MaxTimeMs, options.MaxImageBytes, options.GcMode);

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTimeOffset.UtcNow,
    concurrency = new
    {
        max = options.Concurrency,
        available = semaphore.CurrentCount
    }
}));

// Root info endpoint
app.MapGet("/", () => Results.Ok(new
{
    service = "Distillate",
    version = "v1.1",
    description = "Capacity-bound reader view extraction service",
    endpoints = new
    {
        health = "GET /health",
        process = "POST /process"
    },
    example = new
    {
        url = "https://example.com/article",
        engine = "readability",
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

// Expose Program class for testing
public partial class Program { }
