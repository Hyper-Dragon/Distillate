using System.Buffers;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Distillate.Core.Conversion;
using Distillate.Core.Models;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Distillate.Core.Images;

/// <summary>
/// Processes images for a single request, producing all requested HTML/image variants.
/// Images are deduplicated by normalised source URL. The first occurrence in document
/// order determines the index and local filename used for link rewriting.
/// </summary>
public partial class ImageProcessor
{
    private readonly ILogger<ImageProcessor> _logger;
    private readonly HttpClient _httpClient;
    private readonly long _maxEmbedBytes;

    private static readonly Dictionary<string, string> ExtensionByMime = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/gif"] = ".gif",
        ["image/webp"] = ".webp",
        ["image/svg+xml"] = ".svg",
        ["image/x-icon"] = ".ico",
        ["image/bmp"] = ".bmp"
    };

    private static readonly Dictionary<string, string> MimeByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".svg"] = "image/svg+xml",
        [".ico"] = "image/x-icon",
        [".bmp"] = "image/bmp"
    };

    // Per-image download timeout. Applies to both header request and body download.
    // Tracking pixels and stalled CDN requests will be abandoned after this window
    // rather than blocking the entire processing pipeline.
    private const int PerImageTimeoutMs = 10_000;

    // Maximum output heights applied when pngConversionRequired is true.
    // Portrait images (height > width) are capped at 1080 px; landscape at 720 px.
    private const int PngPortraitMaxHeight = 1080;
    private const int PngLandscapeMaxHeight = 720;

    public ImageProcessor(ILogger<ImageProcessor> logger, HttpClient httpClient, long maxEmbedBytes)
    {
        _logger = logger;
        _httpClient = httpClient;
        _maxEmbedBytes = maxEmbedBytes;
    }

    /// <summary>Returns the preferred file extension for a given MIME type.</summary>
    public static string GetExtensionForMime(string mimeType) =>
        ExtensionByMime.GetValueOrDefault(mimeType, ".jpg");

    public async Task<ImageProcessorResult> ProcessAsync(
        string sourceHtml,
        string cleanHtml,
        Uri baseUri,
        bool needEmbedded,
        bool needLinked,
        bool needNoImages,
        bool needInPageImages,
        bool needOutOfPageImages,
        bool imageLinkRewrite,
        bool pngConversionRequired,
        List<ImageFetchLog>? imageFetchLog,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? excludeImageUrls = null)
    {
        // --- Parse clean HTML and drop caller-excluded images before anything else ---
        // Removal happens before index assignment so an excluded image consumes no image-NNNN
        // slot and the remaining numbering stays contiguous. The filtered HTML replaces
        // cleanHtml for every downstream variant (linked passthrough, embed, markdown source),
        // so an excluded image cannot survive as an un-rewritten remote src.
        var config = AngleSharp.Configuration.Default;
        var browsingContext = BrowsingContext.New(config);
        var parser = browsingContext.GetService<IHtmlParser>()!;

        var cleanDoc = parser.ParseDocument(cleanHtml);
        var excludedInPage = RemoveExcludedImages(cleanDoc, baseUri, excludeImageUrls);
        if (excludedInPage > 0)
        {
            cleanHtml = cleanDoc.DocumentElement.OuterHtml;
            LogExcludedImages(excludedInPage, "inPage");
        }

        // URL → (index, alt) for in-page images
        var inPageUrlToEntry = new Dictionary<string, (int Index, string? Alt)>(StringComparer.OrdinalIgnoreCase);
        foreach (var img in cleanDoc.QuerySelectorAll("img[src]"))
        {
            if (ResolveImageUri(baseUri, img.GetAttribute("src")) is not { } abs)
                continue;
            if (IsImageExcluded(abs.AbsoluteUri, excludeImageUrls))
                continue;

            var key = abs.AbsoluteUri;
            if (!inPageUrlToEntry.ContainsKey(key))
                inPageUrlToEntry[key] = (inPageUrlToEntry.Count, img.GetAttribute("alt"));
        }

        // --- Collect out-of-page image URLs from source HTML ---
        // The same filter applies here: an excluded in-page image is already gone from
        // cleanDoc, so without this check it would walk back in via allOutOfPageImages
        // (it is still present in the source document).
        var sourceDoc = parser.ParseDocument(sourceHtml);
        var outOfPageUrlToEntry = new Dictionary<string, (int Index, string? Alt)>(StringComparer.OrdinalIgnoreCase);
        var excludedOutOfPage = 0;
        if (needOutOfPageImages)
        {
            foreach (var img in sourceDoc.QuerySelectorAll("img[src]"))
            {
                if (ResolveImageUri(baseUri, img.GetAttribute("src")) is not { } abs)
                    continue;
                if (IsImageExcluded(abs.AbsoluteUri, excludeImageUrls))
                {
                    excludedOutOfPage++;
                    continue;
                }

                var key = abs.AbsoluteUri;
                if (!inPageUrlToEntry.ContainsKey(key) && !outOfPageUrlToEntry.ContainsKey(key))
                    outOfPageUrlToEntry[key] = (outOfPageUrlToEntry.Count, img.GetAttribute("alt"));
            }

            if (excludedOutOfPage > 0)
                LogExcludedImages(excludedOutOfPage, "outOfPage");
        }

        // --- Fetch in-page images if needed ---
        ImageEntry[]? inPageImages = null;
        bool inPagePartial = false;
        Dictionary<string, ImageEntry>? inPageFetched = null;

        if (needInPageImages && inPageUrlToEntry.Count > 0)
        {
            (inPageFetched, inPagePartial) = await FetchImagesAsync(
                inPageUrlToEntry, pngConversionRequired, "inPage", imageFetchLog, cancellationToken);
            inPageImages = inPageFetched.Values.OrderBy(e => e.Index).ToArray();
        }

        // --- Fetch out-of-page images if needed ---
        ImageEntry[]? outOfPageImages = null;
        bool outOfPagePartial = false;

        if (needOutOfPageImages && outOfPageUrlToEntry.Count > 0)
        {
            var (fetched, partial) = await FetchImagesAsync(
                outOfPageUrlToEntry, pngConversionRequired, "outOfPage", imageFetchLog, cancellationToken);
            outOfPageImages = fetched.Values.OrderBy(e => e.Index).ToArray();
            outOfPagePartial = partial;
        }

        // --- Build local name map for link rewriting ---
        // URL → localName, based on fetched entries (or index map if fetch wasn't done)
        Dictionary<string, string> localNameMap = BuildLocalNameMap(
            inPageUrlToEntry, inPageFetched, pngConversionRequired);

        // --- Produce HTML variants ---
        string? htmlEmbedded = null;
        string? htmlLinked = null;
        string? htmlNoImages = null;
        bool imageTruncated = false;

        if (needEmbedded || needLinked || needNoImages)
        {
            // Re-parse cleanHtml fresh for each variant to avoid shared DOM mutation
            if (needNoImages)
            {
                var doc = parser.ParseDocument(cleanHtml);
                StripImages(doc);
                htmlNoImages = OutputCleaner.UnwrapHtmlDocument(doc.DocumentElement.OuterHtml);
            }

            if (needLinked && imageLinkRewrite)
            {
                var doc = parser.ParseDocument(cleanHtml);
                RewriteImageLinks(doc, baseUri, localNameMap);
                htmlLinked = doc.DocumentElement.OuterHtml;
            }
            else if (needLinked)
            {
                htmlLinked = cleanHtml;
            }

            if (needEmbedded)
            {
                var doc = parser.ParseDocument(cleanHtml);
                (htmlEmbedded, imageTruncated) = await EmbedImagesAsync(doc, baseUri, imageFetchLog, cancellationToken);
            }
        }

        return new ImageProcessorResult
        {
            CleanHtmlEmbeddedImages = htmlEmbedded,
            CleanHtmlLinkedImages = htmlLinked,
            CleanHtmlNoImages = htmlNoImages,
            AllInPageImages = inPageImages,
            AllOutOfPageImages = outOfPageImages,
            ImageTruncated = imageTruncated,
            AllInPageImagesPartial = inPagePartial,
            AllOutOfPageImagesPartial = outOfPagePartial,
            ExcludedImageCount = excludedInPage + excludedOutOfPage
        };
    }

    private static Dictionary<string, string> BuildLocalNameMap(
        Dictionary<string, (int Index, string? Alt)> urlToEntry,
        Dictionary<string, ImageEntry>? fetched,
        bool pngConversionRequired)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (url, (index, _)) in urlToEntry)
        {
            string localName;
            if (fetched != null && fetched.TryGetValue(url, out var entry))
            {
                localName = entry.LocalName;
            }
            else
            {
                var ext = pngConversionRequired ? ".png" : ".jpg";
                localName = $"image-{index:D4}{ext}";
            }
            map[url] = localName;
        }

        return map;
    }

    private async Task<(Dictionary<string, ImageEntry> Entries, bool Partial)> FetchImagesAsync(
        Dictionary<string, (int Index, string? Alt)> urlToEntry,
        bool pngConversionRequired,
        string pool,
        List<ImageFetchLog>? fetchLog,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, ImageEntry>(StringComparer.OrdinalIgnoreCase);
        bool partial = false;

        foreach (var (url, (index, alt)) in urlToEntry)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                var entry = await FetchSingleImageAsync(url, index, alt, pngConversionRequired, pool, fetchLog, cancellationToken);
                if (entry != null)
                    result[url] = entry;
                else
                    partial = true;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to fetch image: {Url}", url);
                fetchLog?.Add(new ImageFetchLog { Url = url, Pool = pool, Outcome = "exception", Ms = 0 });
                partial = true;
            }
        }

        return (result, partial);
    }

    private async Task<ImageEntry?> FetchSingleImageAsync(
        string url,
        int index,
        string? alt,
        bool pngConversionRequired,
        string pool,
        List<ImageFetchLog>? fetchLog,
        CancellationToken cancellationToken)
    {
        // Independent per-image timeout: abandons tracking pixels, stalled CDN responses,
        // or any server that sends headers but never delivers a body.
        using var perImageCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        perImageCts.CancelAfter(PerImageTimeoutMs);
        var token = perImageCts.Token;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {

            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode)
            {
                fetchLog?.Add(new ImageFetchLog
                {
                    Url = url,
                    Pool = pool,
                    Outcome = "httpError",
                    HttpStatus = (int)response.StatusCode,
                    Ms = sw.Elapsed.TotalMilliseconds
                });
                return null;
            }

            var mimeType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (string.IsNullOrEmpty(mimeType))
            {
                var ext = Path.GetExtension(new Uri(url).AbsolutePath);
                mimeType = MimeByExtension.GetValueOrDefault(ext, "application/octet-stream");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var memStream = new MemoryStream();

            var buffer = ArrayPool<byte>.Shared.Rent(81920);
            try
            {
                int read;
                while ((read = await stream.ReadAsync(buffer.AsMemory(0, 81920), token)) > 0)
                    await memStream.WriteAsync(buffer.AsMemory(0, read), token);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            var imageBytes = memStream.ToArray();
            string outputMime;
            byte[] outputBytes;

            if (pngConversionRequired && !mimeType.Equals("image/svg+xml", StringComparison.OrdinalIgnoreCase))
            {
                outputBytes = ConvertToPng(imageBytes);
                outputMime = "image/png";
            }
            else
            {
                outputBytes = imageBytes;
                outputMime = mimeType;
            }

            var ext2 = ExtensionByMime.GetValueOrDefault(outputMime, ".bin");

            fetchLog?.Add(new ImageFetchLog
            {
                Url = url,
                Pool = pool,
                Outcome = "ok",
                HttpStatus = (int)response.StatusCode,
                Bytes = outputBytes.Length,
                Ms = sw.Elapsed.TotalMilliseconds
            });

            return new ImageEntry
            {
                Index = index,
                LocalName = $"image-{index:D4}{ext2}",
                OriginalUrl = url,
                OriginalMimeType = outputMime,
                Alt = alt,
                SizeBytes = outputBytes.Length,
                Data = Convert.ToBase64String(outputBytes)
            };

        } // end try
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            LogImageFetchTimeout(url);
            fetchLog?.Add(new ImageFetchLog
            {
                Url = url,
                Pool = pool,
                Outcome = "timeout",
                Ms = sw.Elapsed.TotalMilliseconds
            });
            return null;
        }
    }

    public static byte[] ConvertToPng(byte[] inputBytes)
    {
        using var skBitmap = SKBitmap.Decode(inputBytes);
        if (skBitmap == null)
            return inputBytes;

        // Step 1: Create the PNG first using the original working approach.
        // This guarantees a valid PNG regardless of source format.
        using var sourceImage = SKImage.FromBitmap(skBitmap);
        using var pngData = sourceImage.Encode(SKEncodedImageFormat.Png, 100);
        if (pngData == null)
            return inputBytes;

        // Step 2: Resize the PNG if the source dimensions exceed the limits.
        var (targetWidth, targetHeight) = CalculatePngResizeDimensions(skBitmap.Width, skBitmap.Height);
        if (targetWidth == skBitmap.Width && targetHeight == skBitmap.Height)
            return pngData.ToArray();   // Already within limits — return the PNG as-is

        using var resizedBitmap = skBitmap.Resize(
            new SKImageInfo(targetWidth, targetHeight),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        if (resizedBitmap == null)
            return pngData.ToArray();   // Resize failed — return the unresized PNG

        using var resizedImage = SKImage.FromBitmap(resizedBitmap);
        if (resizedImage == null)
            return pngData.ToArray();

        using var resizedData = resizedImage.Encode(SKEncodedImageFormat.Png, 100);
        return resizedData?.ToArray() ?? pngData.ToArray();
    }

    /// <summary>
    /// Returns the target (width, height) after applying PNG resize limits.
    /// Portrait images (height &gt; width) are capped at <see cref="PngPortraitMaxHeight"/> px;
    /// landscape images at <see cref="PngLandscapeMaxHeight"/> px. Aspect ratio is preserved.
    /// </summary>
    public static (int Width, int Height) CalculatePngResizeDimensions(int originalWidth, int originalHeight)
    {
        if (originalWidth <= 0 || originalHeight <= 0)
            return (originalWidth, originalHeight);

        int maxHeight = originalHeight > originalWidth ? PngPortraitMaxHeight : PngLandscapeMaxHeight;

        if (originalHeight <= maxHeight)
            return (originalWidth, originalHeight);

        var scale = (float)maxHeight / originalHeight;
        var newWidth = (int)Math.Round(originalWidth * scale);
        return (newWidth, maxHeight);
    }

    // --- excludeImageUrls support ---

    /// <summary>
    /// True when <paramref name="absoluteUrl"/> contains any non-blank pattern from
    /// <paramref name="excludeImageUrls"/> as a case-insensitive substring. A null or empty
    /// pattern list never matches, so requests that omit the option keep today's behaviour.
    /// </summary>
    public static bool IsImageExcluded(string absoluteUrl, IReadOnlyList<string>? excludeImageUrls)
    {
        if (excludeImageUrls is null || excludeImageUrls.Count == 0)
            return false;

        foreach (var pattern in excludeImageUrls)
        {
            // A blank pattern would match every URL — ignore it rather than drop all images.
            if (string.IsNullOrWhiteSpace(pattern))
                continue;

            if (absoluteUrl.Contains(pattern.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Removes every &lt;img&gt; whose resolved absolute URL is excluded, together with its
    /// &lt;picture&gt; wrapper (which would otherwise leave orphan &lt;source&gt; elements) and any
    /// &lt;a&gt;/&lt;figure&gt; left empty by the removal. Returns the number of images removed.
    /// The removal is what stops a matching image reaching the markdown stage as a remote src:
    /// skipping it during link rewriting alone would leave the original src in place.
    /// </summary>
    public static int RemoveExcludedImages(IDocument document, Uri baseUri, IReadOnlyList<string>? excludeImageUrls)
    {
        if (excludeImageUrls is null || excludeImageUrls.Count == 0)
            return 0;

        // Collect first, then remove — removing during enumeration mutates the query result.
        var targets = new List<IElement>();
        foreach (var img in document.QuerySelectorAll("img[src]"))
        {
            if (ResolveImageUri(baseUri, img.GetAttribute("src")) is not { } abs)
                continue;
            if (!IsImageExcluded(abs.AbsoluteUri, excludeImageUrls))
                continue;

            var parent = img.ParentElement;
            targets.Add(parent != null && IsNamed(parent, "picture") ? parent : img);
        }

        var removed = 0;
        foreach (var target in targets)
        {
            var parent = target.ParentElement;
            if (parent is null)
                continue;   // already detached with an ancestor (<picture> removed first)

            target.Remove();
            removed++;
            PruneEmptyWrappers(parent);
        }

        return removed;
    }

    /// <summary>
    /// Applies <see cref="RemoveExcludedImages"/> to a standalone HTML fragment and returns the
    /// filtered HTML. Returns the input unchanged (same instance) when the option is absent or
    /// nothing matched, so existing requests produce byte-identical output.
    /// </summary>
    public static string FilterExcludedImages(
        string html, Uri baseUri, IReadOnlyList<string>? excludeImageUrls, out int removedCount)
    {
        removedCount = 0;
        if (string.IsNullOrWhiteSpace(html) || excludeImageUrls is null || excludeImageUrls.Count == 0)
            return html;

        var parser = BrowsingContext.New(AngleSharp.Configuration.Default).GetService<IHtmlParser>()!;
        var document = parser.ParseDocument(html);
        removedCount = RemoveExcludedImages(document, baseUri, excludeImageUrls);
        if (removedCount == 0)
            return html;

        return OutputCleaner.UnwrapHtmlDocument(document.DocumentElement.OuterHtml);
    }

    private static Uri? ResolveImageUri(Uri baseUri, string? src)
    {
        if (string.IsNullOrEmpty(src) || src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return null;

        return Uri.TryCreate(baseUri, src, out var abs) && (abs.Scheme == "http" || abs.Scheme == "https")
            ? abs
            : null;
    }

    /// <summary>
    /// Removes &lt;a&gt;/&lt;figure&gt; wrappers the exclusion left with no text and no media,
    /// climbing outwards so nested wrappers (&lt;figure&gt;&lt;a&gt;&lt;img&gt;) all collapse.
    /// Wrappers that still carry text (e.g. a figure with a figcaption) are kept.
    /// </summary>
    private static void PruneEmptyWrappers(IElement? element)
    {
        var current = element;
        while (current != null && IsRemovableWrapper(current) && IsEffectivelyEmpty(current))
        {
            var parent = current.ParentElement;
            current.Remove();
            current = parent;
        }
    }

    private static bool IsRemovableWrapper(IElement element) =>
        IsNamed(element, "a") || IsNamed(element, "figure");

    private static bool IsEffectivelyEmpty(IElement element) =>
        string.IsNullOrWhiteSpace(element.TextContent) &&
        element.QuerySelectorAll(MediaSelector).Length == 0;

    private static bool IsNamed(IElement element, string localName) =>
        element.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase);

    private const string MediaSelector = "img, picture, svg, video, audio, iframe, object, embed, canvas";

    private static void StripImages(IDocument document)
    {
        // Remove <picture> elements (contain <source> and <img>)
        foreach (var pic in document.QuerySelectorAll("picture").ToList())
            pic.Remove();

        // Remove remaining <img> tags
        foreach (var img in document.QuerySelectorAll("img").ToList())
            img.Remove();

        // Remove <svg> elements that are decorative
        foreach (var svg in document.QuerySelectorAll("svg").ToList())
            svg.Remove();

        // Remove <figure> elements that are now empty
        foreach (var fig in document.QuerySelectorAll("figure").ToList())
        {
            if (string.IsNullOrWhiteSpace(fig.TextContent))
                fig.Remove();
        }
    }

    private static void RewriteImageLinks(
        IDocument document,
        Uri baseUri,
        Dictionary<string, string> localNameMap)
    {
        foreach (var img in document.QuerySelectorAll("img[src]"))
        {
            var src = img.GetAttribute("src");
            if (string.IsNullOrEmpty(src) || src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                continue;

            if (Uri.TryCreate(baseUri, src, out var abs) && localNameMap.TryGetValue(abs.AbsoluteUri, out var localName))
                img.SetAttribute("src", localName);
        }
    }

    private async Task<(string Html, bool Truncated)> EmbedImagesAsync(
        IDocument document,
        Uri baseUri,
        List<ImageFetchLog>? fetchLog,
        CancellationToken cancellationToken)
    {
        long totalBytes = 0;
        bool truncated = false;

        foreach (var img in document.QuerySelectorAll("img[src]").ToList())
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var src = img.GetAttribute("src");
            if (string.IsNullOrEmpty(src) || src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!Uri.TryCreate(baseUri, src, out var abs) || (abs.Scheme != "http" && abs.Scheme != "https"))
                continue;

            // Per-image timeout prevents tracking pixels or stalled CDN responses from blocking the pipeline
            using var perImageCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            perImageCts.CancelAfter(PerImageTimeoutMs);
            var imageToken = perImageCts.Token;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                using var response = await _httpClient.GetAsync(abs, HttpCompletionOption.ResponseHeadersRead, imageToken);
                if (!response.IsSuccessStatusCode)
                {
                    fetchLog?.Add(new ImageFetchLog
                    {
                        Url = abs.AbsoluteUri,
                        Pool = "embed",
                        Outcome = "httpError",
                        HttpStatus = (int)response.StatusCode,
                        Ms = sw.Elapsed.TotalMilliseconds
                    });
                    continue;
                }

                var mimeType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                if (string.IsNullOrEmpty(mimeType))
                {
                    var ext = Path.GetExtension(abs.AbsolutePath);
                    mimeType = MimeByExtension.GetValueOrDefault(ext, "application/octet-stream");
                }

                await using var stream = await response.Content.ReadAsStreamAsync(imageToken);
                using var memStream = new MemoryStream();
                var buffer = ArrayPool<byte>.Shared.Rent(81920);
                try
                {
                    int read;
                    while ((read = await stream.ReadAsync(buffer.AsMemory(0, 81920), imageToken)) > 0)
                        await memStream.WriteAsync(buffer.AsMemory(0, read), imageToken);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }

                var bytes = memStream.ToArray();
                if (totalBytes + bytes.Length > _maxEmbedBytes)
                {
                    fetchLog?.Add(new ImageFetchLog
                    {
                        Url = abs.AbsoluteUri,
                        Pool = "embed",
                        Outcome = "skipped",
                        Bytes = bytes.Length,
                        Ms = sw.Elapsed.TotalMilliseconds
                    });
                    truncated = true;
                    break;
                }

                totalBytes += bytes.Length;
                var dataUri = $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";
                img.SetAttribute("src", dataUri);
                fetchLog?.Add(new ImageFetchLog
                {
                    Url = abs.AbsoluteUri,
                    Pool = "embed",
                    Outcome = "ok",
                    HttpStatus = (int)response.StatusCode,
                    Bytes = bytes.Length,
                    Ms = sw.Elapsed.TotalMilliseconds
                });
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                LogImageEmbedTimeout(abs);
                fetchLog?.Add(new ImageFetchLog
                {
                    Url = abs.AbsoluteUri,
                    Pool = "embed",
                    Outcome = "timeout",
                    Ms = sw.Elapsed.TotalMilliseconds
                });
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to embed image: {Url}", abs);
                fetchLog?.Add(new ImageFetchLog
                {
                    Url = abs.AbsoluteUri,
                    Pool = "embed",
                    Outcome = "exception",
                    Ms = sw.Elapsed.TotalMilliseconds
                });
            }
        }

        return (document.DocumentElement.OuterHtml, truncated);
    }
    [LoggerMessage(Level = LogLevel.Debug, Message = "Image fetch timed out after 10000ms: {Url}")]
    private partial void LogImageFetchTimeout(string url);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Image embed timed out after 10000ms: {Url}")]
    private partial void LogImageEmbedTimeout(Uri url);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Excluded {Count} image(s) matching excludeImageUrls in the {Pool} pool")]
    private partial void LogExcludedImages(int count, string pool);
}

public record ImageProcessorResult
{
    public string? CleanHtmlEmbeddedImages { get; init; }
    public string? CleanHtmlLinkedImages { get; init; }
    public string? CleanHtmlNoImages { get; init; }
    public ImageEntry[]? AllInPageImages { get; init; }
    public ImageEntry[]? AllOutOfPageImages { get; init; }
    public bool ImageTruncated { get; init; }
    public bool AllInPageImagesPartial { get; init; }
    public bool AllOutOfPageImagesPartial { get; init; }

    /// <summary>Number of images dropped by excludeImageUrls (in-page removals + out-of-page skips).</summary>
    public int ExcludedImageCount { get; init; }
}
