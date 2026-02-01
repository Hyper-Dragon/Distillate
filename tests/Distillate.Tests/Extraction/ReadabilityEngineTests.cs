using Distillate.Core.Extraction;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Distillate.Tests.Extraction;

public class ReadabilityEngineTests
{
    private readonly ReadabilityEngine _engine;

    public ReadabilityEngineTests()
    {
        var logger = Mock.Of<ILogger<ReadabilityEngine>>();
        _engine = new ReadabilityEngine(logger);
    }

    [Fact]
    public void Engine_HasCorrectName()
    {
        Assert.Equal("readability", _engine.Name);
    }

    [Fact]
    public void Extract_SimpleArticle_ReturnsContent()
    {
        var html = @"
            <!DOCTYPE html>
            <html>
            <head><title>Test Title</title></head>
            <body>
                <article>
                    <h1>Article Heading</h1>
                    <p>This is a paragraph with enough content to be considered valid article text.
                    We need at least 200 characters to pass the content threshold check in our implementation.
                    So let's add some more meaningful text here to ensure we have enough content.</p>
                </article>
            </body>
            </html>";

        var result = _engine.Extract(html, new Uri("https://example.com"));

        Assert.True(result.Success);
        Assert.NotNull(result.CleanHtml);
        Assert.NotNull(result.RawText);
        Assert.Contains("Article Heading", result.RawText);
    }

    [Fact]
    public void Extract_RemovesScriptsAndStyles()
    {
        var html = @"
            <!DOCTYPE html>
            <html>
            <head>
                <title>Test</title>
                <style>.hidden { display: none; }</style>
            </head>
            <body>
                <script>alert('malicious');</script>
                <article>
                    <h1>Clean Content</h1>
                    <p>This is the actual article content that should be extracted. 
                    We need enough text to pass the minimum content length threshold for our extraction algorithm.
                    Adding more text to make sure we have at least 200 characters here.</p>
                </article>
                <script>console.log('another script');</script>
            </body>
            </html>";

        var result = _engine.Extract(html, new Uri("https://example.com"));

        Assert.True(result.Success);
        Assert.DoesNotContain("alert", result.CleanHtml ?? "");
        Assert.DoesNotContain("malicious", result.CleanHtml ?? "");
        Assert.DoesNotContain("console.log", result.CleanHtml ?? "");
        Assert.Contains("Clean Content", result.RawText);
    }

    [Fact]
    public void Extract_ResolvesRelativeUrls()
    {
        var html = @"
            <!DOCTYPE html>
            <html>
            <body>
                <article>
                    <h1>Article with Links</h1>
                    <p>Here is a paragraph with a <a href=""/relative/link"">relative link</a> that should be made absolute.
                    And here is an <img src=""/images/photo.jpg"" alt=""test""> image as well.
                    Adding more content to ensure we have enough characters for extraction threshold.</p>
                </article>
            </body>
            </html>";

        var result = _engine.Extract(html, new Uri("https://example.com/page"));

        Assert.True(result.Success);
        Assert.Contains("https://example.com/relative/link", result.CleanHtml);
        Assert.Contains("https://example.com/images/photo.jpg", result.CleanHtml);
    }

    [Fact]
    public void Extract_ExtractsOgTitle()
    {
        var html = @"
            <!DOCTYPE html>
            <html>
            <head>
                <title>Fallback Title</title>
                <meta property=""og:title"" content=""Open Graph Title"">
            </head>
            <body>
                <article>
                    <p>Article content goes here. We need enough text to pass the minimum content length threshold.
                    Let's add some more text to make sure we have at least 200 characters in this article.</p>
                </article>
            </body>
            </html>";

        var result = _engine.Extract(html, new Uri("https://example.com"));

        Assert.True(result.Success);
        Assert.Equal("Open Graph Title", result.Title);
    }

    [Fact]
    public void Extract_FallsBackToH1Title()
    {
        var html = @"
            <!DOCTYPE html>
            <html>
            <head><title>Page Title</title></head>
            <body>
                <article>
                    <h1>H1 Article Title</h1>
                    <p>Article content goes here. We need enough text to pass the minimum content length threshold.
                    Let's add some more text to make sure we have at least 200 characters in this article.</p>
                </article>
            </body>
            </html>";

        var result = _engine.Extract(html, new Uri("https://example.com"));

        Assert.True(result.Success);
        Assert.Equal("H1 Article Title", result.Title);
    }
}
