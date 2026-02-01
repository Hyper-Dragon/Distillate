using System.Text.Json.Serialization;

namespace Distillate.Core.Models;

/// <summary>
/// Optional diagnostic payload returned when includeDebugInfo=true.
/// Contains per-stage wall-clock timings and a log of every image
/// fetch attempt so callers can see exactly what took time and why.
/// </summary>
public record DebugInfo
{
    /// <summary>Wall-clock time for each named processing stage, in order.</summary>
    [JsonPropertyName("stages")]
    public StageTiming[] Stages { get; init; } = [];

    /// <summary>One entry per image URL attempted during this request.</summary>
    [JsonPropertyName("imageFetches")]
    public ImageFetchLog[] ImageFetches { get; init; } = [];

    /// <summary>Byte length of the raw browser-captured HTML.</summary>
    [JsonPropertyName("sourceHtmlBytes")]
    public int SourceHtmlBytes { get; init; }

    /// <summary>Byte length of the reader-extracted clean HTML after HtmlArticleCleaner.</summary>
    [JsonPropertyName("cleanHtmlBytes")]
    public int CleanHtmlBytes { get; init; }
}

public record StageTiming
{
    [JsonPropertyName("stage")]
    public required string Stage { get; init; }

    [JsonPropertyName("ms")]
    public double Ms { get; init; }
}

public record ImageFetchLog
{
    /// <summary>Absolute URL that was requested.</summary>
    [JsonPropertyName("url")]
    public required string Url { get; init; }

    /// <summary>Which image pool this fetch belongs to: inPage, outOfPage, embed, featured.</summary>
    [JsonPropertyName("pool")]
    public required string Pool { get; init; }

    /// <summary>Outcome: ok | timeout | httpError | exception | skipped.</summary>
    [JsonPropertyName("outcome")]
    public required string Outcome { get; init; }

    /// <summary>HTTP status code, when a response was received.</summary>
    [JsonPropertyName("httpStatus")]
    public int? HttpStatus { get; init; }

    /// <summary>Response body size in bytes for successful fetches.</summary>
    [JsonPropertyName("bytes")]
    public long? Bytes { get; init; }

    /// <summary>Wall-clock time for this fetch in milliseconds.</summary>
    [JsonPropertyName("ms")]
    public double Ms { get; init; }
}
