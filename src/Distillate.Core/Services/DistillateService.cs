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
    // Injected into every page when clearCookies=true to make JS cookie APIs appear disabled
    private const string DisableCookiesScript = @"
        Object.defineProperty(navigator, 'cookieEnabled', { get: () => false, configurable: true });
        Object.defineProperty(document, 'cookie', {
            get: () => '',
            set: () => {},
            configurable: true
        });
    ";

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

        // Set request-derived flags upfront so they are present even on early-exit error responses
        if (!request.IncludeSourceHtml)
            completionFlags |= CompletionFlag.SourceHtmlOmitted;

        var effectiveTimeout = request.TimeoutMs ?? _options.MaxTimeMs;
        effectiveTimeout = Math.Min(effectiveTimeout, _options.MaxTimeMs);

        var effectiveStabilization = request.StabilizationMs ?? _options.StabilizationMs;
        var effectiveUserAgent = request.UserAgent ?? _options.UserAgent;

        using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        List<StageTiming>? stages = request.IncludeDebugInfo ? new() : null;
        List<ImageFetchLog>? imageFetchLog = request.IncludeDebugInfo ? new() : null;
        int debugSourceHtmlBytes = 0;
        int debugCleanHtmlBytes = 0;

        try
        {
            if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri))
                return CreateErrorResponse(request.Url, stopwatch.Elapsed.TotalMilliseconds, traceId, completionFlags, "Invalid URL format");

            if (uri.Scheme != "http" && uri.Scheme != "https")
                return CreateErrorResponse(request.Url, stopwatch.Elapsed.TotalMilliseconds, traceId, completionFlags, "Only HTTP and HTTPS URLs are supported");

            _logger.LogInformation("Processing URL: {Url} with engine: {Engine}, traceId: {TraceId}",
                request.Url, request.Engine, traceId);

            var browserTimer = Stopwatch.StartNew();
            var sourceHtml = await FetchWithBrowserAsync(uri, effectiveUserAgent, effectiveStabilization, request.Cookies, request.Referer, request.ClearCookies, linkedCts.Token);
            browserTimer.Stop();
            stages?.Add(new StageTiming { Stage = "BrowserFetch", Ms = browserTimer.Elapsed.TotalMilliseconds });
            debugSourceHtmlBytes = sourceHtml?.Length ?? 0;

            if (string.IsNullOrEmpty(sourceHtml))
                return CreateErrorResponse(request.Url, stopwatch.Elapsed.TotalMilliseconds, traceId, completionFlags, "Failed to fetch page content");

            // --- Reader extraction (SmartReader — Mozilla Readability port) ---
            var extractionTimer = Stopwatch.StartNew();
            var engine = _engineFactory.GetEngine();
            var extraction = engine.Extract(sourceHtml, uri);
            extractionTimer.Stop();
            stages?.Add(new StageTiming { Stage = "Extraction", Ms = extractionTimer.Elapsed.TotalMilliseconds });

            if (!extraction.Success)
                errors.Add($"Extraction failed: {extraction.Error}");

            var baseCleanHtml = extraction.CleanHtml ?? string.Empty;

            // --- DOM-level article cleaning (structural, not regex) ---
            // This is the key quality step: clean the HTML using semantic/structural
            // rules before producing any downstream output. Like Firefox Reader View.
            var cleaningTimer = Stopwatch.StartNew();
            var articleHtml = string.IsNullOrEmpty(baseCleanHtml)
                ? baseCleanHtml
                : HtmlArticleCleaner.Clean(baseCleanHtml);
            cleaningTimer.Stop();
            stages?.Add(new StageTiming { Stage = "HtmlCleaning", Ms = cleaningTimer.Elapsed.TotalMilliseconds });
            debugCleanHtmlBytes = articleHtml?.Length ?? 0;

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

            bool needAnyImageWork = extraction.Success && !string.IsNullOrEmpty(articleHtml) &&
                (request.IncludeCleanHtmlEmbeddedImages || request.IncludeCleanHtmlLinkedImages ||
                 request.IncludeCleanHtmlNoImages || effectiveIncludeInPage || request.IncludeAllOutOfPageImages);

            if (needAnyImageWork)
            {
                try
                {
                    var httpClient = _httpClientFactory.CreateClient(
                        request.ClearCookies ? "ImageDownloaderNoCookies" : "ImageDownloader");
                    var processor = new ImageProcessor(
                        _loggerFactory.CreateLogger<ImageProcessor>(),
                        httpClient,
                        _options.MaxImageBytes);

                    var imageTimer = Stopwatch.StartNew();
                    var imgResult = await processor.ProcessAsync(
                        sourceHtml,
                        articleHtml,
                        uri,
                        needEmbedded:        request.IncludeCleanHtmlEmbeddedImages,
                        needLinked:          request.IncludeCleanHtmlLinkedImages,
                        needNoImages:        request.IncludeCleanHtmlNoImages,
                        needInPageImages:    effectiveIncludeInPage,
                        needOutOfPageImages: request.IncludeAllOutOfPageImages,
                        imageLinkRewrite:    request.ImageLinkRewrite,
                        pngConversionRequired: request.PngConversionRequired,
                        imageFetchLog,
                        linkedCts.Token);
                    imageTimer.Stop();
                    stages?.Add(new StageTiming { Stage = "ImageProcessing", Ms = imageTimer.Elapsed.TotalMilliseconds });

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
                cleanHtmlNoImages = articleHtml;
            }

            // --- Markdown variants ---
            var markdownTimer = Stopwatch.StartNew();
            string? markdownNoImages = null;
            string? markdownLinked = null;

            // Prepend title as H1 if the article HTML doesn't already open with one
            var titlePrefix = !string.IsNullOrWhiteSpace(extraction.Title)
                && !articleHtml.TrimStart().StartsWith("<h1", StringComparison.OrdinalIgnoreCase)
                ? $"# {extraction.Title}\n\n"
                : string.Empty;

            if (request.IncludeMarkdownNoImages && extraction.Success)
            {
                var source = cleanHtmlNoImages ?? (needAnyImageWork ? null : articleHtml);
                // Always strip images from HTML before conversion for the NoImages variant
                var htmlForNoImages = OutputCleaner.StripHtmlImages(source ?? articleHtml);
                markdownNoImages = _markdownConverter.ConvertToMarkdown(htmlForNoImages);
                if (markdownNoImages != null)
                {
                    markdownNoImages = OutputCleaner.StripMarkdownImages(markdownNoImages);
                    markdownNoImages = OutputCleaner.CleanMarkdown(markdownNoImages);
                    markdownNoImages = OutputCleaner.NormalizeUnicodePunctuation(markdownNoImages, request.UnicodeNormalization);
                    markdownNoImages = titlePrefix + markdownNoImages;
                    markdownNoImages = OutputCleaner.DeduplicateLeadingTitle(markdownNoImages);
                }
            }

            if (request.IncludeMarkdownLinkedImages && extraction.Success)
            {
                markdownLinked = _markdownConverter.ConvertToMarkdown(cleanHtmlLinked ?? articleHtml);
                if (markdownLinked != null)
                {
                    markdownLinked = OutputCleaner.CleanMarkdown(markdownLinked);
                    markdownLinked = OutputCleaner.NormalizeUnicodePunctuation(markdownLinked, request.UnicodeNormalization);
                    markdownLinked = titlePrefix + markdownLinked;
                    markdownLinked = OutputCleaner.DeduplicateLeadingTitle(markdownLinked);
                }
            }

            markdownTimer.Stop();
            stages?.Add(new StageTiming { Stage = "MarkdownConversion", Ms = markdownTimer.Elapsed.TotalMilliseconds });

            // Featured image fallback: when markdownLinkedImages is requested but the article
            // body contains no images, inject the featured/hero image under the title heading
            // and add it to allInPageImages so the caller receives it as a downloadable file.
            var featuredTimer = Stopwatch.StartNew();
            if (request.IncludeMarkdownLinkedImages
                && markdownLinked != null
                && !string.IsNullOrWhiteSpace(extraction.FeaturedImage)
                && (allInPageImages == null || allInPageImages.Length == 0))
            {
                try
                {
                    var httpClient = _httpClientFactory.CreateClient(
                        request.ClearCookies ? "ImageDownloaderNoCookies" : "ImageDownloader");
                    using var featuredCts = CancellationTokenSource.CreateLinkedTokenSource(linkedCts.Token);
                    featuredCts.CancelAfter(10_000);
                    using var response = await httpClient.GetAsync(
                        extraction.FeaturedImage, HttpCompletionOption.ResponseHeadersRead, featuredCts.Token);
                    if (response.IsSuccessStatusCode)
                    {
                        var mimeType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
                        var bytes = await response.Content.ReadAsByteArrayAsync(featuredCts.Token);
                        imageFetchLog?.Add(new ImageFetchLog
                        {
                            Url = extraction.FeaturedImage!, Pool = "featured", Outcome = "ok",
                            HttpStatus = (int)response.StatusCode, Bytes = bytes.Length,
                            Ms = featuredTimer.Elapsed.TotalMilliseconds
                        });
                        var ext = ImageProcessor.GetExtensionForMime(mimeType);
                        var localName = $"image-0000{ext}";
                        allInPageImages =
                        [
                            new ImageEntry
                            {
                                Index = 0,
                                LocalName = localName,
                                OriginalUrl = extraction.FeaturedImage,
                                OriginalMimeType = mimeType,
                                Alt = extraction.Title,
                                SizeBytes = bytes.Length,
                                Data = Convert.ToBase64String(bytes)
                            }
                        ];
                        markdownLinked = OutputCleaner.InjectFeaturedImageAfterTitle(
                            markdownLinked, $"![{extraction.Title ?? ""}]({localName})");
                    }
                    else
                    {
                        imageFetchLog?.Add(new ImageFetchLog
                        {
                            Url = extraction.FeaturedImage!, Pool = "featured", Outcome = "httpError",
                            HttpStatus = (int)response.StatusCode,
                            Ms = featuredTimer.Elapsed.TotalMilliseconds
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to fetch featured image: {Url}", extraction.FeaturedImage);
                    imageFetchLog?.Add(new ImageFetchLog { Url = extraction.FeaturedImage!, Pool = "featured", Outcome = "exception", Ms = 0 });
                }
            }
            featuredTimer.Stop();
            stages?.Add(new StageTiming { Stage = "FeaturedImageFetch", Ms = featuredTimer.Elapsed.TotalMilliseconds });

            // --- Plain text ---
            // Generate from cleaned HTML with proper block-level separation,
            // not from raw TextContent which concatenates everything.
            string? plainText = null;
            if (request.IncludePlainText && extraction.Success && !string.IsNullOrEmpty(articleHtml))
            {
                var plainTextTimer = Stopwatch.StartNew();
                plainText = HtmlArticleCleaner.ExtractPlainText(baseCleanHtml);
                if (plainText != null)
                    plainText = OutputCleaner.NormalizeUnicodePunctuation(plainText, request.UnicodeNormalization);
                plainTextTimer.Stop();
                stages?.Add(new StageTiming { Stage = "PlainText", Ms = plainTextTimer.Elapsed.TotalMilliseconds });
            }

            // --- Source HTML ---
            string? responseSourceHtml = null;
            if (request.IncludeSourceHtml)
                responseSourceHtml = sourceHtml;
            // Note: SourceHtmlOmitted flag was already set upfront based on IncludeSourceHtml

            stopwatch.Stop();

            return new ProcessResponse
            {
                Success = extraction.Success,
                RequestedUrl = request.Url,
                ProcessedAt = DateTimeOffset.UtcNow,
                ProcessingTimeMs = stopwatch.Elapsed.TotalMilliseconds,
                Title = extraction.Title,
                Byline = extraction.Byline,
                Author = extraction.Author,
                Excerpt = extraction.Excerpt,
                SiteName = extraction.SiteName,
                Language = extraction.Language,
                PublicationDate = extraction.PublicationDate,
                FeaturedImage = extraction.FeaturedImage,
                TimeToRead = extraction.TimeToRead?.ToString(@"hh\:mm\:ss"),
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
                AllOutOfPageImages = allOutOfPageImages,
                DebugInfo = request.IncludeDebugInfo ? new DebugInfo
                {
                    Stages = stages?.ToArray() ?? [],
                    ImageFetches = imageFetchLog?.ToArray() ?? [],
                    SourceHtmlBytes = debugSourceHtmlBytes,
                    CleanHtmlBytes = debugCleanHtmlBytes
                } : null
            };
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            _logger.LogWarning("Request timed out after {Timeout}ms", effectiveTimeout);
            return CreateErrorResponse(request.Url, stopwatch.Elapsed.TotalMilliseconds, traceId, completionFlags, $"Request timed out after {effectiveTimeout}ms");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Processing failed for {Url}", request.Url);
            return CreateErrorResponse(request.Url, stopwatch.Elapsed.TotalMilliseconds, traceId, completionFlags, ex.Message);
        }
        finally
        {
            if (_options.IsAggressiveGc)
                PerformAggressiveGc();
        }
    }

    private async Task<string?> FetchWithBrowserAsync(Uri uri, string userAgent, int stabilizationMs, CookieEntry[]? cookies, string? referer, bool clearCookies, CancellationToken cancellationToken)
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

            if (clearCookies)
            {
                _logger.LogDebug("Cookie blocking enabled for {Url}: stripping Set-Cookie response headers and disabling JS cookie APIs", uri);

                // Intercept every response to strip Set-Cookie headers so the browser stores no cookies.
                // This re-fetches each response via route.FetchAsync, which is the only Playwright mechanism
                // for modifying response headers. The overhead is acceptable because clearCookies is
                // opt-in and not the default path.
                await context.RouteAsync("**/*", async route =>
                {
                    var response = await route.FetchAsync();
                    var headers = response.Headers
                        .Where(kvp => !kvp.Key.Equals("set-cookie", StringComparison.OrdinalIgnoreCase))
                        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
                    await route.FulfillAsync(new RouteFulfillOptions { Response = response, Headers = headers });
                });

                // Override JS cookie APIs so pages see cookies as disabled
                await context.AddInitScriptAsync(DisableCookiesScript);
            }
            else if (cookies != null && cookies.Length > 0)
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
                Timeout = _options.MaxTimeMs,
                Referer = referer
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

    private static ProcessResponse CreateErrorResponse(string url, double processingTimeMs, string traceId, CompletionFlag completionFlags, string error)
    {
        return new ProcessResponse
        {
            Success = false,
            RequestedUrl = url,
            ProcessedAt = DateTimeOffset.UtcNow,
            ProcessingTimeMs = processingTimeMs,
            CompletionFlags = (int)completionFlags,
            Errors = [error],
            TraceId = traceId
        };
    }
}


