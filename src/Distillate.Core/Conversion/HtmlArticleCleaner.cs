using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using ASharpConfig = AngleSharp.Configuration;

namespace Distillate.Core.Conversion;

/// <summary>
/// DOM-based article HTML cleaner. Implements the same strategies as
/// Mozilla Readability.js / Firefox Reader View:
///   1. Class/ID substring matching to remove "unlikely candidates"
///   2. Structural CSS-selector removal
///   3. Weight-based conditional pruning of low-quality nodes
///   4. Link-density analysis
///   5. Text-pattern detection for share/newsletter/donation blocks
/// </summary>
public static partial class HtmlArticleCleaner
{
    // ── Readability.js-style class/ID substring matching ──
    // Elements whose class or id contains these substrings are unlikely to be
    // article content. Ported from Readability.js UNLIKELY_CANDIDATES regex.
    [GeneratedRegex(
        @"banner|breadcrumb|combx|comment|community|cover-wrap|disqus|extra|" +
        @"footer|gdpr|header|legends|menu|related|remark|replies|rss|shoutbox|" +
        @"sidebar|skyscraper|social|sponsor|supplemental|ad-break|agegate|" +
        @"pagination|pager|popup|yom-resolve|donation|donate|newsletter|" +
        @"signup|subscribe|promo|widget|byline|metadata|meta-data|" +
        @"share|sharing|cookie|consent|masthead|nav",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UnlikelyCandidatesRegex();

    // Elements matching these substrings should be kept even if they also
    // match the unlikely pattern. From Readability.js OK_MAYBE_ITS_A_CANDIDATE.
    [GeneratedRegex(
        @"and|article|body|column|content|main|shadow|entry|post-body|" +
        @"hentry|h-entry|page|text|blog-post|story-body",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex MaybeCandidateRegex();

    // CSS selectors for elements that are structurally non-article content.
    private static readonly string[] RemoveSelectors =
    [
        // Image captions and credits
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

        // Navigation that may be inside article
        "nav",
        "footer",

        // Editorial pullquotes that duplicate article text
        "figure.wp-block-pullquote",
        ".pullquote",
        "[data-component='pull-quote']",

        // CMS metadata containers that aren't article body
        "[data-nosnippet]",
        ".article-meta",
        ".article-header-data",
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

        // Phase 2: Readability.js-style unlikely candidate removal
        // This is the KEY strategy — substring match class/id attributes
        RemoveUnlikelyCandidates(doc);

        // Phase 3: Remove elements matching structural selectors
        RemoveBySelectors(doc);

        // Phase 4: Remove figures that contain only images (no meaningful text)
        RemoveImageOnlyFigures(doc);

        // Phase 5: Weight-based conditional pruning (Readability.js _postProcessContent)
        // Removes div/section/ul/table/form nodes that fail content-quality checks
        ConditionallyPruneNodes(doc);

        // Phase 6: Remove link-dense containers (tag clouds, "Most viewed", social blocks)
        RemoveLinkDenseBlocks(doc);

        // Phase 7: Remove elements that look like "related stories" sections
        RemoveRelatedSections(doc);

        // Phase 8: Remove share/social paragraphs by text pattern
        RemoveShareParagraphs(doc);

        // Phase 9: Remove mailing list / newsletter / donation blocks by text pattern
        RemoveMailingListBlocks(doc);

        // Phase 10: Remove Wikipedia-style footnotes and reference lists
        RemoveFootnotesAndReferences(doc);

        // Phase 11: Remove "Related Blogs/Posts" listing sections by repeating structure
        RemoveRepeatingBlogListings(doc);

        // Phase 12: Remove orphaned byline/date elements at the start of the article
        RemoveLeadingBylineJunk(doc);

        // Phase 13: Remove image credit lines
        RemoveImageCredits(doc);

        // Phase 14: Remove trailing author bio after <hr>
        RemoveTrailingAuthorBio(doc);

        // Phase 15: Remove empty elements left behind by cleaning
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
        var candidates = doc.QuerySelectorAll("div, section, ul, ol, p").ToList();

        foreach (var el in candidates)
        {
            var totalText = el.TextContent?.Trim() ?? string.Empty;
            if (totalText.Length < 10)
                continue;

            var links = el.QuerySelectorAll("a");
            if (!links.Any())
                continue;

            var linkTextLength = links.Sum(a => (a.TextContent?.Trim() ?? string.Empty).Length);
            var totalTextLength = totalText.Length;

            // For larger blocks, require higher link density + more links
            double threshold = totalTextLength > 500 ? 0.5 : 0.7;
            int minLinks = totalTextLength > 500 ? 5 : 3;

            if (totalTextLength > 0 && (double)linkTextLength / totalTextLength > threshold
                && links.Length >= minLinks)
            {
                el.Remove();
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
        // Remove containers that look like newsletter/mailing list/donation signups
        foreach (var el in doc.QuerySelectorAll("div, section, form, aside, p").ToList())
        {
            var text = el.TextContent?.Trim() ?? string.Empty;
            if (text.Length > 0 && text.Length < 500 &&
                MailingListPatternRegex().IsMatch(text))
            {
                el.Remove();
            }
        }
    }

    [GeneratedRegex(@"^(share\s+this|share\s*:\s*$|share\s+on|follow\s+us|share\s+this\s+(post|article|story))", RegexOptions.IgnoreCase)]
    private static partial Regex SharePatternRegex();

    [GeneratedRegex(
        @"(join\s+our\s+(mailing|newsletter)|subscribe\s+to\s+our|sign\s+up\s+for\s+our|" +
        @"newsletter\s+sign\s*up|get\s+our\s+newsletter|keep\s+informed\s+about|" +
        @"join\s+our\s+newsletter|become\s+a\s+member|support\s+our\s+work|" +
        @"ability\s+to\s+carry\s+out\s+our\s+work\s+is\s+dependent\s+on|" +
        @"subscribe\s+to\s+our\s+(patreon|newsletter)|if\s+you\s+would\s+like\s+to\s+support)",
        RegexOptions.IgnoreCase)]
    private static partial Regex MailingListPatternRegex();

    // ── Core Readability.js strategy: Unlikely candidate removal ──

    private static void RemoveUnlikelyCandidates(IDocument doc)
    {
        // Walk all elements and remove those whose class/id substring-matches
        // the "unlikely candidates" pattern, UNLESS they also match the "maybe"
        // pattern or are essential structural elements.
        var preserveTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "html", "body", "article", "main", "section"
        };

        foreach (var el in doc.QuerySelectorAll("*").ToList())
        {
            if (preserveTags.Contains(el.LocalName))
                continue;

            var classId = $"{el.ClassName ?? ""} {el.Id ?? ""}";
            if (string.IsNullOrWhiteSpace(classId))
                continue;

            if (UnlikelyCandidatesRegex().IsMatch(classId) &&
                !MaybeCandidateRegex().IsMatch(classId))
            {
                el.Remove();
            }
        }
    }

    // ── Readability.js _postProcessContent: weight-based conditional pruning ──

    private static void ConditionallyPruneNodes(IDocument doc)
    {
        // Evaluate div/section/table/ul/form nodes and remove those that
        // fail content-quality heuristics. This catches the "Related Blogs"
        // type sections that have some text but aren't real article content.
        var conditionalTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "div", "section", "table", "ul", "ol", "form", "fieldset"
        };

        foreach (var el in doc.QuerySelectorAll("div, section, table, ul, ol, form, fieldset").ToList())
        {
            // Don't prune the outermost content wrapper
            if (el.ParentElement is { LocalName: "body" } && el.ParentElement.Children.Length <= 2)
                continue;

            var text = el.TextContent?.Trim() ?? string.Empty;
            if (text.Length == 0)
                continue;

            var links = el.QuerySelectorAll("a");
            var linkCount = links.Length;
            var linkDensity = text.Length > 0
                ? (double)links.Sum(a => (a.TextContent?.Trim() ?? "").Length) / text.Length
                : 0;

            var images = el.QuerySelectorAll("img").Length;
            var embeds = el.QuerySelectorAll("embed, object, iframe, video").Length;
            var paragraphs = el.QuerySelectorAll("p").Length;

            // Readability.js weight: positive for content-like classes, negative for junk-like
            var weight = GetElementWeight(el);

            // Remove node if it looks like junk:
            // - Negative weight (class/id suggests non-content)
            // - High link density with few images/embeds to justify it
            // - Very few paragraphs relative to text length
            if (weight < 0 && linkDensity > 0.3)
            {
                el.Remove();
            }
            else if (linkDensity > 0.5 && images < 2 && embeds == 0)
            {
                // High link density without visual content → likely related/nav
                el.Remove();
            }
        }
    }

    private static int GetElementWeight(IElement el)
    {
        var classId = $"{el.ClassName ?? ""} {el.Id ?? ""}";
        int weight = 0;

        if (PositiveWeightRegex().IsMatch(classId))
            weight += 25;
        if (NegativeWeightRegex().IsMatch(classId))
            weight -= 25;

        return weight;
    }

    [GeneratedRegex(
        @"article|body|content|entry|hentry|h-entry|main|page|post|text|blog|story",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PositiveWeightRegex();

    [GeneratedRegex(
        @"hidden|banner|combx|comment|com-|contact|foot|footer|footnote|" +
        @"gdpr|masthead|media|meta|outbrain|promo|related|scroll|share|" +
        @"shoutbox|sidebar|skyscraper|sponsor|shopping|tags|tool|widget|" +
        @"donate|donation|newsletter|signup|subscribe|social|nav",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex NegativeWeightRegex();

    // ── Phase 10: Wikipedia-style footnotes and reference lists ──

    private static void RemoveFootnotesAndReferences(IDocument doc)
    {
        // Remove reference list sections (Wikipedia, academic sites)
        // These are typically: ol with class "references", div with class "reflist",
        // or elements with id matching "references", "footnotes", "citations"
        var refSelectors = new[]
        {
            "ol.references", ".reflist", ".refbegin",
            "#references", "#footnotes", "#citations", "#notes",
            ".references", ".footnotes", ".citations",
            ".mw-references-wrap", // MediaWiki specific
            "[role='doc-endnotes']",
        };

        foreach (var selector in refSelectors)
        {
            try
            {
                foreach (var el in doc.QuerySelectorAll(selector).ToList())
                    el.Remove();
            }
            catch { }
        }

        // Remove "References" / "Notes" / "See also" / "External links" headings
        // and everything after them (common Wikipedia pattern)
        var trailingHeadings = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "References", "Notes", "Footnotes", "Citations",
            "See also", "External links", "Further reading",
            "Bibliography", "Sources"
        };

        foreach (var heading in doc.QuerySelectorAll("h2, h3").ToList())
        {
            var headingText = heading.TextContent?.Trim() ?? "";
            // Strip Wikipedia edit links: "References[edit]" → "References"
            var cleanHeading = EditBracketRegex().Replace(headingText, "").Trim();

            if (trailingHeadings.Contains(cleanHeading))
            {
                // Remove this heading and all subsequent siblings
                var toRemove = new List<IElement> { heading };
                var next = heading.NextElementSibling;
                while (next != null)
                {
                    // Stop if we hit another h2 (new major section)
                    if (next.LocalName == "h2" && heading.LocalName == "h2")
                        break;
                    toRemove.Add(next);
                    next = next.NextElementSibling;
                }
                foreach (var el in toRemove)
                    el.Remove();
            }
        }

        // Remove inline sup elements that are just footnote markers: [1], [2], etc.
        foreach (var sup in doc.QuerySelectorAll("sup").ToList())
        {
            var text = sup.TextContent?.Trim() ?? "";
            if (FootnoteMarkerRegex().IsMatch(text))
                sup.Remove();
        }
    }

    [GeneratedRegex(@"\[edit\]$", RegexOptions.IgnoreCase)]
    private static partial Regex EditBracketRegex();

    [GeneratedRegex(@"^\[[\d\w]+\]$")]
    private static partial Regex FootnoteMarkerRegex();

    // ── Phase 11: Repeating blog listing sections ──

    private static void RemoveRepeatingBlogListings(IDocument doc)
    {
        // Detect sections that contain a repeating pattern of:
        // heading + date/author + excerpt + "Find Out More" / "Read More" link
        // This catches OpenRights-style "Related Blogs" that have enough text
        // to pass link-density checks but are clearly listings, not article content.

        // Strategy: find any heading whose text matches "Related" patterns
        var relatedHeadingPatterns = new[]
        {
            "Related Blogs", "Related Posts", "Related Articles", "Related Stories",
            "More from", "You might also like", "Recommended", "Also on",
            "More stories", "Latest posts", "Recent posts",
        };

        foreach (var heading in doc.QuerySelectorAll("h2, h3, h4").ToList())
        {
            var headingText = heading.TextContent?.Trim() ?? "";
            bool isRelatedHeading = relatedHeadingPatterns.Any(p =>
                headingText.Contains(p, StringComparison.OrdinalIgnoreCase));

            if (!isRelatedHeading)
                continue;

            // Remove this heading and everything after it
            var toRemove = new List<IElement> { heading };
            var next = heading.NextElementSibling;
            while (next != null)
            {
                toRemove.Add(next);
                next = next.NextElementSibling;
            }
            foreach (var el in toRemove)
                el.Remove();
        }

        // Also detect "Find Out More" link patterns repeated 3+ times
        // (strong signal of a blog listing even without an obvious heading)
        var findMoreLinks = doc.QuerySelectorAll("a").ToList()
            .Where(a => FindOutMoreRegex().IsMatch(a.TextContent?.Trim() ?? ""))
            .ToList();

        if (findMoreLinks.Count >= 3)
        {
            // These are listing links — remove each one's parent container
            foreach (var link in findMoreLinks)
            {
                // Walk up to find a container div/section/article
                var parent = link.ParentElement;
                while (parent != null && parent.LocalName is not ("div" or "section" or "article")
                       && parent.LocalName != "body")
                {
                    parent = parent.ParentElement;
                }
                if (parent != null && parent.LocalName != "body")
                    parent.Remove();
            }
        }
    }

    [GeneratedRegex(@"^(Find Out More|Read More|Learn More|Continue Reading)$", RegexOptions.IgnoreCase)]
    private static partial Regex FindOutMoreRegex();

    // ── Phase 12: Orphaned byline/date elements at start ──

    private static void RemoveLeadingBylineJunk(IDocument doc)
    {
        var body = doc.Body;
        if (body == null) return;

        // Check the first few elements for byline/date patterns
        // e.g. "By\nPublished Mar 12, 2026, 3:53 PM EDT"
        var children = body.Children.ToList();
        int removed = 0;
        foreach (var child in children)
        {
            if (removed >= 3) break; // Don't strip more than 3 leading elements

            var text = child.TextContent?.Trim() ?? "";
            if (text.Length == 0)
            {
                child.Remove();
                removed++;
                continue;
            }
            if (text.Length > 150) break; // Reached real content

            if (BylineDateRegex().IsMatch(text))
            {
                child.Remove();
                removed++;
            }
            else
            {
                break; // Stop at first non-byline element
            }
        }
    }

    [GeneratedRegex(
        @"^(By\s*$|By\s+\w|Published\s|Posted\s|Updated\s|\d{1,2}\s+(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec))",
        RegexOptions.IgnoreCase)]
    private static partial Regex BylineDateRegex();

    // ── Phase 13: Image credit lines ──

    private static void RemoveImageCredits(IDocument doc)
    {
        // Remove short elements that are image/illustration credits
        foreach (var el in doc.QuerySelectorAll("p, div, span, figcaption").ToList())
        {
            var text = el.TextContent?.Trim() ?? "";
            if (text.Length > 0 && text.Length < 100 &&
                ImageCreditRegex().IsMatch(text))
            {
                el.Remove();
            }
        }
    }

    [GeneratedRegex(
        @"^(Illustration:\s|Photo:\s|Image:\s|Credit:\s|Photo\s+by\s|Image\s+(source|credit)|" +
        @"Photograph:\s|Getty\s+Images|Associated\s+Press|Reuters|AP\s+Photo)",
        RegexOptions.IgnoreCase)]
    private static partial Regex ImageCreditRegex();

    // ── Phase 14: Trailing author bio after <hr> ──

    private static void RemoveTrailingAuthorBio(IDocument doc)
    {
        // Pattern: an <hr> followed by 1-2 short italic/em paragraphs at the
        // end of the article body (common blog/opinion bio pattern).
        var body = doc.Body;
        if (body == null) return;

        // Find all <hr> elements — check if any is followed only by short
        // bio-like paragraphs until end of container
        foreach (var hr in doc.QuerySelectorAll("hr").ToList())
        {
            var parent = hr.ParentElement;
            if (parent == null) continue;

            var siblings = parent.Children.ToList();
            int hrIndex = siblings.IndexOf(hr);
            if (hrIndex < 0) continue;

            var afterHr = siblings.Skip(hrIndex + 1).ToList();
            if (afterHr.Count == 0 || afterHr.Count > 3) continue;

            // Check if all elements after the <hr> are short and contain italic text
            bool allShortBio = afterHr.All(el =>
            {
                var text = el.TextContent?.Trim() ?? "";
                if (text.Length > 500 || text.Length == 0) return false;
                // Must be mostly italic/emphasis or be a short bio-looking paragraph
                var emText = string.Join("", el.QuerySelectorAll("em, i").Select(e => e.TextContent ?? "")).Trim();
                return emText.Length > text.Length * 0.5;
            });

            if (allShortBio)
            {
                hr.Remove();
                foreach (var el in afterHr)
                    el.Remove();
            }
        }
    }

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
