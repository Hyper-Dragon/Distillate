namespace Distillate.Core.Extraction;

public record ExtractionResult
{
    public bool Success { get; init; }
    public string? Title { get; init; }
    public string? CleanHtml { get; init; }
    public string? RawText { get; init; }
    public string? Error { get; init; }
}
