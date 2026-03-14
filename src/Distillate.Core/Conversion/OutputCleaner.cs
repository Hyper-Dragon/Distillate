using System.Text.RegularExpressions;
using Distillate.Core.Models;

namespace Distillate.Core.Conversion;

/// <summary>
/// Generic post-processing cleanup for all text and markdown outputs.
/// Applied after extraction and markdown conversion to fix common quality issues
/// that appear across sites.
/// </summary>
public static partial class OutputCleaner
{
    // --- Markdown cleanup ---

    /// <summary>
    /// Cleans markdown output: strips empty bold/italic, fixes whitespace, removes
    /// common site chrome patterns, and normalizes non-breaking spaces.
    /// </summary>
    public static string CleanMarkdown(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return markdown;

        var result = markdown;

        // Normalize non-breaking spaces to regular spaces
        result = result.Replace('\u00A0', ' ');

        // Strip empty bold markers: **** (two empty ** pairs collapsed together)
        result = EmptyBoldRegex().Replace(result, "");

        // Fix missing space before inline links: "word[link" → "word [link"
        result = MissingSpaceBeforeLinkRegex().Replace(result, "$1 [");

        // Fix missing space after image/link when followed by text: "](url)Text" → "](url)\nText"
        // But only for image captions like "Image source, X"
        result = ImageCaptionAfterImageRegex().Replace(result, "$1\n$2");

        // Clean up newsletter/promo junk (common across Guardian, NYT, WaPo)
        // First pass: remove full blocks with both open and close markers
        result = NewsletterPromoRegex().Replace(result, "");
        // Second pass: remove standalone "skip past newsletter promotion" lines
        // when no matching close marker is present on the page
        result = StandaloneNewsletterSkipLineRegex().Replace(result, "");

        // Clean up social share link blocks at end: [Facebook](...)[Twitter](...)[Email](...)
        result = SocialShareBlockRegex().Replace(result, "");

        // Remove orphaned image captions when no image context (e.g., "Image source, Reuters\n")
        // Only strip when they appear on their own line
        result = OrphanedImageCaptionRegex().Replace(result, "");

        // Fix heading whitespace: "## \n     Title" → "## Title"
        result = HeadingWhitespaceRegex().Replace(result, "$1 $2");

        // Decode &amp; in URLs within markdown links/images
        result = AmpInMarkdownUrlRegex().Replace(result, m =>
            m.Value.Replace("&amp;", "&"));

        // Remove leading whitespace from standalone image lines so Pandoc sees them
        // as block-level images rather than code blocks or indented text.
        result = LeadingSpaceBeforeImageLineRegex().Replace(result, "![");

        // Collapse 3+ consecutive blank lines to 2
        result = ExcessiveBlankLinesRegex().Replace(result, "\n\n");

        // Trim trailing whitespace on each line
        result = TrailingWhitespaceRegex().Replace(result, "");

        return result.Trim();
    }

    /// <summary>
    /// Strips markdown image syntax: ![alt](url) and any immediately following caption line.
    /// Used for the NoImages output variant.
    /// </summary>
    public static string StripMarkdownImages(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return markdown;

        // Remove ![alt](url) image tags
        var result = MarkdownImageRegex().Replace(markdown, "");

        // Remove orphaned image captions that follow removed images
        result = OrphanedImageCaptionRegex().Replace(result, "");

        // Collapse resulting excessive blank lines
        result = ExcessiveBlankLinesRegex().Replace(result, "\n\n");

        return result.Trim();
    }

    // --- Plain text cleanup ---

    /// <summary>
    /// Cleans plain text output: normalizes whitespace, removes common junk patterns.
    /// </summary>
    public static string CleanPlainText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var result = text;

        // Normalize non-breaking spaces
        result = result.Replace('\u00A0', ' ');

        // Fix "ByAuthorName" → "By Author Name" (common BBC pattern)
        result = BylineNoSpaceRegex().Replace(result, "By $1");

        // Fix "Published12 March" → "Published 12 March"
        result = PublishedNoSpaceRegex().Replace(result, "Published $1");

        // Clean newsletter promo text
        result = NewsletterPromoRegex().Replace(result, "");
        result = StandaloneNewsletterSkipLineRegex().Replace(result, "");

        // Clean "Image source, X" orphaned captions
        result = OrphanedImageCaptionPlainRegex().Replace(result, "");

        // Collapse excessive whitespace (3+ newlines → 2)
        result = ExcessiveBlankLinesRegex().Replace(result, "\n\n");

        // Trim trailing whitespace per line
        result = TrailingWhitespaceRegex().Replace(result, "");

        return result.Trim();
    }

    // --- HTML cleanup ---

    /// <summary>
    /// Strips images from HTML: removes img, picture, source, and figure elements
    /// that only contain images. Also removes the html/head/body wrapper if present.
    /// </summary>
    public static string StripHtmlImages(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return html;

        var result = html;

        // Remove <picture>...</picture> elements (contain <source> and <img>)
        result = PictureElementRegex().Replace(result, "");

        // Remove remaining <img .../> and <img ...> tags
        result = ImgTagRegex().Replace(result, "");

        // Remove <figure> elements that are now empty or only contain whitespace/figcaption about images
        result = EmptyFigureRegex().Replace(result, "");

        return result;
    }

    /// <summary>
    /// Removes the html/head/body wrapper that AngleSharp adds when re-parsing fragments.
    /// Returns just the body inner content.
    /// </summary>
    public static string UnwrapHtmlDocument(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return html;

        var match = BodyContentRegex().Match(html);
        return match.Success ? match.Groups[1].Value : html;
    }

    /// <summary>
    /// Collapses 3+ consecutive newlines to 2. Used by HtmlArticleCleaner.
    /// </summary>
    public static string CollapseExcessiveNewlines(string text)
    {
        return ExcessiveBlankLinesRegex().Replace(text, "\n\n");
    }

    /// <summary>
    /// Removes the second heading when it immediately follows the first and has identical text.
    /// Handles the common case where the title is prepended as # H1 but the article HTML also
    /// opens with an H1 or H2 containing the same text.
    /// </summary>
    public static string DeduplicateLeadingTitle(string markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return markdown;

        var lines = markdown.Split('\n');
        var limit = Math.Min(lines.Length, 30);

        int firstIdx = -1;
        string firstText = "";

        for (int i = 0; i < limit; i++)
        {
            var m = AnyHeadingRegex().Match(lines[i]);
            if (!m.Success) continue;

            if (firstIdx < 0)
            {
                firstIdx = i;
                firstText = m.Groups[1].Value.Trim();
                continue;
            }

            // Second heading found — remove it if text matches
            var secondText = m.Groups[1].Value.Trim();
            if (string.Equals(firstText, secondText, StringComparison.OrdinalIgnoreCase))
            {
                var resultLines = new List<string>(lines.Length - 1);
                for (int j = 0; j < lines.Length; j++)
                    if (j != i) resultLines.Add(lines[j]);
                return ExcessiveBlankLinesRegex()
                    .Replace(string.Join('\n', resultLines), "\n\n")
                    .TrimEnd();
            }
            break; // Different heading text — stop
        }

        return markdown;
    }

    /// <summary>
    /// Injects an image markdown tag immediately after the first heading in the document.
    /// Used to add the featured/hero image when the article body contains no images.
    /// </summary>
    public static string InjectFeaturedImageAfterTitle(string markdown, string imageMarkdown)
    {
        if (string.IsNullOrEmpty(markdown)) return imageMarkdown + "\n\n" + markdown;

        var match = AnyHeadingLineRegex().Match(markdown);
        if (!match.Success)
            return markdown + "\n\n" + imageMarkdown;

        var afterHeading = match.Index + match.Length;
        // Skip blank lines immediately after the heading
        int bodyStart = afterHeading;
        while (bodyStart < markdown.Length && markdown[bodyStart] == '\n')
            bodyStart++;

        return markdown[..afterHeading] + "\n" + imageMarkdown + "\n\n" + markdown[bodyStart..];
    }

    // --- Unicode punctuation normalization ---

    /// <summary>
    /// Replaces common Unicode typographic characters (smart quotes, en/em dashes,
    /// ellipsis, etc.) according to the requested mode.
    /// <list type="bullet">
    ///   <item><see cref="UnicodeNormalizationMode.Preserve"/> — returns text unchanged (default).</item>
    ///   <item><see cref="UnicodeNormalizationMode.Ascii"/> — maps to plain ASCII equivalents.</item>
    ///   <item><see cref="UnicodeNormalizationMode.Latex"/> — maps to LaTeX-idiomatic sequences
    ///         (en-dash → <c>--</c>, em-dash → <c>---</c>).</item>
    /// </list>
    /// Non-breaking spaces (U+00A0) are always normalized to regular spaces.
    /// Soft hyphens (U+00AD) are always removed.
    /// </summary>
    public static string NormalizeUnicodePunctuation(
        string text, UnicodeNormalizationMode mode = UnicodeNormalizationMode.Preserve)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        if (mode == UnicodeNormalizationMode.Preserve)
            return text;

        var result = text;

        // --- Always applied regardless of mode ---

        // Non-breaking space → regular space
        result = result.Replace('\u00A0', ' ');

        // Soft hyphen → remove (invisible formatting hint)
        result = result.Replace("\u00AD", "");

        // --- Smart single quotes → ASCII apostrophe ---
        result = result.Replace('\u2018', '\'');  // ' left single quotation mark
        result = result.Replace('\u2019', '\'');  // ' right single quotation mark
        result = result.Replace('\u201A', '\'');  // ‚ single low-9 quotation mark

        // --- Smart double quotes → ASCII double quote ---
        result = result.Replace('\u201C', '"');   // " left double quotation mark
        result = result.Replace('\u201D', '"');   // " right double quotation mark
        result = result.Replace('\u201E', '"');   // „ double low-9 quotation mark

        // --- Dashes (mode-sensitive) ---
        if (mode == UnicodeNormalizationMode.Latex)
        {
            result = result.Replace("\u2014", "---");  // — em dash → LaTeX ---
            result = result.Replace("\u2013", "--");   // – en dash → LaTeX --
        }
        else // Ascii
        {
            result = result.Replace("\u2013", "-");    // – en dash → single hyphen
            result = result.Replace("\u2014", "--");   // — em dash → double hyphen
        }
        result = result.Replace('\u2010', '-');   // ‐ hyphen
        result = result.Replace('\u2011', '-');   // ‑ non-breaking hyphen
        result = result.Replace('\u2012', '-');   // ‒ figure dash

        // --- Ellipsis ---
        result = result.Replace("\u2026", "...");  // … horizontal ellipsis

        // --- Prime marks → ASCII equivalents ---
        result = result.Replace('\u2032', '\'');  // ′ prime
        result = result.Replace('\u2033', '"');   // ″ double prime

        // --- Bullet → ASCII asterisk ---
        result = result.Replace('\u2022', '*');   // • bullet

        return result;
    }

    // --- Compiled regex patterns ---

    [GeneratedRegex(@"\*{4}")]
    private static partial Regex EmptyBoldRegex();



    [GeneratedRegex(@"(\w)\[")]
    private static partial Regex MissingSpaceBeforeLinkRegex();

    [GeneratedRegex(@"(\]\([^)]+\))(Image source)")]
    private static partial Regex ImageCaptionAfterImageRegex();

    // Match the Guardian-style newsletter block in both plain text and markdown link forms:
    //   plain:    skip past newsletter promotion ... after newsletter promotion
    //   markdown: [skip past newsletter promotion](url) ... [after newsletter promotion](url)
    [GeneratedRegex(@"\[?skip past newsletter promotion\]?(?:\([^)]*\))?\s*[\s\S]*?\[?after newsletter promotion\]?(?:\([^)]*\))?\s*\n?", RegexOptions.IgnoreCase)]
    private static partial Regex NewsletterPromoRegex();

    // Removes a lone "skip past newsletter promotion" line when the page has no matching close marker
    [GeneratedRegex(@"^[ \t]*\[?skip past newsletter promotion\]?(?:\([^)]*\))?[ \t]*\r?\n?", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex StandaloneNewsletterSkipLineRegex();

    [GeneratedRegex(@"\n?\[Facebook\]\([^)]*\)\s*\[Twitter\]\([^)]*\)\s*\[Email\]\([^)]*\)\s*$", RegexOptions.Multiline)]
    private static partial Regex SocialShareBlockRegex();

    [GeneratedRegex(@"^Image source,\s*\S[^\n]*\n?", RegexOptions.Multiline)]
    private static partial Regex OrphanedImageCaptionRegex();

    [GeneratedRegex(@"Image source,\s*\S[^\n]*")]
    private static partial Regex OrphanedImageCaptionPlainRegex();

    [GeneratedRegex(@"(#{1,6})\s*\n\s+(\S)")]
    private static partial Regex HeadingWhitespaceRegex();

    [GeneratedRegex(@"(?:\!\[[^\]]*\]\([^)]*&amp;[^)]*\)|\[[^\]]*\]\([^)]*&amp;[^)]*\))")]
    private static partial Regex AmpInMarkdownUrlRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExcessiveBlankLinesRegex();

    [GeneratedRegex(@"[ \t]+$", RegexOptions.Multiline)]
    private static partial Regex TrailingWhitespaceRegex();

    [GeneratedRegex(@"^[ \t]+!\[", RegexOptions.Multiline)]
    private static partial Regex LeadingSpaceBeforeImageLineRegex();

    [GeneratedRegex(@"^#{1,6}\s+(.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex AnyHeadingRegex();

    [GeneratedRegex(@"^#{1,6}\s+.+\n?", RegexOptions.Multiline)]
    private static partial Regex AnyHeadingLineRegex();

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]+\)\s*")]
    private static partial Regex MarkdownImageRegex();

    [GeneratedRegex(@"By([A-Z][a-z]+\s+[A-Z])")]
    private static partial Regex BylineNoSpaceRegex();

    [GeneratedRegex(@"Published(\d)")]
    private static partial Regex PublishedNoSpaceRegex();

    [GeneratedRegex(@"<picture[\s\S]*?</picture>", RegexOptions.IgnoreCase)]
    private static partial Regex PictureElementRegex();

    [GeneratedRegex(@"<img\b[^>]*/?\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex ImgTagRegex();

    [GeneratedRegex(@"<figure[^>]*>\s*((<figcaption[^>]*>.*?</figcaption>)?\s*)</figure>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex EmptyFigureRegex();

    [GeneratedRegex(@"<body[^>]*>([\s\S]*)</body>", RegexOptions.IgnoreCase)]
    private static partial Regex BodyContentRegex();
}
