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

    [JsonPropertyName("rawText")]
    public string? RawText { get; init; }

    [JsonPropertyName("markdown")]
    public string? Markdown { get; init; }

    [JsonPropertyName("cleanHtml")]
    public string? CleanHtml { get; init; }

    [JsonPropertyName("sourceHtml")]
    public string? SourceHtml { get; init; }

    [JsonPropertyName("completionFlags")]
    public int CompletionFlags { get; init; }

    [JsonPropertyName("images")]
    public ExtractedImage[]? Images { get; init; }

    [JsonPropertyName("errors")]
    public string[]? Errors { get; init; }

    [JsonPropertyName("traceId")]
    public string? TraceId { get; init; }
}

public record ExtractedImage
{
    /// <summary>Original source URL of the image.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    /// <summary>Alt text from the img element, if present.</summary>
    [JsonPropertyName("alt")]
    public string? Alt { get; init; }

    /// <summary>MIME type of the image (e.g. image/png, image/jpeg, image/webp).</summary>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; init; }

    /// <summary>Base64-encoded image bytes.</summary>
    [JsonPropertyName("data")]
    public string? Data { get; init; }

    /// <summary>Size of the image in bytes.</summary>
    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; init; }

    /// <summary>Zero-based index of this image in the page.</summary>
    [JsonPropertyName("index")]
    public int Index { get; init; }
}

[Flags]
public enum CompletionFlag
{
    Complete = 0,
    ImageTruncated = 1,
    SourceHtmlOmitted = 2,
    EngineFallbackUsed = 4
}
