using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using ASharpConfig = AngleSharp.Configuration;

namespace Distillate.Core.Conversion;

/// <summary>
/// DOM-based article HTML cleaner. Uses structural and semantic rules to strip
/// non-article elements (image credits, social links, newsletter promos, related
/// stories, tag clouds, comment counts, etc.) from extracted article HTML.
///
/// This mirrors what Firefox Reader View does: clean the DOM before producing
/// any text output. All decisions are based on element structure, roles, classes,
/// IDs, and link density — not on matching specific words in content.
/// </summary>
public static partial class HtmlArticleCleaner
{
    // CSS selectors for elements that are structurally non-article content.
    // These target semantic/structural patterns, not specific text.
    private static readonly string[] RemoveSelectors =
    [
        // Image captions and credits (figcaption inside figure with only img)
        "figcaption",

        // Asides, promos, callouts
        "aside",
        "[role='complementary']",
        "[role='banner']",
        "[role='navigation']",
        "[role='contentinfo']",
        "[data-component='newsletter-promo']",
        "[data-component='links-block']",
        "[data-component='topic-list']",
        "[data-component='tag-list']",
        "[data-component='related-stories']",
        "[data-component='see-alsos']",

        // Common class patterns for non-content (structural inference)
        ".social-share", ".share-tools", ".share-buttons", ".sharing",
        ".share-bar", ".share-links", ".social-links", ".social-media-links",
        ".newsletter-promo", ".newsletter-signup", ".email-signup",
        ".newsletter", ".mailing-list", ".subscribe",
        ".related-content", ".related-stories", ".related-posts", ".related-links",
        ".more-on-this-story", ".story-more",
        ".tag-list", ".topic-list", ".tags", ".topics", ".categories",
        ".post-tags", ".entry-tags", ".post-categories",
        ".comments-link", ".comment-count",
        ".image-caption", ".media-caption", ".image-credit", ".media-credit",
        ".visually-hidden", ".sr-only",
        ".ad", ".ads", ".advertisement", ".advert",
        ".popup", ".modal", ".overlay",
        ".skip-link",
        ".author-bio", ".author-info",

        // Common ID patterns
        "#comments", "#comment-section",
        "#newsletter-signup", "#email-signup", "#mail-title",
        "#related-content", "#related-stories",
        "#share-tools",
        "#topics", "#tags",

        // Navigation that may be inside article
        "nav",
    ];

    /// <summary>
    /// Cleans article HTML by removing non-content elements using DOM structure.
    /// Returns cleaned HTML fragment (no html/head/body wrapper).
    /// </summary>
    public static string Clean(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return html;

        var config = ASharpConfig.Default;
        var context = BrowsingContext.New(config);
        var parser = context.GetService<IHtmlParser>()!;
        var doc = parser.ParseDocument(html);

        // Phase 1: Remove HTML comments (ad markers, CMS junk)
        RemoveComments(doc);

        // Phase 2: Remove elements matching structural selectors
        RemoveBySelectors(doc);

        // Phase 3: Remove figures that contain only images (no meaningful text)
        RemoveImageOnlyFigures(doc);

        // Phase 4: Remove link-dense containers (tag clouds, "Most viewed", social blocks)
        RemoveLinkDenseBlocks(doc);

        // Phase 5: Remove elements that look like "related stories" sections
        // (heading followed by a list of links, typically at end of article)
        RemoveRelatedSections(doc);

        // Phase 6: Remove share/social paragraphs by text pattern
        RemoveShareParagraphs(doc);

        // Phase 7: Remove mailing list / newsletter blocks by text pattern
        RemoveMailingListBlocks(doc);

        // Phase 8: Remove empty elements left behind by cleaning
        RemoveEmptyElements(doc);

        return OutputCleaner.UnwrapHtmlDocument(doc.DocumentElement.OuterHtml);
    }

    /// <summary>
    /// Extracts clean plain text from HTML with proper block-level separation.
    /// Unlike Element.TextContent which concatenates everything, this inserts
    /// newlines between block-level elements so paragraphs don't run together.
    /// </summary>
    public static string ExtractPlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        // First clean the HTML
        var cleanedHtml = Clean(html);

        var config = ASharpConfig.Default;
        var context = BrowsingContext.New(config);
        var parser = context.GetService<IHtmlParser>()!;
        var doc = parser.ParseDocument(cleanedHtml);

        var body = doc.Body;
        if (body == null)
            return string.Empty;

        var lines = new List<string>();
        ExtractTextFromNode(body, lines);

        // Join with newlines and normalize
        var result = string.Join("\n", lines
            .Select(l => l.Trim())
            .Where(l => l.Length > 0));

        // Collapse 3+ consecutive newlines to 2
        result = OutputCleaner.CollapseExcessiveNewlines(result);

        return result.Trim();
    }

    private static void RemoveBySelectors(IDocument doc)
    {
        foreach (var selector in RemoveSelectors)
        {
            try
            {
                foreach (var el in doc.QuerySelectorAll(selector).ToList())
                    el.Remove();
            }
            catch
            {
                // Invalid selector for this document, skip
            }
        }
    }

    private static void RemoveImageOnlyFigures(IDocument doc)
    {
        foreach (var figure in doc.QuerySelectorAll("figure").ToList())
        {
            // If the figure has no meaningful text content after removing img/picture/source/svg
            var textWithoutMedia = GetTextWithoutMedia(figure);
            if (string.IsNullOrWhiteSpace(textWithoutMedia))
            {
                figure.Remove();
            }
        }
    }

    private static string GetTextWithoutMedia(IElement element)
    {
        // Clone the element and strip media to check remaining text
        var clone = element.Clone(true) as IElement;
        if (clone == null) return string.Empty;

        foreach (var media in clone.QuerySelectorAll("img, picture, source, svg, figcaption").ToList())
            media.Remove();

        return clone.TextContent?.Trim() ?? string.Empty;
    }

    private static void RemoveLinkDenseBlocks(IDocument doc)
    {
        // Find containers where link text dominates total text (> 70%)
        // These are typically tag clouds, social sharing, "most viewed" blocks
        var candidates = doc.QuerySelectorAll("div, section, ul, ol, p").ToList();

        foreach (var el in candidates)
        {
            // Skip elements with lots of content (article body)
            var totalText = el.TextContent?.Trim() ?? string.Empty;
            if (totalText.Length > 500 || totalText.Length < 10)
                continue;

            var links = el.QuerySelectorAll("a");
            if (!links.Any())
                continue;

            var linkTextLength = links.Sum(a => (a.TextContent?.Trim() ?? string.Empty).Length);
            var totalTextLength = totalText.Length;

            if (totalTextLength > 0 && (double)linkTextLength / totalTextLength > 0.7)
            {
                // High link density - likely navigation/tags/social, not article prose
                // But only if it has multiple links (a single link in a paragraph is fine)
                if (links.Length >= 3)
                {
                    el.Remove();
                }
            }
        }
    }

    private static void RemoveRelatedSections(IDocument doc)
    {
        // Look for heading + list-of-links patterns at the end of the document
        // "Related topics", "More on this story", "Most viewed", "Explore more on these topics"
        var headings = doc.QuerySelectorAll("h2, h3, h4, h5, h6").ToList();

        foreach (var heading in headings)
        {
            // Check if this heading's subsequent siblings are primarily link lists
            var sibling = heading.NextElementSibling;
            if (sibling == null) continue;

            // Check if the section after the heading is link-dense
            int siblingCount = 0;
            int totalTextLen = 0;
            int linkTextLen = 0;
            var siblingsToRemove = new List<IElement>();

            var current = sibling;
            while (current != null && siblingCount < 10)
            {
                var text = current.TextContent?.Trim() ?? string.Empty;
                totalTextLen += text.Length;
                linkTextLen += current.QuerySelectorAll("a")
                    .Sum(a => (a.TextContent?.Trim() ?? string.Empty).Length);

                siblingsToRemove.Add(current);
                siblingCount++;
                current = current.NextElementSibling;
            }

            // If the section after the heading is 60%+ links, remove heading + section
            if (totalTextLen > 0 && (double)linkTextLen / totalTextLen > 0.6 && siblingCount >= 1)
            {
                heading.Remove();
                foreach (var s in siblingsToRemove)
                    s.Remove();
            }
        }
    }

    private static void RemoveEmptyElements(IDocument doc)
    {
        // Remove empty block elements left by cleaning (skip br, hr, img)
        var emptyBlocks = doc.QuerySelectorAll("div, section, span, p, ul, ol, figure, aside").ToList();
        foreach (var el in emptyBlocks)
        {
            if (string.IsNullOrWhiteSpace(el.TextContent) &&
                !el.QuerySelectorAll("img, video, audio, iframe, svg").Any())
            {
                el.Remove();
            }
        }
    }

    private static void RemoveComments(IDocument doc)
    {
        var comments = doc.Descendants<IComment>().ToList();
        foreach (var comment in comments)
            comment.Remove();
    }

    private static void RemoveShareParagraphs(IDocument doc)
    {
        // Remove short paragraphs/divs whose text is a share/social prompt
        foreach (var el in doc.QuerySelectorAll("p, div, span").ToList())
        {
            var text = el.TextContent?.Trim() ?? string.Empty;
            if (text.Length > 0 && text.Length < 50 &&
                SharePatternRegex().IsMatch(text))
            {
                el.Remove();
            }
        }
    }

    private static void RemoveMailingListBlocks(IDocument doc)
    {
        // Remove containers that look like newsletter/mailing list signups
        // Look for short text + form, or heading with "mailing list"/"newsletter"/"subscribe"
        foreach (var el in doc.QuerySelectorAll("div, section, form, aside").ToList())
        {
            var text = el.TextContent?.Trim() ?? string.Empty;
            if (text.Length > 0 && text.Length < 200 &&
                MailingListPatternRegex().IsMatch(text))
            {
                el.Remove();
            }
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^(share\s+this|share\s*:\s*$|share\s+on|follow\s+us|share\s+this\s+(post|article|story))", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex SharePatternRegex();

    [System.Text.RegularExpressions.GeneratedRegex(@"(join\s+our\s+mailing|subscribe\s+to\s+our|sign\s+up\s+for\s+our|newsletter\s+sign\s*up|get\s+our\s+newsletter|keep\s+informed\s+about)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex MailingListPatternRegex();

    // Block-level elements that should produce line breaks in plain text
    private static readonly HashSet<string> BlockElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "section", "article", "header", "footer",
        "h1", "h2", "h3", "h4", "h5", "h6",
        "ul", "ol", "li",
        "blockquote", "pre",
        "table", "tr", "thead", "tbody",
        "br", "hr",
        "figure", "figcaption",
        "details", "summary",
        "address", "dd", "dt", "dl"
    };

    private static void ExtractTextFromNode(INode node, List<string> lines)
    {
        foreach (var child in node.ChildNodes)
        {
            if (child is IText textNode)
            {
                var text = textNode.Data;
                // Normalize whitespace but preserve content
                text = text.Replace('\u00A0', ' ');

                if (!string.IsNullOrWhiteSpace(text))
                {
                    // Add to current line or append
                    if (lines.Count == 0)
                        lines.Add(text);
                    else
                        lines[^1] += text;
                }
            }
            else if (child is IElement element)
            {
                var tag = element.LocalName;

                if (tag is "script" or "style" or "noscript")
                    continue;

                bool isBlock = BlockElements.Contains(tag);

                if (isBlock)
                {
                    // Start a new line for block elements
                    lines.Add(string.Empty);
                }

                ExtractTextFromNode(element, lines);

                if (isBlock)
                {
                    // End with a new line after block elements
                    lines.Add(string.Empty);
                }
            }
        }
    }
}
