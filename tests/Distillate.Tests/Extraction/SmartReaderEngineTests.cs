using Distillate.Core.Extraction;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Distillate.Tests.Extraction;

public class SmartReaderEngineTests
{
    private readonly SmartReaderEngine _engine;

    public SmartReaderEngineTests()
    {
        var logger = Mock.Of<ILogger<SmartReaderEngine>>();
        _engine = new SmartReaderEngine(logger);
    }

    [Fact]
    public void Engine_HasCorrectName()
    {
        Assert.Equal("smartreader", _engine.Name);
    }

    [Fact]
    public void Extract_ValidArticle_ReturnsContent()
    {
        var html = @"
            <!DOCTYPE html>
            <html>
            <head><title>SmartReader Test</title></head>
            <body>
                <article>
                    <h1>Main Article Title</h1>
                    <p>This is the first paragraph of the article. It contains important information
                    that readers would find valuable. SmartReader should be able to extract this content
                    and present it in a clean format without any distracting elements.</p>
                    <p>Here is another paragraph with additional content. The article continues with
                    more meaningful text that adds value to the overall piece. We need sufficient content
                    for SmartReader to consider this page readable.</p>
                </article>
            </body>
            </html>";

        var result = _engine.Extract(html, new Uri("https://example.com/article"));

        Assert.True(result.Success);
        Assert.NotNull(result.Title);
        Assert.NotNull(result.CleanHtml);
        Assert.NotNull(result.RawText);
    }

    [Fact]
    public void Extract_MinimalContent_MayFailGracefully()
    {
        var html = @"
            <!DOCTYPE html>
            <html>
            <body>
                <p>Too short.</p>
            </body>
            </html>";

        var result = _engine.Extract(html, new Uri("https://example.com"));

        // SmartReader may or may not find this readable - either outcome is acceptable
        // The important thing is it doesn't throw an exception
        Assert.NotNull(result);
    }
}
