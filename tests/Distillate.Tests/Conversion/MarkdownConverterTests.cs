using Distillate.Core.Conversion;
using Xunit;

namespace Distillate.Tests.Conversion;

public class MarkdownConverterTests
{
    private readonly MarkdownConverter _converter;

    public MarkdownConverterTests()
    {
        _converter = new MarkdownConverter();
    }

    [Fact]
    public void ConvertToMarkdown_Null_ReturnsNull()
    {
        var result = _converter.ConvertToMarkdown(null);
        Assert.Null(result);
    }

    [Fact]
    public void ConvertToMarkdown_EmptyString_ReturnsNull()
    {
        var result = _converter.ConvertToMarkdown("");
        Assert.Null(result);
    }

    [Fact]
    public void ConvertToMarkdown_WhitespaceOnly_ReturnsNull()
    {
        var result = _converter.ConvertToMarkdown("   \n\t  ");
        Assert.Null(result);
    }

    [Fact]
    public void ConvertToMarkdown_SimpleHeading_ConvertsCorrectly()
    {
        var html = "<h1>Hello World</h1>";
        var result = _converter.ConvertToMarkdown(html);

        Assert.NotNull(result);
        Assert.Contains("# Hello World", result);
    }

    [Fact]
    public void ConvertToMarkdown_Paragraph_ConvertsCorrectly()
    {
        var html = "<p>This is a paragraph.</p>";
        var result = _converter.ConvertToMarkdown(html);

        Assert.NotNull(result);
        Assert.Contains("This is a paragraph.", result);
    }

    [Fact]
    public void ConvertToMarkdown_Link_ConvertsCorrectly()
    {
        var html = @"<a href=""https://example.com"">Example Link</a>";
        var result = _converter.ConvertToMarkdown(html);

        Assert.NotNull(result);
        Assert.Contains("[Example Link](https://example.com)", result);
    }

    [Fact]
    public void ConvertToMarkdown_UnorderedList_ConvertsCorrectly()
    {
        var html = @"<ul><li>Item 1</li><li>Item 2</li><li>Item 3</li></ul>";
        var result = _converter.ConvertToMarkdown(html);

        Assert.NotNull(result);
        Assert.Contains("- Item 1", result);
        Assert.Contains("- Item 2", result);
        Assert.Contains("- Item 3", result);
    }

    [Fact]
    public void ConvertToMarkdown_Bold_ConvertsCorrectly()
    {
        var html = "<p>This is <strong>bold</strong> text.</p>";
        var result = _converter.ConvertToMarkdown(html);

        Assert.NotNull(result);
        Assert.Contains("**bold**", result);
    }

    [Fact]
    public void ConvertToMarkdown_Italic_ConvertsCorrectly()
    {
        var html = "<p>This is <em>italic</em> text.</p>";
        var result = _converter.ConvertToMarkdown(html);

        Assert.NotNull(result);
        Assert.Contains("*italic*", result);
    }

    [Fact]
    public void ConvertToMarkdown_Image_ConvertsCorrectly()
    {
        var html = @"<img src=""https://example.com/image.png"" alt=""Alt text"">";
        var result = _converter.ConvertToMarkdown(html);

        Assert.NotNull(result);
        Assert.Contains("![Alt text](https://example.com/image.png)", result);
    }

    [Fact]
    public void ConvertToMarkdown_ComplexHtml_ConvertsCorrectly()
    {
        var html = @"
            <article>
                <h1>Article Title</h1>
                <p>First paragraph with <strong>bold</strong> and <em>italic</em> text.</p>
                <h2>Section Heading</h2>
                <p>Second paragraph with a <a href=""https://example.com"">link</a>.</p>
                <ul>
                    <li>First item</li>
                    <li>Second item</li>
                </ul>
            </article>";

        var result = _converter.ConvertToMarkdown(html);

        Assert.NotNull(result);
        Assert.Contains("# Article Title", result);
        Assert.Contains("## Section Heading", result);
        Assert.Contains("**bold**", result);
        Assert.Contains("*italic*", result);
        Assert.Contains("[link](https://example.com)", result);
    }

    // ========== Image syntax normalisation (issue #22) ==========

    // The production shape: a reference-style image with an empty identifier followed by an
    // inline target. No renderer parses it as an image, so it reaches the page as literal text.
    [Fact]
    public void NormaliseImageMarkdown_EmptyReferenceIdentifier_RepairedToInline()
    {
        var result = MarkdownConverter.NormaliseImageMarkdown("![][](image-0000.png)");

        Assert.Equal("![](image-0000.png)", result);
        Assert.DoesNotContain("![][](", result);
    }

    [Fact]
    public void NormaliseImageMarkdown_EmptyReferenceIdentifier_KeepsAltText()
    {
        var result = MarkdownConverter.NormaliseImageMarkdown("![A photo of a cat][](image-0001.jpg)");

        Assert.Equal("![A photo of a cat](image-0001.jpg)", result);
    }

    // The real-world sample from the issue: the malformed image is also glued to the prose
    // that follows it, so it has to become its own block as well.
    [Fact]
    public void NormaliseImageMarkdown_RealWorldSample_RepairedAndSeparated()
    {
        const string input = "—Murtaza Hussain\n\n![][](image-0000.png)On Tuesday, the Israeli Air Force posted video";

        var result = MarkdownConverter.NormaliseImageMarkdown(input);

        Assert.DoesNotContain("![][](", result);
        Assert.Contains("![](image-0000.png)\n\nOn Tuesday", result);
    }

    [Fact]
    public void NormaliseImageMarkdown_ImageGluedToFollowingProse_OwnParagraph()
    {
        var result = MarkdownConverter.NormaliseImageMarkdown("![](image-0000.png)On Tuesday the strike happened");

        Assert.Equal("![](image-0000.png)\n\nOn Tuesday the strike happened", result);
    }

    [Fact]
    public void NormaliseImageMarkdown_ImageGluedToPrecedingProse_OwnParagraph()
    {
        var result = MarkdownConverter.NormaliseImageMarkdown("The wreckage![](image-0000.png)");

        Assert.Equal("The wreckage\n\n![](image-0000.png)", result);
    }

    [Fact]
    public void NormaliseImageMarkdown_ImageGluedBothSides_OwnParagraph()
    {
        var result = MarkdownConverter.NormaliseImageMarkdown("before![](image-0000.png)after");

        Assert.Equal("before\n\n![](image-0000.png)\n\nafter", result);
    }

    [Fact]
    public void NormaliseImageMarkdown_ImageOnItsOwnLine_Unchanged()
    {
        const string input = "Some text\n\n![](image-0000.png)\n\nMore text";

        Assert.Equal(input, MarkdownConverter.NormaliseImageMarkdown(input));
    }

    [Fact]
    public void NormaliseImageMarkdown_ValidImageWithTitle_Unchanged()
    {
        const string input = "![](image-0000.png \"a title\")";

        Assert.Equal(input, MarkdownConverter.NormaliseImageMarkdown(input));
    }

    // A linked image is one inline construct: [![alt](img)](url). Splitting inside its label
    // would make renderers stop parsing the link.
    [Fact]
    public void NormaliseImageMarkdown_ImageInsideLinkLabel_Unchanged()
    {
        const string input = "[see ![alt](a.png) here](https://example.com)";

        Assert.Equal(input, MarkdownConverter.NormaliseImageMarkdown(input));
    }

    [Fact]
    public void NormaliseImageMarkdown_LinkedImage_Unchanged()
    {
        const string input = "[![](image-0000.png)](https://example.com/full.png)";

        Assert.Equal(input, MarkdownConverter.NormaliseImageMarkdown(input));
    }

    [Fact]
    public void NormaliseImageMarkdown_AdjacentImages_NotBroken()
    {
        var result = MarkdownConverter.NormaliseImageMarkdown("![](a.png)![](b.png)");

        Assert.Contains("![](a.png)", result);
        Assert.Contains("![](b.png)", result);
    }

    [Fact]
    public void NormaliseImageMarkdown_ImageInFencedCodeBlock_Unchanged()
    {
        const string input = "```\n![](image-0000.png)text\n```";

        Assert.Equal(input, MarkdownConverter.NormaliseImageMarkdown(input));
    }

    [Fact]
    public void NormaliseImageMarkdown_ImageInIndentedCodeBlock_Unchanged()
    {
        const string input = "    ![](image-0000.png)text";

        Assert.Equal(input, MarkdownConverter.NormaliseImageMarkdown(input));
    }

    [Fact]
    public void NormaliseImageMarkdown_ImageInListItem_Unchanged()
    {
        // Splitting here would break the list item open into a new paragraph.
        var result = MarkdownConverter.NormaliseImageMarkdown("- ![](image-0000.png)a caption");

        Assert.StartsWith("- ![](image-0000.png)", result);
        Assert.DoesNotContain("\n\n", result);
    }

    [Fact]
    public void NormaliseImageMarkdown_ProseBracketsNotADestination_Unchanged()
    {
        const string input = "![diagram] (see below) explains it";

        Assert.Equal(input, MarkdownConverter.NormaliseImageMarkdown(input));
    }

    [Fact]
    public void NormaliseImageMarkdown_Idempotent()
    {
        const string input = "—Byline\n\n![][](image-0000.png)On Tuesday";

        var once = MarkdownConverter.NormaliseImageMarkdown(input);
        var twice = MarkdownConverter.NormaliseImageMarkdown(once);

        Assert.Equal(once, twice);
    }

    // The guarantee has to hold for what the converter actually emits, not just for hand-written
    // markdown: an image glued to the following text in the source HTML must not be emitted glued.
    [Fact]
    public void ConvertToMarkdown_ImageGluedToTextInHtml_EmitsSeparateParagraph()
    {
        const string html = @"<p><img src=""image-0000.png"" alt="""">On Tuesday, the Israeli Air Force posted video</p>";

        var result = _converter.ConvertToMarkdown(html);

        Assert.NotNull(result);
        Assert.Contains("![](image-0000.png)\n\nOn Tuesday", result);
    }

    [Fact]
    public void ConvertToMarkdown_EmptyAltImage_StillInlineImage()
    {
        const string html = @"<p><img src=""image-0000.png"" alt=""""></p>";

        var result = _converter.ConvertToMarkdown(html);

        Assert.Equal("![](image-0000.png)", result);
    }
}
