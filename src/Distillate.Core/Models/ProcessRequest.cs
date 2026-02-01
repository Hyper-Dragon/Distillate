using System.Text.Json.Serialization;

namespace Distillate.Core.Models;

public record ProcessRequest
{
    [JsonPropertyName("url")]
    public required string Url { get; init; }

    [JsonPropertyName("engine")]
    public string Engine { get; init; } = "readability";

    [JsonPropertyName("includeImages")]
    public bool IncludeImages { get; init; } = true;

    [JsonPropertyName("includeSourceHtml")]
    public bool IncludeSourceHtml { get; init; } = false;

    [JsonPropertyName("timeoutMs")]
    public int? TimeoutMs { get; init; }

    [JsonPropertyName("stabilizationMs")]
    public int? StabilizationMs { get; init; }

    [JsonPropertyName("userAgent")]
    public string? UserAgent { get; init; }

    [JsonPropertyName("cookies")]
    public CookieEntry[]? Cookies { get; init; }
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
