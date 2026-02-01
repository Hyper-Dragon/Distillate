namespace Distillate.Core.Extraction;

public record ExtractionResult
{
    public bool Success { get; init; }
    public string? Title { get; init; }
    public string? CleanHtml { get; init; }
    public string? RawText { get; init; }
    public string? Error { get; init; }
    public string? Byline { get; init; }
    public string? Author { get; init; }
    public string? Excerpt { get; init; }
    public string? SiteName { get; init; }
    public string? Language { get; init; }
    public DateTime? PublicationDate { get; init; }
    public string? FeaturedImage { get; init; }
    public TimeSpan? TimeToRead { get; init; }
}
