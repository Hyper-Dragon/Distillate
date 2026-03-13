using System.Text.Json.Serialization;

namespace Distillate.Core.Models;

public record ProcessResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("requestedUrl")]
    public required string RequestedUrl { get; init; }

    [JsonPropertyName("processedAt")]
    public DateTimeOffset ProcessedAt { get; init; }

    [JsonPropertyName("processingTimeMs")]
    public double ProcessingTimeMs { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("byline")]
    public string? Byline { get; init; }

    [JsonPropertyName("author")]
    public string? Author { get; init; }

    [JsonPropertyName("excerpt")]
    public string? Excerpt { get; init; }

    [JsonPropertyName("siteName")]
    public string? SiteName { get; init; }

    [JsonPropertyName("language")]
    public string? Language { get; init; }

    [JsonPropertyName("publicationDate")]
    public DateTime? PublicationDate { get; init; }

    [JsonPropertyName("featuredImage")]
    public string? FeaturedImage { get; init; }

    [JsonPropertyName("timeToRead")]
    public string? TimeToRead { get; init; }

    [JsonPropertyName("completionFlags")]
    public int CompletionFlags { get; init; }

    [JsonPropertyName("errors")]
    public string[]? Errors { get; init; }

    [JsonPropertyName("traceId")]
    public string? TraceId { get; init; }

    // --- Content outputs ---

    [JsonPropertyName("plainText")]
    public string? PlainText { get; init; }

    [JsonPropertyName("sourceHtml")]
    public string? SourceHtml { get; init; }

    [JsonPropertyName("cleanHtmlEmbeddedImages")]
    public string? CleanHtmlEmbeddedImages { get; init; }

    [JsonPropertyName("cleanHtmlLinkedImages")]
    public string? CleanHtmlLinkedImages { get; init; }

    [JsonPropertyName("cleanHtmlNoImages")]
    public string? CleanHtmlNoImages { get; init; }

    [JsonPropertyName("markdownLinkedImages")]
    public string? MarkdownLinkedImages { get; init; }

    [JsonPropertyName("markdownNoImages")]
    public string? MarkdownNoImages { get; init; }

    // --- Link outputs ---

    [JsonPropertyName("allLinksInPage")]
    public string[]? AllLinksInPage { get; init; }

    [JsonPropertyName("allLinksMedia")]
    public string[]? AllLinksMedia { get; init; }

    [JsonPropertyName("allLinksAssets")]
    public string[]? AllLinksAssets { get; init; }

    // --- Image outputs ---

    [JsonPropertyName("allInPageImages")]
    public ImageEntry[]? AllInPageImages { get; init; }

    [JsonPropertyName("allOutOfPageImages")]
    public ImageEntry[]? AllOutOfPageImages { get; init; }

    // --- Debug ---

    [JsonPropertyName("debugInfo")]
    public DebugInfo? DebugInfo { get; init; }
}

public record ImageEntry
{
    /// <summary>Zero-based index, unique per source URL within its array.</summary>
    [JsonPropertyName("index")]
    public int Index { get; init; }

    /// <summary>Authoritative local filename: image-{index:D4}.{ext}</summary>
    [JsonPropertyName("localName")]
    public required string LocalName { get; init; }

    /// <summary>Original absolute source URL of the image.</summary>
    [JsonPropertyName("originalUrl")]
    public string? OriginalUrl { get; init; }

    /// <summary>MIME type detected from HTTP response headers.</summary>
    [JsonPropertyName("originalMimeType")]
    public string? OriginalMimeType { get; init; }

    /// <summary>Alt text from the first occurrence of this URL in document order.</summary>
    [JsonPropertyName("alt")]
    public string? Alt { get; init; }

    /// <summary>Byte length of data after any conversion.</summary>
    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; init; }

    /// <summary>Base64-encoded image bytes (PNG if pngConversionRequired, else original format).</summary>
    [JsonPropertyName("data")]
    public string? Data { get; init; }
}

[Flags]
public enum CompletionFlag
{
    Complete = 0,
    ImageTruncated = 1,
    SourceHtmlOmitted = 2,
    EngineFallbackUsed = 4,
    ImageLinkRewriteApplied = 8,
    ImageLinkRewriteImpliedInPageImages = 16,
    AllInPageImagesPartial = 32,
    AllOutOfPageImagesPartial = 64
}

