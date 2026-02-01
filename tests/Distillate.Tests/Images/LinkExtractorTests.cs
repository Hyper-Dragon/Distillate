using Distillate.Core.Images;
using Xunit;

namespace Distillate.Tests.Images;

public class LinkExtractorTests
{
    private static readonly Uri BaseUri = new("https://example.com/article");

    [Fact]
    public void Extract_ContentLinks_AreAbsolute()
    {
        var html = """
            <html><body>
              <a href="/page1">Page 1</a>
              <a href="https://other.com/page">Other</a>
              <a href="relative/page">Relative</a>
            </body></html>
            """;

        var result = LinkExtractor.Extract(html, BaseUri);

        Assert.Contains("https://example.com/page1", result.LinksInPage);
        Assert.Contains("https://other.com/page", result.LinksInPage);
        // URI resolution: 'relative/page' against 'https://example.com/article' treats 'article' as a file
        Assert.Contains("https://example.com/relative/page", result.LinksInPage);
    }

    [Fact]
    public void Extract_ContentLinks_AreDeduplicated()
    {
        var html = """
            <html><body>
              <a href="/page1">First</a>
              <a href="/page1">Duplicate</a>
              <a href="https://example.com/page1">Absolute duplicate</a>
            </body></html>
            """;

        var result = LinkExtractor.Extract(html, BaseUri);

        Assert.Single(result.LinksInPage);
    }

    [Fact]
    public void Extract_FragmentLinks_AreExcluded()
    {
        var html = "<html><body><a href=\"#section1\">Skip</a></body></html>";

        var result = LinkExtractor.Extract(html, BaseUri);

        Assert.Empty(result.LinksInPage);
    }

    [Fact]
    public void Extract_MediaLinks_IncludesImagesAndVideo()
    {
        var html = """
            <html><body>
              <img src="/photo.jpg" />
              <video src="/clip.mp4"></video>
              <source src="/audio.ogg" />
            </body></html>
            """;

        var result = LinkExtractor.Extract(html, BaseUri);

        Assert.Contains("https://example.com/photo.jpg", result.LinksMedia);
        Assert.Contains("https://example.com/clip.mp4", result.LinksMedia);
        Assert.Contains("https://example.com/audio.ogg", result.LinksMedia);
    }

    [Fact]
    public void Extract_AssetLinks_IncludesCssAndScript()
    {
        var html = """
            <html><head>
              <link href="/style.css" rel="stylesheet" />
              <script src="/app.js"></script>
            </head><body></body></html>
            """;

        var result = LinkExtractor.Extract(html, BaseUri);

        Assert.Contains("https://example.com/style.css", result.LinksAssets);
        Assert.Contains("https://example.com/app.js", result.LinksAssets);
    }

    [Fact]
    public void Extract_DataUriImages_AreExcluded()
    {
        var html = "<html><body><img src=\"data:image/png;base64,abc123\" /></body></html>";

        var result = LinkExtractor.Extract(html, BaseUri);

        Assert.Empty(result.LinksMedia);
    }

    [Fact]
    public void Extract_NonHttpSchemes_AreExcluded()
    {
        var html = """
            <html><body>
              <a href="mailto:user@example.com">Mail</a>
              <a href="ftp://files.example.com/file">FTP</a>
              <a href="https://valid.com/page">Valid</a>
            </body></html>
            """;

        var result = LinkExtractor.Extract(html, BaseUri);

        Assert.Single(result.LinksInPage);
        Assert.Equal("https://valid.com/page", result.LinksInPage[0]);
    }

    [Fact]
    public void Extract_EmptyHtml_ReturnsEmptyArrays()
    {
        var result = LinkExtractor.Extract("<html><body></body></html>", BaseUri);

        Assert.Empty(result.LinksInPage);
        Assert.Empty(result.LinksMedia);
        Assert.Empty(result.LinksAssets);
    }
}
