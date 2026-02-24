using Distillate.Core.Models;
using Xunit;

namespace Distillate.Tests.Models;

public class ProcessRequestTests
{
    [Fact]
    public void ProcessRequest_DefaultValues_AreCorrect()
    {
        var request = new ProcessRequest { Url = "https://example.com" };

        Assert.Equal("https://example.com", request.Url);
        Assert.Equal("readability", request.Engine);
        Assert.Null(request.TimeoutMs);
        Assert.Null(request.StabilizationMs);
        Assert.Null(request.UserAgent);
        Assert.Null(request.Cookies);

        // Content flags
        Assert.False(request.IncludeSourceHtml);
        Assert.False(request.IncludeCleanHtmlEmbeddedImages);
        Assert.False(request.IncludeCleanHtmlLinkedImages);
        Assert.False(request.IncludeCleanHtmlNoImages);
        Assert.False(request.IncludeMarkdownLinkedImages);
        Assert.True(request.IncludeMarkdownNoImages);
        Assert.True(request.IncludePlainText);

        // Link flags
        Assert.False(request.IncludeAllLinksInPage);
        Assert.False(request.IncludeAllLinksMedia);
        Assert.False(request.IncludeAllLinksAssets);

        // Image flags
        Assert.False(request.IncludeAllInPageImages);
        Assert.False(request.IncludeAllOutOfPageImages);
        Assert.False(request.ImageLinkRewrite);
        Assert.False(request.PngConversionRequired);
    }

    [Fact]
    public void ProcessRequest_CustomValues_ArePreserved()
    {
        var request = new ProcessRequest
        {
            Url = "https://example.com/article",
            Engine = "smartreader",
            TimeoutMs = 30000,
            StabilizationMs = 5000,
            UserAgent = "CustomAgent/1.0",
            Cookies = [new CookieEntry { Name = "session", Value = "abc123" }],
            IncludeSourceHtml = true,
            IncludeCleanHtmlEmbeddedImages = true,
            IncludeCleanHtmlLinkedImages = true,
            IncludeCleanHtmlNoImages = true,
            IncludeMarkdownLinkedImages = true,
            IncludeMarkdownNoImages = false,
            IncludePlainText = false,
            IncludeAllLinksInPage = true,
            IncludeAllLinksMedia = true,
            IncludeAllLinksAssets = true,
            IncludeAllInPageImages = true,
            IncludeAllOutOfPageImages = true,
            ImageLinkRewrite = true,
            PngConversionRequired = true
        };

        Assert.Equal("smartreader", request.Engine);
        Assert.Equal(30000, request.TimeoutMs);
        Assert.Equal(5000, request.StabilizationMs);
        Assert.Equal("CustomAgent/1.0", request.UserAgent);
        Assert.Single(request.Cookies);
        Assert.Equal("session", request.Cookies[0].Name);
        Assert.True(request.IncludeSourceHtml);
        Assert.True(request.IncludeCleanHtmlEmbeddedImages);
        Assert.True(request.IncludeCleanHtmlLinkedImages);
        Assert.True(request.IncludeCleanHtmlNoImages);
        Assert.True(request.IncludeMarkdownLinkedImages);
        Assert.False(request.IncludeMarkdownNoImages);
        Assert.False(request.IncludePlainText);
        Assert.True(request.IncludeAllLinksInPage);
        Assert.True(request.IncludeAllLinksMedia);
        Assert.True(request.IncludeAllLinksAssets);
        Assert.True(request.IncludeAllInPageImages);
        Assert.True(request.IncludeAllOutOfPageImages);
        Assert.True(request.ImageLinkRewrite);
        Assert.True(request.PngConversionRequired);
    }
}

