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
public class ImageProcessor
{
    private readonly ILogger<ImageProcessor> _logger;
    private readonly HttpClient _httpClient;
    private readonly long _maxEmbedBytes;

    private static readonly Dictionary<string, string> ExtensionByMime = new(StringComparer.OrdinalIgnoreCase)
    {
        { "image/jpeg", ".jpg" },
        { "image/png", ".png" },
        { "image/gif", ".gif" },
        { "image/webp", ".webp" },
        { "image/svg+xml", ".svg" },
        { "image/x-icon", ".ico" },
        { "image/bmp", ".bmp" }
    };

    private static readonly Dictionary<string, string> MimeByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".jpg", "image/jpeg" },
        { ".jpeg", "image/jpeg" },
        { ".png", "image/png" },
        { ".gif", "image/gif" },
        { ".webp", "image/webp" },
        { ".svg", "image/svg+xml" },
        { ".ico", "image/x-icon" },
        { ".bmp", "image/bmp" }
    };

    public ImageProcessor(ILogger<ImageProcessor> logger, HttpClient httpClient, long maxEmbedBytes)
    {
        _logger = logger;
        _httpClient = httpClient;
        _maxEmbedBytes = maxEmbedBytes;
    }

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
        CancellationToken cancellationToken)
    {
        // --- Parse clean HTML and collect unique in-page image URLs in document order ---
        var config = AngleSharp.Configuration.Default;
        var browsingContext = BrowsingContext.New(config);
        var parser = browsingContext.GetService<IHtmlParser>()!;

        var cleanDoc = parser.ParseDocument(cleanHtml);
        var cleanImgElements = cleanDoc.QuerySelectorAll("img[src]").ToList();

        // URL → (index, alt) for in-page images
        var inPageUrlToEntry = new Dictionary<string, (int Index, string? Alt)>(StringComparer.OrdinalIgnoreCase);
        foreach (var img in cleanImgElements)
        {
            var src = img.GetAttribute("src");
            if (string.IsNullOrEmpty(src) || src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!Uri.TryCreate(baseUri, src, out var abs) || (abs.Scheme != "http" && abs.Scheme != "https"))
                continue;

            var key = abs.AbsoluteUri;
            if (!inPageUrlToEntry.ContainsKey(key))
                inPageUrlToEntry[key] = (inPageUrlToEntry.Count, img.GetAttribute("alt"));
        }

        // --- Collect out-of-page image URLs from source HTML ---
        var sourceDoc = parser.ParseDocument(sourceHtml);
        var outOfPageUrlToEntry = new Dictionary<string, (int Index, string? Alt)>(StringComparer.OrdinalIgnoreCase);
        if (needOutOfPageImages)
        {
            foreach (var img in sourceDoc.QuerySelectorAll("img[src]"))
            {
                var src = img.GetAttribute("src");
                if (string.IsNullOrEmpty(src) || src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!Uri.TryCreate(baseUri, src, out var abs) || (abs.Scheme != "http" && abs.Scheme != "https"))
                    continue;

                var key = abs.AbsoluteUri;
                if (!inPageUrlToEntry.ContainsKey(key) && !outOfPageUrlToEntry.ContainsKey(key))
                    outOfPageUrlToEntry[key] = (outOfPageUrlToEntry.Count, img.GetAttribute("alt"));
            }
        }

        // --- Fetch in-page images if needed ---
        ImageEntry[]? inPageImages = null;
        bool inPagePartial = false;
        Dictionary<string, ImageEntry>? inPageFetched = null;

        if (needInPageImages && inPageUrlToEntry.Count > 0)
        {
            (inPageFetched, inPagePartial) = await FetchImagesAsync(
                inPageUrlToEntry, pngConversionRequired, cancellationToken);
            inPageImages = inPageFetched.Values.OrderBy(e => e.Index).ToArray();
        }

        // --- Fetch out-of-page images if needed ---
        ImageEntry[]? outOfPageImages = null;
        bool outOfPagePartial = false;

        if (needOutOfPageImages && outOfPageUrlToEntry.Count > 0)
        {
            var (fetched, partial) = await FetchImagesAsync(
                outOfPageUrlToEntry, pngConversionRequired, cancellationToken);
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
                (htmlEmbedded, imageTruncated) = await EmbedImagesAsync(doc, baseUri, cancellationToken);
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
            AllOutOfPageImagesPartial = outOfPagePartial
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
                var entry = await FetchSingleImageAsync(url, index, alt, pngConversionRequired, cancellationToken);
                if (entry != null)
                    result[url] = entry;
                else
                    partial = true;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to fetch image: {Url}", url);
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
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        var mimeType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (string.IsNullOrEmpty(mimeType))
        {
            var ext = Path.GetExtension(new Uri(url).AbsolutePath);
            mimeType = MimeByExtension.GetValueOrDefault(ext, "application/octet-stream");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var memStream = new MemoryStream();

        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, 81920), cancellationToken)) > 0)
                await memStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
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

        var ext2 = pngConversionRequired
            ? ".png"
            : ExtensionByMime.GetValueOrDefault(outputMime, ".bin");

        return new ImageEntry
        {
            Index = index,
            LocalName = $"image-{index:D4}{ext2}",
            OriginalUrl = url,
            OriginalMimeType = mimeType,
            Alt = alt,
            SizeBytes = outputBytes.Length,
            Data = Convert.ToBase64String(outputBytes)
        };
    }

    private byte[] ConvertToPng(byte[] inputBytes)
    {
        using var skBitmap = SKBitmap.Decode(inputBytes);
        if (skBitmap == null)
            return inputBytes;

        using var image = SKImage.FromBitmap(skBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

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

            try
            {
                using var response = await _httpClient.GetAsync(abs, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode)
                    continue;

                var mimeType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                if (string.IsNullOrEmpty(mimeType))
                {
                    var ext = Path.GetExtension(abs.AbsolutePath);
                    mimeType = MimeByExtension.GetValueOrDefault(ext, "application/octet-stream");
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var memStream = new MemoryStream();
                var buffer = ArrayPool<byte>.Shared.Rent(81920);
                try
                {
                    int read;
                    while ((read = await stream.ReadAsync(buffer.AsMemory(0, 81920), cancellationToken)) > 0)
                        await memStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }

                var bytes = memStream.ToArray();
                if (totalBytes + bytes.Length > _maxEmbedBytes)
                {
                    truncated = true;
                    break;
                }

                totalBytes += bytes.Length;
                var dataUri = $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";
                img.SetAttribute("src", dataUri);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to embed image: {Url}", abs);
            }
        }

        return (document.DocumentElement.OuterHtml, truncated);
    }
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
}
