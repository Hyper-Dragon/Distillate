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
}
