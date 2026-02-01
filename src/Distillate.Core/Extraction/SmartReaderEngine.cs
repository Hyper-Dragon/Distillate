using Microsoft.Extensions.Logging;
using SmartReader;

namespace Distillate.Core.Extraction;

public class SmartReaderEngine : IReaderEngine
{
    private readonly ILogger<SmartReaderEngine> _logger;

    public string Name => "smartreader";

    public SmartReaderEngine(ILogger<SmartReaderEngine> logger)
    {
        _logger = logger;
    }

    public ExtractionResult Extract(string html, Uri baseUri)
    {
        try
        {
            var reader = new Reader(baseUri.AbsoluteUri, html);
            var article = reader.GetArticle();

            if (!article.IsReadable)
            {
                return new ExtractionResult
                {
                    Success = false,
                    Error = "SmartReader could not extract readable content"
                };
            }

            return new ExtractionResult
            {
                Success = true,
                Title = article.Title,
                CleanHtml = article.Content,
                RawText = article.TextContent?.Trim()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SmartReader extraction failed");
            return new ExtractionResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }
}
