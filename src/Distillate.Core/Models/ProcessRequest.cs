using System.Text.Json.Serialization;

namespace Distillate.Core.Models;

public record ProcessRequest
{
    [JsonPropertyName("url")]
    public required string Url { get; init; }

    [JsonPropertyName("engine")]
    public string Engine { get; init; } = "smartreader";

    [JsonPropertyName("timeoutMs")]
    public int? TimeoutMs { get; init; }

    [JsonPropertyName("stabilizationMs")]
    public int? StabilizationMs { get; init; }

    [JsonPropertyName("userAgent")]
    public string? UserAgent { get; init; }

    [JsonPropertyName("referer")]
    public string? Referer { get; init; }

    [JsonPropertyName("cookies")]
    public CookieEntry[]? Cookies { get; init; }

    // --- Content output flags ---

    [JsonPropertyName("includeSourceHtml")]
    public bool IncludeSourceHtml { get; init; } = false;

    [JsonPropertyName("includeCleanHtmlEmbeddedImages")]
    public bool IncludeCleanHtmlEmbeddedImages { get; init; } = false;

    [JsonPropertyName("includeCleanHtmlLinkedImages")]
    public bool IncludeCleanHtmlLinkedImages { get; init; } = false;

    [JsonPropertyName("includeCleanHtmlNoImages")]
    public bool IncludeCleanHtmlNoImages { get; init; } = false;

    [JsonPropertyName("includeMarkdownLinkedImages")]
    public bool IncludeMarkdownLinkedImages { get; init; } = false;

    [JsonPropertyName("includeMarkdownNoImages")]
    public bool IncludeMarkdownNoImages { get; init; } = true;

    [JsonPropertyName("includePlainText")]
    public bool IncludePlainText { get; init; } = true;

    // --- Link extraction flags ---

    [JsonPropertyName("includeAllLinksInPage")]
    public bool IncludeAllLinksInPage { get; init; } = false;

    [JsonPropertyName("includeAllLinksMedia")]
    public bool IncludeAllLinksMedia { get; init; } = false;

    [JsonPropertyName("includeAllLinksAssets")]
    public bool IncludeAllLinksAssets { get; init; } = false;

    // --- Image extraction flags ---

    [JsonPropertyName("includeAllInPageImages")]
    public bool IncludeAllInPageImages { get; init; } = false;

    [JsonPropertyName("includeAllOutOfPageImages")]
    public bool IncludeAllOutOfPageImages { get; init; } = false;

    // --- Image processing options ---

    /// <summary>
    /// Rewrite img src in cleanHtmlLinkedImages and markdownLinkedImages to local
    /// filenames (image-0000.ext). Implicitly forces includeAllInPageImages=true.
    /// </summary>
    [JsonPropertyName("imageLinkRewrite")]
    public bool ImageLinkRewrite { get; init; } = false;

    /// <summary>
    /// Convert all fetched images to PNG using SkiaSharp.
    /// When false, images are returned in their original format.
    /// </summary>
    [JsonPropertyName("pngConversionRequired")]
    public bool PngConversionRequired { get; init; } = false;

    /// <summary>
    /// Controls how Unicode typographic characters (smart quotes, dashes, ellipsis)
    /// are handled in text and markdown outputs.
    /// "preserve" (default) keeps them as-is; "ascii" converts to plain ASCII;
    /// "latex" converts to LaTeX-idiomatic sequences (e.g. em-dash → ---).
    /// </summary>
    [JsonPropertyName("unicodeNormalization")]
    public UnicodeNormalizationMode UnicodeNormalization { get; init; } = UnicodeNormalizationMode.Preserve;

    /// <summary>
    /// When true, a debugInfo object is included in the response containing
    /// per-stage wall-clock timings and a log of every image fetch (URL, outcome,
    /// HTTP status, bytes, ms). Useful for diagnosing slow or hanging requests.
    /// Default false — no overhead when not requested.
    /// </summary>
    [JsonPropertyName("includeDebugInfo")]
    public bool IncludeDebugInfo { get; init; } = false;
}

public record CookieEntry
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("value")]
    public required string Value { get; init; }

    [JsonPropertyName("domain")]
    public string? Domain { get; init; }

    [JsonPropertyName("path")]
    public string? Path { get; init; }
}

