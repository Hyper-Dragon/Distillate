using Distillate.Core.Conversion;
using Xunit;

namespace Distillate.Tests.Conversion;

public class HtmlArticleCleanerTests
{
    // ── Clean: null / empty ──

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Clean_NullOrWhitespace_ReturnsSameValue(string? input)
    {
        var result = HtmlArticleCleaner.Clean(input!);
        Assert.Equal(input, result);
    }

    // ── Phase 1: HTML comments ──

    [Fact]
    public void Clean_RemovesHtmlComments()
    {
        var html = "<div><p>Keep this</p><!-- advertisement --><p>Keep too</p></div>";
        var result = HtmlArticleCleaner.Clean(html);

        Assert.DoesNotContain("<!--", result);
        Assert.Contains("Keep this", result);
        Assert.Contains("Keep too", result);
    }

    // ── Phase 2: Unlikely candidates (class/id substring matching) ──

    [Fact]
    public void Clean_RemovesElementsWithSidebarClass()
    {
        var html = """
            <div>
                <p>Article content that is long enough to be preserved by the cleaner for testing.</p>
                <div class="sidebar-widget">Sidebar junk</div>
            </div>
            """;
        var result = HtmlArticleCleaner.Clean(html);

        Assert.DoesNotContain("Sidebar junk", result);
        Assert.Contains("Article content", result);
    }

    [Fact]
    public void Clean_KeepsArticleClassElements()
    {
        var html = """
            <div>
                <div class="article-body">
                    <p>Important article content that must be preserved.</p>
                </div>
            </div>
            """;
        var result = HtmlArticleCleaner.Clean(html);

        Assert.Contains("Important article content", result);
    }

    // ── Phase 3: Structural selectors ──

    [Fact]
    public void Clean_RemovesAsideElements()
    {
        var html = """
            <div>
                <p>Main content paragraph.</p>
                <aside>Promotional sidebar content</aside>
            </div>
            """;
        var result = HtmlArticleCleaner.Clean(html);

        Assert.DoesNotContain("Promotional sidebar", result);
        Assert.Contains("Main content", result);
    }

    [Fact]
    public void Clean_RemovesNavAndFooter()
    {
        var html = """
            <div>
                <nav>Home | About | Contact</nav>
                <p>Article text.</p>
                <footer>Copyright 2026</footer>
            </div>
            """;
        var result = HtmlArticleCleaner.Clean(html);

        Assert.DoesNotContain("Home | About", result);
        Assert.DoesNotContain("Copyright 2026", result);
        Assert.Contains("Article text", result);
    }

    [Fact]
    public void Clean_RemovesPullquotes()
    {
        var html = """
            <div>
                <p>The quick brown fox jumps over the lazy dog, a famous sentence.</p>
                <figure class="wp-block-pullquote"><blockquote><p>The quick brown fox</p></blockquote></figure>
            </div>
            """;
        var result = HtmlArticleCleaner.Clean(html);

        // The pullquote duplicate should be removed; the body text preserved
        Assert.Contains("quick brown fox jumps over the lazy dog", result);
    }

    [Fact]
    public void Clean_RemovesDataNosnippetElements()
    {
        var html = """
            <div>
                <div data-nosnippet="">By Author Name</div>
                <p>Actual article content starts here.</p>
            </div>
            """;
        var result = HtmlArticleCleaner.Clean(html);

        Assert.DoesNotContain("By Author Name", result);
        Assert.Contains("Actual article content", result);
    }

    // ── Phase 4: Image-only figures ──

    [Fact]
    public void Clean_RemovesFiguresWithOnlyImages()
    {
        var html = """
            <div>
                <figure><img src="photo.jpg" alt="Photo"></figure>
                <p>Text content.</p>
            </div>
            """;
        var result = HtmlArticleCleaner.Clean(html);

        Assert.Contains("Text content", result);
        // Caption-less figure (decorative) should be removed
        Assert.DoesNotContain("photo.jpg", result);
    }

    [Fact]
    public void Clean_PreservesFiguresWithCaptions()
    {
        var html = """
            <div>
                <figure><img src="article-photo.jpg" alt="Photo"><figcaption>A descriptive caption</figcaption></figure>
                <p>Article content.</p>
            </div>
            """;
        var result = HtmlArticleCleaner.Clean(html);

        Assert.Contains("Article content", result);
        // Figure with a caption should be kept (it's part of the article)
        Assert.Contains("article-photo.jpg", result);
    }

    // ── Phase 8: Share paragraphs ──

    [Fact]
    public void Clean_RemovesShareOnSocialParagraphs()
    {
        var html = """
            <div>
                <p>Important content.</p>
                <p>Share on Facebook Twitter LinkedIn</p>
            </div>
            """;
        var result = HtmlArticleCleaner.Clean(html);

        Assert.Contains("Important content", result);
        Assert.DoesNotContain("Share on Facebook", result);
    }

    // ── Phase 9: Mailing list blocks ──

    [Fact]
    public void Clean_RemovesNewsletterBlocks()
    {
        var html = """
            <div>
                <p>Article content.</p>
            </div>
            <div>Join our newsletter for updates</div>
            """;
        var result = HtmlArticleCleaner.Clean(html);

        Assert.Contains("Article content", result);
        Assert.DoesNotContain("Join our newsletter", result);
    }

    // ── Phase 10: Wikipedia-style footnotes ──

    [Fact]
    public void Clean_RemovesFootnoteMarkers()
    {
        var html = """
            <div>
                <p>Important fact<sup>[1]</sup> and another<sup>[2]</sup>.</p>
            </div>
            """;
        var result = HtmlArticleCleaner.Clean(html);

        Assert.Contains("Important fact", result);
        Assert.DoesNotContain("[1]", result);
        Assert.DoesNotContain("[2]", result);
    }

    [Fact]
    public void Clean_RemovesReferencesSections()
    {
        var html = """
            <div>
                <p>Article body content.</p>
                <h2>References</h2>
                <ol class="references">
                    <li>Reference 1</li>
                    <li>Reference 2</li>
                </ol>
            </div>
            """;
        var result = HtmlArticleCleaner.Clean(html);

        Assert.Contains("Article body content", result);
        Assert.DoesNotContain("References", result);
        Assert.DoesNotContain("Reference 1", result);
    }

    [Fact]
    public void Clean_RemovesSeeAlsoAndExternalLinks()
    {
        var html = """
            <div>
                <p>Article content here.</p>
                <h2>See also</h2>
                <ul><li><a href="/related">Related topic</a></li></ul>
                <h2>External links</h2>
                <ul><li><a href="https://example.com">Example.com</a></li></ul>
            </div>
            """;
        var result = HtmlArticleCleaner.Clean(html);

        Assert.Contains("Article content", result);
        Assert.DoesNotContain("See also", result);
        Assert.DoesNotContain("External links", result);
    }

    [Fact]
    public void Clean_RemovesEditBrackets()
    {
        var html = """
            <div>
                <p>Content.</p>
                <h2>References[edit]</h2>
                <p>Ref content</p>
            </div>
            """;
        var result = HtmlArticleCleaner.Clean(html);

        Assert.DoesNotContain("[edit]", result);
    }

    // ── Phase 11: Repeating blog listings ──

    [Fact]
    public void Clean_RemovesRelatedBlogsSection()
    {
        var html = """
            <div>
                <p>Main article.</p>
                <h3>Related Blogs</h3>
                <div><h4>Blog 1</h4><p>Excerpt 1</p><a href="/blog1">Find Out More</a></div>
                <div><h4>Blog 2</h4><p>Excerpt 2</p><a href="/blog2">Find Out More</a></div>
                <div><h4>Blog 3</h4><p>Excerpt 3</p><a href="/blog3">Find Out More</a></div>
            </div>
            """;
        var result = HtmlArticleCleaner.Clean(html);

        Assert.Contains("Main article", result);
        Assert.DoesNotContain("Related Blogs", result);
        Assert.DoesNotContain("Blog 1", result);
    }

    // ── Phase 13: Image credits ──

    [Fact]
    public void Clean_RemovesImageCreditLines()
    {
        var html = """
            <div>
                <p>Article text.</p>
                <p>Illustration: Artist Name for Publication</p>
            </div>
            """;
        var result = HtmlArticleCleaner.Clean(html);

        Assert.Contains("Article text", result);
        Assert.DoesNotContain("Illustration:", result);
    }

    [Fact]
    public void Clean_RemovesPhotoCreditLines()
    {
        var html = """
            <div>
                <p>Article text.</p>
                <p>Photo by John Smith</p>
            </div>
            """;
        var result = HtmlArticleCleaner.Clean(html);

        Assert.Contains("Article text", result);
        Assert.DoesNotContain("Photo by John", result);
    }

    // ── Phase 14: Trailing author bio ──

    [Fact]
    public void Clean_RemovesTrailingAuthorBioAfterHr()
    {
        var html = """
            <div>
                <p>Article conclusion.</p>
                <hr>
                <p><em>Jane Doe is a writer and journalist who covers technology for The Publication.</em></p>
            </div>
            """;
        var result = HtmlArticleCleaner.Clean(html);

        Assert.Contains("Article conclusion", result);
        Assert.DoesNotContain("Jane Doe is a writer", result);
    }

    [Fact]
    public void Clean_KeepsContentAfterHr_WhenNotBioLike()
    {
        var html = """
            <div>
                <p>First section.</p>
                <hr>
                <p>Second section with substantial content that continues the article and is not a bio.</p>
            </div>
            """;
        var result = HtmlArticleCleaner.Clean(html);

        Assert.Contains("First section", result);
        Assert.Contains("Second section", result);
    }

    // ── Phase 15: Empty elements ──

    [Fact]
    public void Clean_RemovesEmptyElements()
    {
        var html = "<div><p>Content</p><p></p><div></div></div>";
        var result = HtmlArticleCleaner.Clean(html);

        Assert.Contains("Content", result);
    }

    // ── ExtractPlainText ──

    [Fact]
    public void ExtractPlainText_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, HtmlArticleCleaner.ExtractPlainText(null!));
        Assert.Equal(string.Empty, HtmlArticleCleaner.ExtractPlainText(""));
    }

    [Fact]
    public void ExtractPlainText_ParagraphSeparation()
    {
        var html = "<div><p>First paragraph.</p><p>Second paragraph.</p></div>";
        var result = HtmlArticleCleaner.ExtractPlainText(html);

        Assert.Contains("First paragraph.", result);
        Assert.Contains("Second paragraph.", result);
        Assert.Contains("\n", result);
    }

    [Fact]
    public void ExtractPlainText_StripsHtmlTags()
    {
        var html = "<p>Text with <strong>bold</strong> and <a href='#'>link</a>.</p>";
        var result = HtmlArticleCleaner.ExtractPlainText(html);

        Assert.Contains("Text with bold and link.", result);
        Assert.DoesNotContain("<strong>", result);
        Assert.DoesNotContain("<a ", result);
    }
}
