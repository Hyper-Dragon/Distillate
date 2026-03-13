using System.Text.RegularExpressions;

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
        result = NewsletterPromoRegex().Replace(result, "");

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

    // --- Compiled regex patterns ---

    [GeneratedRegex(@"\*{4}")]
    private static partial Regex EmptyBoldRegex();



    [GeneratedRegex(@"(\w)\[")]
    private static partial Regex MissingSpaceBeforeLinkRegex();

    [GeneratedRegex(@"(\]\([^)]+\))(Image source)")]
    private static partial Regex ImageCaptionAfterImageRegex();

    [GeneratedRegex(@"^\[?skip past newsletter promotion\]?[\s\S]*?(?:after newsletter promotion|sign up)\s*\n?", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex NewsletterPromoRegex();

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
