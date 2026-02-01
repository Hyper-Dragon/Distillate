using Distillate.Core.Models;
using Xunit;

namespace Distillate.Tests.Models;

public class ProcessResponseTests
{
    [Fact]
    public void ProcessResponse_SuccessfulResponse_HasCorrectShape()
    {
        var response = new ProcessResponse
        {
            Success = true,
            RequestedUrl = "https://example.com",
            ProcessedAt = DateTimeOffset.UtcNow,
            ProcessingTimeMs = 1500.5,
            Title = "Test Article",
            RawText = "Article content here",
            Markdown = "# Test Article\n\nArticle content here",
            CleanHtml = "<h1>Test Article</h1><p>Article content here</p>",
            CompletionFlags = 0,
            TraceId = "abc123"
        };

        Assert.True(response.Success);
        Assert.Null(response.Errors);
        Assert.Null(response.SourceHtml);
        Assert.Equal(0, response.CompletionFlags);
    }

    [Fact]
    public void ProcessResponse_ErrorResponse_HasCorrectShape()
    {
        var response = new ProcessResponse
        {
            Success = false,
            RequestedUrl = "https://invalid-url",
            ProcessedAt = DateTimeOffset.UtcNow,
            ProcessingTimeMs = 50,
            CompletionFlags = 0,
            Errors = new[] { "Failed to fetch content", "DNS resolution failed" },
            TraceId = "xyz789"
        };

        Assert.False(response.Success);
        Assert.NotNull(response.Errors);
        Assert.Equal(2, response.Errors.Length);
        Assert.Null(response.Title);
        Assert.Null(response.CleanHtml);
    }

    [Fact]
    public void CompletionFlags_AreBitmaskValues()
    {
        Assert.Equal(0, (int)CompletionFlag.Complete);
        Assert.Equal(1, (int)CompletionFlag.ImageTruncated);
        Assert.Equal(2, (int)CompletionFlag.SourceHtmlOmitted);
        Assert.Equal(4, (int)CompletionFlag.EngineFallbackUsed);
    }

    [Fact]
    public void CompletionFlags_CanBeCombined()
    {
        var flags = CompletionFlag.ImageTruncated | CompletionFlag.EngineFallbackUsed;
        
        Assert.Equal(5, (int)flags);
        Assert.True(flags.HasFlag(CompletionFlag.ImageTruncated));
        Assert.True(flags.HasFlag(CompletionFlag.EngineFallbackUsed));
        Assert.False(flags.HasFlag(CompletionFlag.SourceHtmlOmitted));
    }
}
