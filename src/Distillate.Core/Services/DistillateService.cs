using System.Diagnostics;
using System.Runtime;
using System.IO;
using Distillate.Core.Configuration;
using Distillate.Core.Conversion;
using Distillate.Core.Extraction;
using Distillate.Core.Images;
using Distillate.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace Distillate.Core.Services;

public class DistillateService : IDistillateService
{
    private readonly ILogger<DistillateService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly DistillateOptions _options;
    private readonly ReaderEngineFactory _engineFactory;
    private readonly MarkdownConverter _markdownConverter;
    private readonly IHttpClientFactory _httpClientFactory;

    public DistillateService(
        ILogger<DistillateService> logger,
        ILoggerFactory loggerFactory,
        IOptions<DistillateOptions> options,
        ReaderEngineFactory engineFactory,
        MarkdownConverter markdownConverter,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _options = options.Value;
        _engineFactory = engineFactory;
        _markdownConverter = markdownConverter;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<ProcessResponse> ProcessAsync(ProcessRequest request, string traceId, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var errors = new List<string>();
        var completionFlags = CompletionFlag.Complete;

        var effectiveTimeout = request.TimeoutMs ?? _options.MaxTimeMs;
        effectiveTimeout = Math.Min(effectiveTimeout, _options.MaxTimeMs);

        var effectiveStabilization = request.StabilizationMs ?? _options.StabilizationMs;
        var effectiveUserAgent = request.UserAgent ?? _options.UserAgent;

        using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri))
                return CreateErrorResponse(request.Url, stopwatch.Elapsed.TotalMilliseconds, traceId, "Invalid URL format");

            if (uri.Scheme != "http" && uri.Scheme != "https")
                return CreateErrorResponse(request.Url, stopwatch.Elapsed.TotalMilliseconds, traceId, "Only HTTP and HTTPS URLs are supported");

            _logger.LogInformation("Processing URL: {Url} with engine: {Engine}, traceId: {TraceId}",
                request.Url, request.Engine, traceId);

            var sourceHtml = await FetchWithBrowserAsync(uri, effectiveUserAgent, effectiveStabilization, request.Cookies, linkedCts.Token);

            if (string.IsNullOrEmpty(sourceHtml))
                return CreateErrorResponse(request.Url, stopwatch.Elapsed.TotalMilliseconds, traceId, "Failed to fetch page content");

            // --- Reader extraction ---
            var engine = _engineFactory.GetEngine(request.Engine);
            var extraction = engine.Extract(sourceHtml, uri);

            if (!extraction.Success)
            {
                _logger.LogWarning("Primary engine {Engine} failed, trying fallback", engine.Name);
                var fallbackEngine = _engineFactory.GetFallbackEngine(engine.Name);
                extraction = fallbackEngine.Extract(sourceHtml, uri);

                if (extraction.Success)
                    completionFlags |= CompletionFlag.EngineFallbackUsed;
                else
                    errors.Add($"Extraction failed: {extraction.Error}");
            }

            var baseCleanHtml = extraction.CleanHtml ?? string.Empty;

            // --- Link extraction (from source HTML) ---
            string[]? allLinksInPage = null;
            string[]? allLinksMedia = null;
            string[]? allLinksAssets = null;

            if (request.IncludeAllLinksInPage || request.IncludeAllLinksMedia || request.IncludeAllLinksAssets)
            {
                var linkResult = LinkExtractor.Extract(sourceHtml, uri);
                if (request.IncludeAllLinksInPage) allLinksInPage = linkResult.LinksInPage;
                if (request.IncludeAllLinksMedia)  allLinksMedia  = linkResult.LinksMedia;
                if (request.IncludeAllLinksAssets) allLinksAssets = linkResult.LinksAssets;
            }

            // --- Resolve imageLinkRewrite implicit flag ---
            bool effectiveIncludeInPage = request.IncludeAllInPageImages;
            if (request.ImageLinkRewrite && !request.IncludeAllInPageImages)
            {
                effectiveIncludeInPage = true;
                completionFlags |= CompletionFlag.ImageLinkRewriteImpliedInPageImages;
            }

            if (request.ImageLinkRewrite)
                completionFlags |= CompletionFlag.ImageLinkRewriteApplied;

            // --- Image processing ---
            string? cleanHtmlEmbedded = null;
            string? cleanHtmlLinked = null;
            string? cleanHtmlNoImages = null;
            ImageEntry[]? allInPageImages = null;
            ImageEntry[]? allOutOfPageImages = null;

            bool needAnyImageWork = extraction.Success && !string.IsNullOrEmpty(baseCleanHtml) &&
                (request.IncludeCleanHtmlEmbeddedImages || request.IncludeCleanHtmlLinkedImages ||
                 request.IncludeCleanHtmlNoImages || effectiveIncludeInPage || request.IncludeAllOutOfPageImages);

            if (needAnyImageWork)
            {
                try
                {
                    var httpClient = _httpClientFactory.CreateClient("ImageDownloader");
                    var processor = new ImageProcessor(
                        _loggerFactory.CreateLogger<ImageProcessor>(),
                        httpClient,
                        _options.MaxImageBytes);

                    var imgResult = await processor.ProcessAsync(
                        sourceHtml,
                        baseCleanHtml,
                        uri,
                        needEmbedded:        request.IncludeCleanHtmlEmbeddedImages,
                        needLinked:          request.IncludeCleanHtmlLinkedImages,
                        needNoImages:        request.IncludeCleanHtmlNoImages,
                        needInPageImages:    effectiveIncludeInPage,
                        needOutOfPageImages: request.IncludeAllOutOfPageImages,
                        imageLinkRewrite:    request.ImageLinkRewrite,
                        pngConversionRequired: request.PngConversionRequired,
                        linkedCts.Token);

                    cleanHtmlEmbedded    = imgResult.CleanHtmlEmbeddedImages;
                    cleanHtmlLinked      = imgResult.CleanHtmlLinkedImages;
                    cleanHtmlNoImages    = imgResult.CleanHtmlNoImages;
                    allInPageImages      = imgResult.AllInPageImages;
                    allOutOfPageImages   = imgResult.AllOutOfPageImages;

                    if (imgResult.ImageTruncated)            completionFlags |= CompletionFlag.ImageTruncated;
                    if (imgResult.AllInPageImagesPartial)    completionFlags |= CompletionFlag.AllInPageImagesPartial;
                    if (imgResult.AllOutOfPageImagesPartial) completionFlags |= CompletionFlag.AllOutOfPageImagesPartial;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Image processing failed");
                    errors.Add($"Image processing failed: {ex.Message}");
                }
            }
            else if (request.IncludeCleanHtmlNoImages && extraction.Success)
            {
                // No image work needed but NoImages variant was requested and there was nothing to process
                cleanHtmlNoImages = baseCleanHtml;
            }

            // --- Markdown variants ---
            string? markdownNoImages = null;
            string? markdownLinked = null;

            if (request.IncludeMarkdownNoImages && extraction.Success)
            {
                var source = cleanHtmlNoImages ?? (needAnyImageWork ? null : baseCleanHtml);
                markdownNoImages = _markdownConverter.ConvertToMarkdown(source ?? baseCleanHtml);
            }

            if (request.IncludeMarkdownLinkedImages && extraction.Success)
            {
                markdownLinked = _markdownConverter.ConvertToMarkdown(cleanHtmlLinked ?? baseCleanHtml);
            }

            // --- Plain text ---
            string? plainText = request.IncludePlainText ? extraction.RawText : null;

            // --- Source HTML ---
            string? responseSourceHtml = null;
            if (request.IncludeSourceHtml)
                responseSourceHtml = sourceHtml;
            else
                completionFlags |= CompletionFlag.SourceHtmlOmitted;

            stopwatch.Stop();

            return new ProcessResponse
            {
                Success = extraction.Success,
                RequestedUrl = request.Url,
                ProcessedAt = DateTimeOffset.UtcNow,
                ProcessingTimeMs = stopwatch.Elapsed.TotalMilliseconds,
                Title = extraction.Title,
                CompletionFlags = (int)completionFlags,
                Errors = errors.Count > 0 ? errors.ToArray() : null,
                TraceId = traceId,
                PlainText = plainText,
                SourceHtml = responseSourceHtml,
                CleanHtmlEmbeddedImages = cleanHtmlEmbedded,
                CleanHtmlLinkedImages = cleanHtmlLinked,
                CleanHtmlNoImages = cleanHtmlNoImages,
                MarkdownLinkedImages = markdownLinked,
                MarkdownNoImages = markdownNoImages,
                AllLinksInPage = allLinksInPage,
                AllLinksMedia = allLinksMedia,
                AllLinksAssets = allLinksAssets,
                AllInPageImages = allInPageImages,
                AllOutOfPageImages = allOutOfPageImages
            };
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            _logger.LogWarning("Request timed out after {Timeout}ms", effectiveTimeout);
            return CreateErrorResponse(request.Url, stopwatch.Elapsed.TotalMilliseconds, traceId, $"Request timed out after {effectiveTimeout}ms");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Processing failed for {Url}", request.Url);
            return CreateErrorResponse(request.Url, stopwatch.Elapsed.TotalMilliseconds, traceId, ex.Message);
        }
        finally
        {
            if (_options.IsAggressiveGc)
                PerformAggressiveGc();
        }
    }

    private async Task<string?> FetchWithBrowserAsync(Uri uri, string userAgent, int stabilizationMs, CookieEntry[]? cookies, CancellationToken cancellationToken)
    {
        IPlaywright? playwright = null;
        IBrowser? browser = null;
        IBrowserContext? context = null;
        IPage? page = null;

        try
        {
            playwright = await Playwright.CreateAsync();

            var launchOptions = new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args = GetChromiumArgs()
            };

            if (!string.IsNullOrEmpty(_options.ChromiumPath) && File.Exists(_options.ChromiumPath))
            {
                launchOptions.ExecutablePath = _options.ChromiumPath;
                _logger.LogDebug("Using chromium executable: {Path}", _options.ChromiumPath);
            }
            else
            {
                _logger.LogDebug("Using Playwright bundled Chromium browser");
            }

            browser = await playwright.Chromium.LaunchAsync(launchOptions);

            var contextOptions = new BrowserNewContextOptions
            {
                UserAgent = userAgent,
                IgnoreHTTPSErrors = true
            };

            context = await browser.NewContextAsync(contextOptions);

            if (cookies != null && cookies.Length > 0)
            {
                var playwrightCookies = cookies.Select(c => new Cookie
                {
                    Name = c.Name,
                    Value = c.Value,
                    Domain = c.Domain ?? uri.Host,
                    Path = c.Path ?? "/"
                }).ToArray();

                await context.AddCookiesAsync(playwrightCookies);
            }

            page = await context.NewPageAsync();

            var response = await page.GotoAsync(uri.AbsoluteUri, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = _options.MaxTimeMs
            });

            if (response == null || !response.Ok)
                _logger.LogWarning("Page load failed with status: {Status}", response?.Status);

            await ScrollToBottomAsync(page, cancellationToken);
            await Task.Delay(stabilizationMs, cancellationToken);

            return await page.ContentAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Browser fetch failed for {Url}", uri);
            throw;
        }
        finally
        {
            if (page != null) await page.CloseAsync();
            if (context != null) await context.CloseAsync();
            if (browser != null) await browser.CloseAsync();
            playwright?.Dispose();
        }
    }

    private string[] GetChromiumArgs()
    {
        var args = new List<string>
        {
            "--no-sandbox",
            "--disable-dev-shm-usage",
            "--disable-gpu",
            $"--js-flags=--max-old-space-size={_options.MaxV8HeapSizeMb}"
        };

        if (!string.IsNullOrEmpty(_options.ChromiumArgs))
            args.AddRange(_options.ChromiumArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        return args.ToArray();
    }

    private async Task ScrollToBottomAsync(IPage page, CancellationToken cancellationToken)
    {
        try
        {
            var scrollScript = @"
                async () => {
                    const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
                    const scrollHeight = document.body.scrollHeight;
                    const viewportHeight = window.innerHeight;
                    let currentPosition = 0;
                    while (currentPosition < scrollHeight) {
                        currentPosition += viewportHeight;
                        window.scrollTo(0, currentPosition);
                        await delay(" + _options.ScrollDelayMs + @");
                    }
                    window.scrollTo(0, 0);
                }
            ";
            await page.EvaluateAsync(scrollScript);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Scroll script failed, continuing anyway");
        }
    }

    private void PerformAggressiveGc()
    {
        _logger.LogDebug("Performing aggressive GC");
        GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Forced, true, true);
    }

    private static ProcessResponse CreateErrorResponse(string url, double processingTimeMs, string traceId, string error)
    {
        return new ProcessResponse
        {
            Success = false,
            RequestedUrl = url,
            ProcessedAt = DateTimeOffset.UtcNow,
            ProcessingTimeMs = processingTimeMs,
            CompletionFlags = 0,
            Errors = [error],
            TraceId = traceId
        };
    }
}


