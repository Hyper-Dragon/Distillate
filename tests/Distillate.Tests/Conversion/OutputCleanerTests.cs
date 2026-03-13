using Distillate.Core.Conversion;
using Xunit;

namespace Distillate.Tests.Conversion;

public class OutputCleanerTests
{
    // ========== CleanMarkdown ==========

    [Fact]
    public void CleanMarkdown_NullInput_ReturnsNull()
    {
        var result = OutputCleaner.CleanMarkdown(null!);
        Assert.Null(result);
    }

    [Fact]
    public void CleanMarkdown_EmptyInput_ReturnsEmpty()
    {
        var result = OutputCleaner.CleanMarkdown("   ");
        Assert.Equal("   ", result);
    }

    [Fact]
    public void CleanMarkdown_NormalizesNonBreakingSpaces()
    {
        var input = "text\u00A0with\u00A0nbsp";
        var result = OutputCleaner.CleanMarkdown(input);
        Assert.Equal("text with nbsp", result);
    }

    [Fact]
    public void CleanMarkdown_StripsEmptyBoldMarkers()
    {
        var input = "But then **** came reports";
        var result = OutputCleaner.CleanMarkdown(input);
        Assert.DoesNotContain("****", result);
        Assert.Contains("But then", result);
        Assert.Contains("came reports", result);
    }

    [Fact]
    public void CleanMarkdown_PreservesRealBold()
    {
        var input = "This is **important** text";
        var result = OutputCleaner.CleanMarkdown(input);
        Assert.Contains("**important**", result);
    }

    [Fact]
    public void CleanMarkdown_FixesMissingSpaceBeforeLink()
    {
        var input = "risen[back above $100](https://example.com)";
        var result = OutputCleaner.CleanMarkdown(input);
        Assert.Contains("risen [back above $100]", result);
    }

    [Fact]
    public void CleanMarkdown_StripsNewsletterPromo()
    {
        var input = "some text\nskip past newsletter promotion\nafter newsletter promotion\nmore text";
        var result = OutputCleaner.CleanMarkdown(input);
        Assert.DoesNotContain("newsletter promotion", result);
        Assert.Contains("some text", result);
        Assert.Contains("more text", result);
    }

    [Fact]
    public void CleanMarkdown_StripsSocialShareBlock()
    {
        var input = "article content\n\n[Facebook](https://example.org/#facebook)[Twitter](https://example.org/#twitter)[Email](https://example.org/#email)";
        var result = OutputCleaner.CleanMarkdown(input);
        Assert.DoesNotContain("Facebook", result);
        Assert.DoesNotContain("Twitter", result);
        Assert.Contains("article content", result);
    }

    [Fact]
    public void CleanMarkdown_StripsOrphanedImageCaptions()
    {
        var input = "Image source, Reuters\n\nThe price of oil";
        var result = OutputCleaner.CleanMarkdown(input);
        Assert.DoesNotContain("Image source", result);
        Assert.Contains("The price of oil", result);
    }

    [Fact]
    public void CleanMarkdown_FixesHeadingWhitespace()
    {
        var input = "## \n        Know Your Enemy";
        var result = OutputCleaner.CleanMarkdown(input);
        Assert.StartsWith("## Know Your Enemy", result);
    }

    [Fact]
    public void CleanMarkdown_DecodesAmpInMarkdownUrls()
    {
        var input = "![alt](https://example.com/img?w=100&amp;h=200&amp;fit=crop)";
        var result = OutputCleaner.CleanMarkdown(input);
        Assert.Contains("&h=200", result);
        Assert.DoesNotContain("&amp;", result);
    }

    [Fact]
    public void CleanMarkdown_CollapsesExcessiveBlankLines()
    {
        var input = "paragraph one\n\n\n\n\nparagraph two";
        var result = OutputCleaner.CleanMarkdown(input);
        Assert.DoesNotContain("\n\n\n", result);
        Assert.Contains("paragraph one\n\nparagraph two", result);
    }

    // ========== StripMarkdownImages ==========

    [Fact]
    public void StripMarkdownImages_RemovesImageSyntax()
    {
        var input = "text before\n\n![A photo](https://example.com/photo.jpg)\n\ntext after";
        var result = OutputCleaner.StripMarkdownImages(input);
        Assert.DoesNotContain("![", result);
        Assert.DoesNotContain("photo.jpg", result);
        Assert.Contains("text before", result);
        Assert.Contains("text after", result);
    }

    [Fact]
    public void StripMarkdownImages_RemovesImagePlusCaptionLine()
    {
        var input = "![alt](https://example.com/img.jpg)\nImage source, Reuters\n\nthe article";
        var result = OutputCleaner.StripMarkdownImages(input);
        Assert.DoesNotContain("Image source", result);
        Assert.Contains("the article", result);
    }

    [Fact]
    public void StripMarkdownImages_PreservesNonImageContent()
    {
        var input = "# Title\n\nParagraph with [a link](https://example.com).\n\n- list item";
        var result = OutputCleaner.StripMarkdownImages(input);
        Assert.Equal(input, result);
    }

    // ========== CleanPlainText ==========

    [Fact]
    public void CleanPlainText_NormalizesNonBreakingSpaces()
    {
        var input = "and\u00A0bombing a school";
        var result = OutputCleaner.CleanPlainText(input);
        Assert.Equal("and bombing a school", result);
    }

    [Fact]
    public void CleanPlainText_FixesBylineSpacing()
    {
        var input = "ByJemma Crew";
        var result = OutputCleaner.CleanPlainText(input);
        Assert.Equal("By Jemma Crew", result);
    }

    [Fact]
    public void CleanPlainText_FixesPublishedSpacing()
    {
        var input = "Published12 March 2026";
        var result = OutputCleaner.CleanPlainText(input);
        Assert.Equal("Published 12 March 2026", result);
    }

    [Fact]
    public void CleanPlainText_StripsNewsletterPromo()
    {
        var input = "some text\nskip past newsletter promotion\nafter newsletter promotion\nmore text";
        var result = OutputCleaner.CleanPlainText(input);
        Assert.DoesNotContain("newsletter promotion", result);
    }

    [Fact]
    public void CleanPlainText_StripsOrphanedImageCaptions()
    {
        var input = "Image source, Reuters\nThe article content";
        var result = OutputCleaner.CleanPlainText(input);
        Assert.DoesNotContain("Image source", result);
        Assert.Contains("The article content", result);
    }

    // ========== StripHtmlImages ==========

    [Fact]
    public void StripHtmlImages_RemovesPictureElements()
    {
        var input = "<div><picture><source srcset=\"img.webp\"><img src=\"img.jpg\"></picture><p>Text</p></div>";
        var result = OutputCleaner.StripHtmlImages(input);
        Assert.DoesNotContain("<picture", result);
        Assert.DoesNotContain("<source", result);
        Assert.DoesNotContain("<img", result);
        Assert.Contains("<p>Text</p>", result);
    }

    [Fact]
    public void StripHtmlImages_RemovesImgTags()
    {
        var input = "<div><img src=\"photo.jpg\" alt=\"photo\" /><p>Content</p></div>";
        var result = OutputCleaner.StripHtmlImages(input);
        Assert.DoesNotContain("<img", result);
        Assert.Contains("<p>Content</p>", result);
    }

    // ========== UnwrapHtmlDocument ==========

    [Fact]
    public void UnwrapHtmlDocument_ExtractsBodyContent()
    {
        var input = "<html><head></head><body><div>content</div></body></html>";
        var result = OutputCleaner.UnwrapHtmlDocument(input);
        Assert.Equal("<div>content</div>", result);
    }

    [Fact]
    public void UnwrapHtmlDocument_NoBodyTag_ReturnsOriginal()
    {
        var input = "<div>content</div>";
        var result = OutputCleaner.UnwrapHtmlDocument(input);
        Assert.Equal("<div>content</div>", result);
    }

    // ========== CleanMarkdown — leading whitespace before image ==========

    [Fact]
    public void CleanMarkdown_TrimsLeadingSpaceFromStandaloneImageLine()
    {
        var input = "# Title\n\n   ![alt](image-0000.jpg)\n\nParagraph.";
        var result = OutputCleaner.CleanMarkdown(input);
        Assert.Contains("\n![alt](image-0000.jpg)", result);
        Assert.DoesNotContain("   ![", result);
    }

    [Fact]
    public void CleanMarkdown_TrimsLeadingTabFromStandaloneImageLine()
    {
        var input = "# Title\n\n\t![](image-0001.jpg)\n\nParagraph.";
        var result = OutputCleaner.CleanMarkdown(input);
        Assert.DoesNotContain("\t![", result);
        Assert.Contains("![](image-0001.jpg)", result);
    }

    [Fact]
    public void CleanMarkdown_StripsNewsletterPromo_MarkdownLinkForm()
    {
        var input = "article text\n[skip past newsletter promotion](https://example.com/skip)\nPromo content.\n[after newsletter promotion](https://example.com/after)\nmore text";
        var result = OutputCleaner.CleanMarkdown(input);
        Assert.DoesNotContain("newsletter promotion", result);
        Assert.Contains("article text", result);
        Assert.Contains("more text", result);
    }

    [Fact]
    public void CleanMarkdown_StripsStandaloneNewsletterSkipLine()
    {
        // Guardian-style: just a lone "skip past newsletter promotion" with no closing marker
        var input = "paragraph one.\nskip past newsletter promotion\nparagraph two.";
        var result = OutputCleaner.CleanMarkdown(input);
        Assert.DoesNotContain("skip past newsletter promotion", result);
        Assert.Contains("paragraph one", result);
        Assert.Contains("paragraph two", result);
    }

    // ========== DeduplicateLeadingTitle ==========

    [Fact]
    public void DeduplicateLeadingTitle_RemovesDuplicateH1ThenH2()
    {
        var input = "# A Tale of Two Plumbers\n\n## A Tale of Two Plumbers\n\nArticle body.";
        var result = OutputCleaner.DeduplicateLeadingTitle(input);
        Assert.StartsWith("# A Tale of Two Plumbers", result);
        // Only one heading with this text
        var count = System.Text.RegularExpressions.Regex.Matches(result, "A Tale of Two Plumbers").Count;
        Assert.Equal(1, count);
        Assert.Contains("Article body", result);
    }

    [Fact]
    public void DeduplicateLeadingTitle_RemovesDuplicateH1ThenH1()
    {
        var input = "# My Title\n\n# My Title\n\nContent here.";
        var result = OutputCleaner.DeduplicateLeadingTitle(input);
        var count = System.Text.RegularExpressions.Regex.Matches(result, "My Title").Count;
        Assert.Equal(1, count);
        Assert.Contains("Content here", result);
    }

    [Fact]
    public void DeduplicateLeadingTitle_PreservesDifferentSecondHeading()
    {
        var input = "# First Heading\n\n## Second Heading\n\nContent.";
        var result = OutputCleaner.DeduplicateLeadingTitle(input);
        Assert.Contains("# First Heading", result);
        Assert.Contains("## Second Heading", result);
    }

    [Fact]
    public void DeduplicateLeadingTitle_NoHeadings_ReturnsUnchanged()
    {
        var input = "Just some text without headings.";
        var result = OutputCleaner.DeduplicateLeadingTitle(input);
        Assert.Equal(input, result);
    }

    [Fact]
    public void DeduplicateLeadingTitle_EmptyInput_ReturnsEmpty()
    {
        Assert.Equal("", OutputCleaner.DeduplicateLeadingTitle(""));
    }

    // ========== InjectFeaturedImageAfterTitle ==========

    [Fact]
    public void InjectFeaturedImageAfterTitle_InjectsUnderFirstHeading()
    {
        var input = "# My Article\n\nFirst paragraph.";
        var result = OutputCleaner.InjectFeaturedImageAfterTitle(input, "![My Article](image-0000.jpg)");
        Assert.Contains("# My Article", result);
        Assert.Contains("![My Article](image-0000.jpg)", result);
        Assert.Contains("First paragraph", result);
        // Image must appear before body text
        var imagePos = result.IndexOf("![My Article]", StringComparison.Ordinal);
        var bodyPos = result.IndexOf("First paragraph", StringComparison.Ordinal);
        Assert.True(imagePos < bodyPos);
    }

    [Fact]
    public void InjectFeaturedImageAfterTitle_NoHeading_AppendsAtEnd()
    {
        var input = "Article body with no heading.";
        var result = OutputCleaner.InjectFeaturedImageAfterTitle(input, "![img](image-0000.jpg)");
        Assert.Contains("![img](image-0000.jpg)", result);
    }
}
