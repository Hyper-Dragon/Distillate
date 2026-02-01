using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;

namespace Distillate.Core.Extraction;

public class ReadabilityEngine : IReaderEngine
{
    private readonly ILogger<ReadabilityEngine> _logger;

    public string Name => "readability";

    public ReadabilityEngine(ILogger<ReadabilityEngine> logger)
    {
        _logger = logger;
    }

    public ExtractionResult Extract(string html, Uri baseUri)
    {
        try
        {
            var config = AngleSharp.Configuration.Default;
            var context = BrowsingContext.New(config);
            var parser = context.GetService<IHtmlParser>()!;
            var document = parser.ParseDocument(html);

            RemoveUnwantedElements(document);

            var article = FindArticleContent(document);
            if (article == null)
            {
                return new ExtractionResult
                {
                    Success = false,
                    Error = "Could not find article content"
                };
            }

            var title = ExtractTitle(document);
            
            ResolveRelativeUrls(article, baseUri);
            var cleanHtml = article.InnerHtml;
            var rawText = article.TextContent?.Trim();

            return new ExtractionResult
            {
                Success = true,
                Title = title,
                CleanHtml = cleanHtml,
                RawText = rawText
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Readability extraction failed");
            return new ExtractionResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    private static void RemoveUnwantedElements(IDocument document)
    {
        var selectorsToRemove = new[]
        {
            "script", "style", "noscript", "iframe", "nav", "header", "footer",
            "aside", ".sidebar", ".advertisement", ".ad", ".ads", ".social-share",
            ".comments", "#comments", ".related-posts", ".popup", ".modal"
        };

        foreach (var selector in selectorsToRemove)
        {
            try
            {
                var elements = document.QuerySelectorAll(selector).ToList();
                foreach (var element in elements)
                {
                    element.Remove();
                }
            }
            catch
            {
                // Invalid selector, skip
            }
        }
    }

    private static IElement? FindArticleContent(IDocument document)
    {
        var candidates = new[]
        {
            document.QuerySelector("article"),
            document.QuerySelector("[role='main']"),
            document.QuerySelector("main"),
            document.QuerySelector(".post-content"),
            document.QuerySelector(".article-content"),
            document.QuerySelector(".entry-content"),
            document.QuerySelector(".content"),
            document.QuerySelector("#content"),
            document.Body
        };

        foreach (var candidate in candidates)
        {
            if (candidate != null && !string.IsNullOrWhiteSpace(candidate.TextContent))
            {
                var textLength = candidate.TextContent.Length;
                if (textLength > 200)
                {
                    return candidate;
                }
            }
        }

        return document.Body;
    }

    private static string? ExtractTitle(IDocument document)
    {
        var ogTitle = document.QuerySelector("meta[property='og:title']")?.GetAttribute("content");
        if (!string.IsNullOrWhiteSpace(ogTitle))
            return ogTitle;

        var h1 = document.QuerySelector("article h1, main h1, h1");
        if (!string.IsNullOrWhiteSpace(h1?.TextContent))
            return h1.TextContent.Trim();

        return document.Title?.Trim();
    }

    private static void ResolveRelativeUrls(IElement element, Uri baseUri)
    {
        var links = element.QuerySelectorAll("a[href]");
        foreach (var link in links)
        {
            var href = link.GetAttribute("href");
            if (!string.IsNullOrEmpty(href) && Uri.TryCreate(baseUri, href, out var absoluteUri))
            {
                link.SetAttribute("href", absoluteUri.AbsoluteUri);
            }
        }

        var images = element.QuerySelectorAll("img[src]");
        foreach (var img in images)
        {
            var src = img.GetAttribute("src");
            if (!string.IsNullOrEmpty(src) && Uri.TryCreate(baseUri, src, out var absoluteUri))
            {
                img.SetAttribute("src", absoluteUri.AbsoluteUri);
            }
        }
    }
}
