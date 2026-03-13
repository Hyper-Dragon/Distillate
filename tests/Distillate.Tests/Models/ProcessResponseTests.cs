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
            PlainText = "Article content here",
            MarkdownNoImages = "# Test Article\n\nArticle content here",
            CleanHtmlNoImages = "<h1>Test Article</h1><p>Article content here</p>",
            CompletionFlags = 0,
            TraceId = "abc123"
        };

        Assert.True(response.Success);
        Assert.Null(response.Errors);
        Assert.Null(response.SourceHtml);
        Assert.Null(response.CleanHtmlEmbeddedImages);
        Assert.Null(response.CleanHtmlLinkedImages);
        Assert.Null(response.MarkdownLinkedImages);
        Assert.Null(response.AllLinksInPage);
        Assert.Null(response.AllLinksMedia);
        Assert.Null(response.AllLinksAssets);
        Assert.Null(response.AllInPageImages);
        Assert.Null(response.AllOutOfPageImages);
        Assert.Null(response.DebugInfo);
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
            Errors = ["Failed to fetch content", "DNS resolution failed"],
            TraceId = "xyz789"
        };

        Assert.False(response.Success);
        Assert.NotNull(response.Errors);
        Assert.Equal(2, response.Errors.Length);
        Assert.Null(response.Title);
        Assert.Null(response.PlainText);
        Assert.Null(response.CleanHtmlNoImages);
        Assert.Null(response.MarkdownNoImages);
    }

    [Fact]
    public void CompletionFlags_AreBitmaskValues()
    {
        Assert.Equal(0,  (int)CompletionFlag.Complete);
        Assert.Equal(1,  (int)CompletionFlag.ImageTruncated);
        Assert.Equal(2,  (int)CompletionFlag.SourceHtmlOmitted);
        Assert.Equal(4,  (int)CompletionFlag.EngineFallbackUsed);
        Assert.Equal(8,  (int)CompletionFlag.ImageLinkRewriteApplied);
        Assert.Equal(16, (int)CompletionFlag.ImageLinkRewriteImpliedInPageImages);
        Assert.Equal(32, (int)CompletionFlag.AllInPageImagesPartial);
        Assert.Equal(64, (int)CompletionFlag.AllOutOfPageImagesPartial);
    }

    [Fact]
    public void CompletionFlags_CanBeCombined()
    {
        var flags = CompletionFlag.ImageTruncated | CompletionFlag.EngineFallbackUsed;

        Assert.Equal(5, (int)flags);
        Assert.True(flags.HasFlag(CompletionFlag.ImageTruncated));
        Assert.True(flags.HasFlag(CompletionFlag.EngineFallbackUsed));
        Assert.False(flags.HasFlag(CompletionFlag.SourceHtmlOmitted));
        Assert.False(flags.HasFlag(CompletionFlag.ImageLinkRewriteApplied));
    }

    [Fact]
    public void CompletionFlags_ImageLinkRewriteWithImplied_CanBeCombined()
    {
        var flags = CompletionFlag.ImageLinkRewriteApplied | CompletionFlag.ImageLinkRewriteImpliedInPageImages;

        Assert.Equal(24, (int)flags);
        Assert.True(flags.HasFlag(CompletionFlag.ImageLinkRewriteApplied));
        Assert.True(flags.HasFlag(CompletionFlag.ImageLinkRewriteImpliedInPageImages));
    }

    [Fact]
    public void ProcessResponse_MetadataFields_CanBePopulated()
    {
        var response = new ProcessResponse
        {
            Success = true,
            RequestedUrl = "https://example.com",
            ProcessedAt = DateTimeOffset.UtcNow,
            ProcessingTimeMs = 1000,
            Title = "Test Article",
            Byline = "John Doe",
            Author = "John Doe",
            Excerpt = "A brief summary of the article.",
            SiteName = "Example News",
            Language = "en",
            PublicationDate = new DateTime(2026, 3, 12),
            FeaturedImage = "https://example.com/hero.jpg",
            TimeToRead = "00:05:00",
            CompletionFlags = 0,
            TraceId = "meta-test"
        };

        Assert.Equal("John Doe", response.Byline);
        Assert.Equal("John Doe", response.Author);
        Assert.Equal("A brief summary of the article.", response.Excerpt);
        Assert.Equal("Example News", response.SiteName);
        Assert.Equal("en", response.Language);
        Assert.Equal(new DateTime(2026, 3, 12), response.PublicationDate);
        Assert.Equal("https://example.com/hero.jpg", response.FeaturedImage);
        Assert.Equal("00:05:00", response.TimeToRead);
    }

    [Fact]
    public void ProcessResponse_MetadataFields_AreNullByDefault()
    {
        var response = new ProcessResponse
        {
            Success = true,
            RequestedUrl = "https://example.com",
            ProcessedAt = DateTimeOffset.UtcNow,
            ProcessingTimeMs = 100,
            CompletionFlags = 0,
            TraceId = "default-test"
        };

        Assert.Null(response.Byline);
        Assert.Null(response.Author);
        Assert.Null(response.Excerpt);
        Assert.Null(response.SiteName);
        Assert.Null(response.Language);
        Assert.Null(response.PublicationDate);
        Assert.Null(response.FeaturedImage);
        Assert.Null(response.TimeToRead);
    }

    [Fact]
    public void ImageEntry_LocalName_FollowsNamingConvention()
    {
        var entry = new ImageEntry
        {
            Index = 3,
            LocalName = "image-0003.jpg",
            OriginalUrl = "https://example.com/photo.jpg",
            OriginalMimeType = "image/jpeg",
            SizeBytes = 12345,
            Data = "base64data"
        };

        Assert.Equal("image-0003.jpg", entry.LocalName);
        Assert.Equal(3, entry.Index);
        Assert.Equal("image/jpeg", entry.OriginalMimeType);
    }
}

