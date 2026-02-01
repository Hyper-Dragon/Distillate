using Microsoft.Extensions.Logging;
using SmartReader;

namespace Distillate.Core.Extraction;

public class SmartReaderEngine(ILogger<SmartReaderEngine> logger) : IReaderEngine
{
    public string Name => "smartreader";

    public ExtractionResult Extract(string html, Uri baseUri)
    {
        try
        {
            var reader = new Reader(baseUri.AbsoluteUri, html)
            {
                // Widen candidate pool to capture more content nodes
                NTopCandidates = 10,
                // Always return best-effort content even if confidence is low
                ContinueIfNotReadable = true,
                // Keep classes to allow downstream HtmlArticleCleaner CSS-selector cleanup
                KeepClasses = true,
            };
            var article = reader.GetArticle();

            if (!article.IsReadable && string.IsNullOrWhiteSpace(article.Content))
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
                RawText = article.TextContent?.Trim(),
                Byline = article.Byline,
                Author = article.Author,
                Excerpt = article.Excerpt,
                SiteName = article.SiteName,
                Language = article.Language,
                PublicationDate = article.PublicationDate,
                FeaturedImage = article.FeaturedImage,
                TimeToRead = article.TimeToRead,
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SmartReader extraction failed");
            return new ExtractionResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }
}
