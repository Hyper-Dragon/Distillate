using Distillate.Core.Models;
using Xunit;

namespace Distillate.Tests.Models;

public class DebugInfoTests
{
    [Fact]
    public void DebugInfo_DefaultValues_AreCorrect()
    {
        var info = new DebugInfo();

        Assert.Empty(info.Stages);
        Assert.Empty(info.ImageFetches);
        Assert.Equal(0, info.SourceHtmlBytes);
        Assert.Equal(0, info.CleanHtmlBytes);
    }

    [Fact]
    public void DebugInfo_WithPopulatedFields_RetainsValues()
    {
        var stages = new[]
        {
            new StageTiming { Stage = "BrowserFetch", Ms = 1234.5 },
            new StageTiming { Stage = "Extraction",   Ms =   88.0 },
            new StageTiming { Stage = "ImageProcessing", Ms = 5000.0 }
        };
        var fetches = new[]
        {
            new ImageFetchLog { Url = "https://example.com/img.jpg", Pool = "inPage",  Outcome = "ok",      HttpStatus = 200, Bytes = 48_000, Ms = 210.0 },
            new ImageFetchLog { Url = "https://pixel.wp.com/g.gif",  Pool = "outOfPage", Outcome = "timeout", Ms = 10_000.0 }
        };

        var info = new DebugInfo
        {
            Stages = stages,
            ImageFetches = fetches,
            SourceHtmlBytes = 120_000,
            CleanHtmlBytes = 18_000
        };

        Assert.Equal(3, info.Stages.Length);
        Assert.Equal("BrowserFetch", info.Stages[0].Stage);
        Assert.Equal(1234.5, info.Stages[0].Ms);
        Assert.Equal("ImageProcessing", info.Stages[2].Stage);

        Assert.Equal(2, info.ImageFetches.Length);
        Assert.Equal("inPage", info.ImageFetches[0].Pool);
        Assert.Equal("ok", info.ImageFetches[0].Outcome);
        Assert.Equal(200, info.ImageFetches[0].HttpStatus);
        Assert.Equal(48_000L, info.ImageFetches[0].Bytes);
        Assert.Equal("timeout", info.ImageFetches[1].Outcome);
        Assert.Null(info.ImageFetches[1].HttpStatus);
        Assert.Null(info.ImageFetches[1].Bytes);

        Assert.Equal(120_000, info.SourceHtmlBytes);
        Assert.Equal(18_000, info.CleanHtmlBytes);
    }

    [Fact]
    public void StageTiming_Properties_AreCorrect()
    {
        var stage = new StageTiming { Stage = "HtmlCleaning", Ms = 42.7 };

        Assert.Equal("HtmlCleaning", stage.Stage);
        Assert.Equal(42.7, stage.Ms);
    }

    [Fact]
    public void ImageFetchLog_AllOutcomes_CanBeSet()
    {
        var outcomes = new[] { "ok", "timeout", "httpError", "exception", "skipped" };
        var pools = new[] { "inPage", "outOfPage", "embed", "featured" };

        foreach (var outcome in outcomes)
        {
            var log = new ImageFetchLog { Url = "https://example.com/x.jpg", Pool = "inPage", Outcome = outcome };
            Assert.Equal(outcome, log.Outcome);
        }

        foreach (var pool in pools)
        {
            var log = new ImageFetchLog { Url = "https://example.com/x.jpg", Pool = pool, Outcome = "ok" };
            Assert.Equal(pool, log.Pool);
        }
    }

    [Fact]
    public void ProcessRequest_IncludeDebugInfo_DefaultsFalse()
    {
        var request = new ProcessRequest { Url = "https://example.com" };
        Assert.False(request.IncludeDebugInfo);
    }

    [Fact]
    public void ProcessRequest_IncludeDebugInfo_CanBeEnabled()
    {
        var request = new ProcessRequest { Url = "https://example.com", IncludeDebugInfo = true };
        Assert.True(request.IncludeDebugInfo);
    }

    [Fact]
    public void ProcessResponse_DebugInfo_IsNullByDefault()
    {
        var response = new ProcessResponse
        {
            Success = true,
            RequestedUrl = "https://example.com",
            ProcessedAt = DateTimeOffset.UtcNow,
            ProcessingTimeMs = 100,
            CompletionFlags = 0,
            TraceId = "t1"
        };

        Assert.Null(response.DebugInfo);
    }

    [Fact]
    public void ProcessResponse_DebugInfo_CanBePopulated()
    {
        var debugInfo = new DebugInfo
        {
            Stages = [new StageTiming { Stage = "BrowserFetch", Ms = 500 }],
            ImageFetches = [new ImageFetchLog { Url = "https://example.com/img.png", Pool = "inPage", Outcome = "ok", HttpStatus = 200, Bytes = 1024, Ms = 80 }],
            SourceHtmlBytes = 50_000,
            CleanHtmlBytes = 8_000
        };

        var response = new ProcessResponse
        {
            Success = true,
            RequestedUrl = "https://example.com",
            ProcessedAt = DateTimeOffset.UtcNow,
            ProcessingTimeMs = 600,
            CompletionFlags = 0,
            TraceId = "t2",
            DebugInfo = debugInfo
        };

        Assert.NotNull(response.DebugInfo);
        Assert.Single(response.DebugInfo.Stages);
        Assert.Single(response.DebugInfo.ImageFetches);
        Assert.Equal(50_000, response.DebugInfo.SourceHtmlBytes);
    }
}
