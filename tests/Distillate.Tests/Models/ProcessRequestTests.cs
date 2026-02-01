using Distillate.Core.Models;
using Xunit;

namespace Distillate.Tests.Models;

public class ProcessRequestTests
{
    [Fact]
    public void ProcessRequest_DefaultValues_AreCorrect()
    {
        var request = new ProcessRequest
        {
            Url = "https://example.com"
        };

        Assert.Equal("https://example.com", request.Url);
        Assert.Equal("readability", request.Engine);
        Assert.True(request.IncludeImages);
        Assert.False(request.IncludeSourceHtml);
        Assert.Null(request.TimeoutMs);
        Assert.Null(request.StabilizationMs);
        Assert.Null(request.UserAgent);
        Assert.Null(request.Cookies);
    }

    [Fact]
    public void ProcessRequest_CustomValues_ArePreserved()
    {
        var request = new ProcessRequest
        {
            Url = "https://example.com/article",
            Engine = "smartreader",
            IncludeImages = false,
            IncludeSourceHtml = true,
            TimeoutMs = 30000,
            StabilizationMs = 5000,
            UserAgent = "CustomAgent/1.0",
            Cookies = new[]
            {
                new CookieEntry { Name = "session", Value = "abc123" }
            }
        };

        Assert.Equal("smartreader", request.Engine);
        Assert.False(request.IncludeImages);
        Assert.True(request.IncludeSourceHtml);
        Assert.Equal(30000, request.TimeoutMs);
        Assert.Equal(5000, request.StabilizationMs);
        Assert.Equal("CustomAgent/1.0", request.UserAgent);
        Assert.Single(request.Cookies);
        Assert.Equal("session", request.Cookies[0].Name);
    }
}
