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

    [JsonPropertyName("errors")]
    public string[]? Errors { get; init; }

    [JsonPropertyName("traceId")]
    public string? TraceId { get; init; }
}

[Flags]
public enum CompletionFlag
{
    Complete = 0,
    ImageTruncated = 1,
    SourceHtmlOmitted = 2,
    EngineFallbackUsed = 4
}
