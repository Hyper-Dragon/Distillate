using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Distillate.Core.Images;
using Distillate.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace Distillate.Tests.Images;

public class ImageProcessorTests
{
    private static readonly Uri BaseUri = new("https://example.com/article");
    private static readonly long MaxEmbedBytes = 10_000_000;

    // ---- Helpers ----

    /// <summary>Creates a minimal JPEG byte array of the given dimensions using SkiaSharp.</summary>
    private static byte[] MakeJpeg(int width, int height)
    {
        using var bmp = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.CornflowerBlue);
        using var data = bmp.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }

    // Minimal 1x1 JPEG for use in ProcessAsync tests.
    private static byte[] CreateJpegBytes() => MakeJpeg(1, 1);

    private static byte[] CreateSvgBytes() =>
        Encoding.UTF8.GetBytes("""<svg xmlns="http://www.w3.org/2000/svg" width="1" height="1"><rect width="1" height="1"/></svg>""");

    /// <summary>Returns true when the byte array starts with the PNG magic number (0x89 0x50 0x4E 0x47).</summary>
    private static bool IsPng(byte[] bytes) =>
        bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;

    /// <summary>Creates an <see cref="ImageProcessor"/> backed by a no-op HTTP client (not needed for ConvertToPng).</summary>
    private static ImageProcessor MakeProcessor() =>
        new(NullLogger<ImageProcessor>.Instance, new HttpClient(), maxEmbedBytes: 10 * 1024 * 1024);

    private static ImageProcessor BuildProcessor(byte[] responseBytes, string contentType) =>
        new(
            NullLogger<ImageProcessor>.Instance,
            new HttpClient(new FakeHttpMessageHandler(responseBytes, contentType)),
            MaxEmbedBytes);

    // --- ConvertToPng ---

    [Fact]
    public void ConvertToPng_JpegInput_ReturnsPngBytes()
    {
        var jpegBytes = MakeJpeg(100, 100);

        var result = ImageProcessor.ConvertToPng(jpegBytes);

        Assert.True(IsPng(result), "Expected PNG magic bytes in output");
    }

    [Fact]
    public void ConvertToPng_LargeJpeg_ReturnsPngWithResizedDimensions()
    {
        // 1920 x 1080 landscape -- exceeds the 720 px landscape limit
        var jpegBytes = MakeJpeg(1920, 1080);

        var result = ImageProcessor.ConvertToPng(jpegBytes);

        Assert.True(IsPng(result), "Expected PNG magic bytes in output");

        using var decoded = SKBitmap.Decode(result);
        Assert.NotNull(decoded);
        Assert.Equal(720, decoded.Height);
        Assert.Equal(1280, decoded.Width);
    }

    [Fact]
    public void ConvertToPng_SmallJpeg_ReturnsPngWithOriginalDimensions()
    {
        // 320 x 240 landscape -- within the 720 px landscape limit, no resize expected
        var jpegBytes = MakeJpeg(320, 240);

        var result = ImageProcessor.ConvertToPng(jpegBytes);

        Assert.True(IsPng(result), "Expected PNG magic bytes in output");

        using var decoded = SKBitmap.Decode(result);
        Assert.NotNull(decoded);
        Assert.Equal(320, decoded.Width);
        Assert.Equal(240, decoded.Height);
    }

    [Fact]
    public void ConvertToPng_PortraitJpegAboveLimit_ReturnsPngWithResizedHeight()
    {
        // 600 x 2160 portrait -- exceeds the 1080 px portrait limit
        var jpegBytes = MakeJpeg(600, 2160);

        var result = ImageProcessor.ConvertToPng(jpegBytes);

        Assert.True(IsPng(result), "Expected PNG magic bytes in output");

        using var decoded = SKBitmap.Decode(result);
        Assert.NotNull(decoded);
        Assert.Equal(1080, decoded.Height);
    }

    // --- ProcessAsync: LocalName and OriginalMimeType after JPEG -> PNG conversion ---

    [Fact]
    public async Task ProcessAsync_PngConversionRequired_JpegImage_LocalNameIsPng()
    {
        var processor = BuildProcessor(CreateJpegBytes(), "image/jpeg");
        var html = """<html><body><img src="https://example.com/photo.jpg" /></body></html>""";

        var result = await processor.ProcessAsync(
            sourceHtml: html, cleanHtml: html, baseUri: BaseUri,
            needEmbedded: false, needLinked: true, needNoImages: false,
            needInPageImages: true, needOutOfPageImages: false,
            imageLinkRewrite: true, pngConversionRequired: true,
            imageFetchLog: null, cancellationToken: CancellationToken.None);

        var image = Assert.Single(result.AllInPageImages!);
        Assert.Equal("image-0000.png", image.LocalName);
    }

    [Fact]
    public async Task ProcessAsync_PngConversionRequired_JpegImage_OriginalMimeTypeIsPng()
    {
        var processor = BuildProcessor(CreateJpegBytes(), "image/jpeg");
        var html = """<html><body><img src="https://example.com/photo.jpg" /></body></html>""";

        var result = await processor.ProcessAsync(
            sourceHtml: html, cleanHtml: html, baseUri: BaseUri,
            needEmbedded: false, needLinked: false, needNoImages: false,
            needInPageImages: true, needOutOfPageImages: false,
            imageLinkRewrite: false, pngConversionRequired: true,
            imageFetchLog: null, cancellationToken: CancellationToken.None);

        var image = Assert.Single(result.AllInPageImages!);
        Assert.Equal("image/png", image.OriginalMimeType);
    }

    [Fact]
    public async Task ProcessAsync_PngConversionRequired_JpegImage_DataBytesArePng()
    {
        var processor = BuildProcessor(CreateJpegBytes(), "image/jpeg");
        var html = """<html><body><img src="https://example.com/photo.jpg" /></body></html>""";

        var result = await processor.ProcessAsync(
            sourceHtml: html, cleanHtml: html, baseUri: BaseUri,
            needEmbedded: false, needLinked: false, needNoImages: false,
            needInPageImages: true, needOutOfPageImages: false,
            imageLinkRewrite: false, pngConversionRequired: true,
            imageFetchLog: null, cancellationToken: CancellationToken.None);

        var image = Assert.Single(result.AllInPageImages!);
        var bytes = Convert.FromBase64String(image.Data!);
        Assert.True(IsPng(bytes), "Expected PNG magic bytes in data");
    }

    // --- ProcessAsync: Linked HTML link rewrite uses .png after conversion ---

    [Fact]
    public async Task ProcessAsync_PngConversionRequired_LinkedHtmlReferencesPngFilename()
    {
        var processor = BuildProcessor(CreateJpegBytes(), "image/jpeg");
        var html = """<html><body><img src="https://example.com/photo.jpg" /></body></html>""";

        var result = await processor.ProcessAsync(
            sourceHtml: html, cleanHtml: html, baseUri: BaseUri,
            needEmbedded: false, needLinked: true, needNoImages: false,
            needInPageImages: true, needOutOfPageImages: false,
            imageLinkRewrite: true, pngConversionRequired: true,
            imageFetchLog: null, cancellationToken: CancellationToken.None);

        Assert.NotNull(result.CleanHtmlLinkedImages);
        Assert.Contains("image-0000.png", result.CleanHtmlLinkedImages);
        Assert.DoesNotContain("image-0000.jpg", result.CleanHtmlLinkedImages);
    }

    // --- ProcessAsync: SVG excluded from conversion when pngConversionRequired = true ---

    [Fact]
    public async Task ProcessAsync_PngConversionRequired_SvgImage_LocalNameRemainssvg()
    {
        var processor = BuildProcessor(CreateSvgBytes(), "image/svg+xml");
        var html = """<html><body><img src="https://example.com/icon.svg" /></body></html>""";

        var result = await processor.ProcessAsync(
            sourceHtml: html, cleanHtml: html, baseUri: BaseUri,
            needEmbedded: false, needLinked: true, needNoImages: false,
            needInPageImages: true, needOutOfPageImages: false,
            imageLinkRewrite: true, pngConversionRequired: true,
            imageFetchLog: null, cancellationToken: CancellationToken.None);

        var image = Assert.Single(result.AllInPageImages!);
        Assert.Equal("image-0000.svg", image.LocalName);
        Assert.Equal("image/svg+xml", image.OriginalMimeType);
    }

    [Fact]
    public async Task ProcessAsync_PngConversionRequired_SvgImage_LinkedHtmlReferencesSvgFilename()
    {
        var processor = BuildProcessor(CreateSvgBytes(), "image/svg+xml");
        var html = """<html><body><img src="https://example.com/icon.svg" /></body></html>""";

        var result = await processor.ProcessAsync(
            sourceHtml: html, cleanHtml: html, baseUri: BaseUri,
            needEmbedded: false, needLinked: true, needNoImages: false,
            needInPageImages: true, needOutOfPageImages: false,
            imageLinkRewrite: true, pngConversionRequired: true,
            imageFetchLog: null, cancellationToken: CancellationToken.None);

        Assert.NotNull(result.CleanHtmlLinkedImages);
        Assert.Contains("image-0000.svg", result.CleanHtmlLinkedImages);
        Assert.DoesNotContain("image-0000.png", result.CleanHtmlLinkedImages);
    }

    // --- ProcessAsync: No conversion when pngConversionRequired = false ---

    [Fact]
    public async Task ProcessAsync_NoPngConversion_JpegImage_RetainsOriginalMetadata()
    {
        var processor = BuildProcessor(CreateJpegBytes(), "image/jpeg");
        var html = """<html><body><img src="https://example.com/photo.jpg" /></body></html>""";

        var result = await processor.ProcessAsync(
            sourceHtml: html, cleanHtml: html, baseUri: BaseUri,
            needEmbedded: false, needLinked: true, needNoImages: false,
            needInPageImages: true, needOutOfPageImages: false,
            imageLinkRewrite: true, pngConversionRequired: false,
            imageFetchLog: null, cancellationToken: CancellationToken.None);

        var image = Assert.Single(result.AllInPageImages!);
        Assert.Equal("image-0000.jpg", image.LocalName);
        Assert.Equal("image/jpeg", image.OriginalMimeType);
        Assert.NotNull(result.CleanHtmlLinkedImages);
        Assert.Contains("image-0000.jpg", result.CleanHtmlLinkedImages);
    }

    // --- CalculatePngResizeDimensions ---

    [Fact]
    public void CalculatePngResizeDimensions_PortraitBelowLimit_ReturnsOriginal()
    {
        // 800 x 1000 -- portrait, height 1000 < 1080 limit
        var (w, h) = ImageProcessor.CalculatePngResizeDimensions(800, 1000);

        Assert.Equal(800, w);
        Assert.Equal(1000, h);
    }

    [Fact]
    public void CalculatePngResizeDimensions_PortraitAtLimit_ReturnsOriginal()
    {
        // Exactly at portrait limit
        var (w, h) = ImageProcessor.CalculatePngResizeDimensions(600, 1080);

        Assert.Equal(600, w);
        Assert.Equal(1080, h);
    }

    [Fact]
    public void CalculatePngResizeDimensions_PortraitAboveLimit_ScalesDown()
    {
        // 900 x 2160 -- portrait, 2x portrait limit -> should become 450 x 1080
        var (w, h) = ImageProcessor.CalculatePngResizeDimensions(900, 2160);

        Assert.Equal(1080, h);
        Assert.Equal(450, w);
    }

    [Fact]
    public void CalculatePngResizeDimensions_LandscapeBelowLimit_ReturnsOriginal()
    {
        // 1280 x 600 -- landscape, height 600 < 720 limit
        var (w, h) = ImageProcessor.CalculatePngResizeDimensions(1280, 600);

        Assert.Equal(1280, w);
        Assert.Equal(600, h);
    }

    [Fact]
    public void CalculatePngResizeDimensions_LandscapeAtLimit_ReturnsOriginal()
    {
        // Exactly at landscape limit
        var (w, h) = ImageProcessor.CalculatePngResizeDimensions(1280, 720);

        Assert.Equal(1280, w);
        Assert.Equal(720, h);
    }

    [Fact]
    public void CalculatePngResizeDimensions_LandscapeAboveLimit_ScalesDown()
    {
        // 2560 x 1440 -- landscape, 2x landscape limit -> should become 1280 x 720
        var (w, h) = ImageProcessor.CalculatePngResizeDimensions(2560, 1440);

        Assert.Equal(720, h);
        Assert.Equal(1280, w);
    }

    [Fact]
    public void CalculatePngResizeDimensions_SquareAboveLandscapeLimit_ScalesDown()
    {
        // Square image: equal width and height, treated as landscape (height not > width)
        // 1000 x 1000 -> max height 720 -> scale = 0.72 -> width = 720
        var (w, h) = ImageProcessor.CalculatePngResizeDimensions(1000, 1000);

        Assert.Equal(720, h);
        Assert.Equal(720, w);
    }

    [Fact]
    public void CalculatePngResizeDimensions_PortraitAspectRatioPreserved()
    {
        // 400 x 4000 -- very tall portrait
        // scale = 1080 / 4000 = 0.27 -> width = round(400 * 0.27) = round(108) = 108
        var (w, h) = ImageProcessor.CalculatePngResizeDimensions(400, 4000);

        Assert.Equal(1080, h);
        Assert.Equal((int)Math.Round(400 * (1080f / 4000f)), w);
    }

    [Fact]
    public void CalculatePngResizeDimensions_LandscapeAspectRatioPreserved()
    {
        // 3840 x 2160 -- 4K landscape
        // scale = 720 / 2160 = 1/3 -> width = round(3840 / 3) = 1280
        var (w, h) = ImageProcessor.CalculatePngResizeDimensions(3840, 2160);

        Assert.Equal(720, h);
        Assert.Equal((int)Math.Round(3840 * (720f / 2160f)), w);
    }

    [Fact]
    public void CalculatePngResizeDimensions_ZeroOrNegativeDimensions_ReturnedUnchanged()
    {
        Assert.Equal((0, 0), ImageProcessor.CalculatePngResizeDimensions(0, 0));
        Assert.Equal((-1, -1), ImageProcessor.CalculatePngResizeDimensions(-1, -1));
        Assert.Equal((100, 0), ImageProcessor.CalculatePngResizeDimensions(100, 0));
    }

    // --- ProcessAsync: excludeImageUrls ---

    [Fact]
    public async Task ProcessAsync_ExcludeImageUrlsAbsent_LeavesImagesAndNumberingUnchanged()
    {
        var processor = BuildCountingProcessor(CreateJpegBytes(), "image/jpeg", out var handler);
        const string html = """
            <html><body>
              <img src="https://example.com/photo-1.jpg" />
              <img src="https://example.com/donate/button.png" />
              <img src="https://example.com/photo-3.jpg" />
            </body></html>
            """;

        var result = await processor.ProcessAsync(
            sourceHtml: html, cleanHtml: html, baseUri: BaseUri,
            needEmbedded: false, needLinked: true, needNoImages: false,
            needInPageImages: true, needOutOfPageImages: false,
            imageLinkRewrite: true, pngConversionRequired: false,
            imageFetchLog: null, cancellationToken: CancellationToken.None);

        Assert.Equal(
            ["image-0000.jpg", "image-0001.jpg", "image-0002.jpg"],
            result.AllInPageImages!.Select(i => i.LocalName).ToArray());
        Assert.Equal(3, result.AllInPageImages!.Length);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(0, result.ExcludedImageCount);
    }

    [Fact]
    public async Task ProcessAsync_ExcludeImageUrlsEmptyArray_BehavesAsAbsent()
    {
        var processor = BuildCountingProcessor(CreateJpegBytes(), "image/jpeg", out var handler);
        const string html = """
            <html><body>
              <img src="https://example.com/photo-1.jpg" />
              <img src="https://example.com/donate/button.png" />
            </body></html>
            """;

        var result = await processor.ProcessAsync(
            sourceHtml: html, cleanHtml: html, baseUri: BaseUri,
            needEmbedded: false, needLinked: true, needNoImages: false,
            needInPageImages: true, needOutOfPageImages: false,
            imageLinkRewrite: true, pngConversionRequired: false,
            imageFetchLog: null, cancellationToken: CancellationToken.None,
            excludeImageUrls: []);

        Assert.Equal(2, result.AllInPageImages!.Length);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(0, result.ExcludedImageCount);
    }

    [Fact]
    public async Task ProcessAsync_ExcludeImageUrls_SubstringMatch_RemovesImageKeepsNumberingContiguous()
    {
        var processor = BuildCountingProcessor(CreateJpegBytes(), "image/jpeg", out var handler);
        const string html = """
            <html><body>
              <img src="https://example.com/photo-1.jpg" />
              <img src="https://counterpunch.org/wp-content/uploads/donate-today.png" />
              <img src="https://example.com/photo-3.jpg" />
            </body></html>
            """;

        var result = await processor.ProcessAsync(
            sourceHtml: html, cleanHtml: html, baseUri: BaseUri,
            needEmbedded: false, needLinked: true, needNoImages: false,
            needInPageImages: true, needOutOfPageImages: true,
            imageLinkRewrite: true, pngConversionRequired: false,
            imageFetchLog: null, cancellationToken: CancellationToken.None,
            excludeImageUrls: ["/donate"]);

        // The excluded image consumes no slot: 0000 and 0001 are the survivors, in document order.
        var images = result.AllInPageImages!;
        Assert.Equal(
            ["https://example.com/photo-1.jpg", "https://example.com/photo-3.jpg"],
            images.Select(i => i.OriginalUrl ?? string.Empty).ToArray());
        Assert.Equal(
            ["image-0000.jpg", "image-0001.jpg"],
            images.Select(i => i.LocalName).ToArray());

        // No download attempt for the excluded URL.
        Assert.DoesNotContain(handler.Requests, u => u.Contains("/donate", StringComparison.OrdinalIgnoreCase));

        // Removed from the document, not merely left un-rewritten.
        var linkedHtml = result.CleanHtmlLinkedImages!;
        Assert.DoesNotContain("donate", linkedHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("image-0000.jpg", linkedHtml);
        Assert.Contains("image-0001.jpg", linkedHtml);

        // ...and it does not walk back in through the out-of-page scan.
        Assert.True(
            result.AllOutOfPageImages is null || result.AllOutOfPageImages.Length == 0,
            "Excluded image must not reappear in allOutOfPageImages");
    }

    [Theory]
    [InlineData("/donate")]
    [InlineData("/DONATE")]
    [InlineData("/Donate")]
    public async Task ProcessAsync_ExcludeImageUrls_MatchIsCaseInsensitive(string pattern)
    {
        var processor = BuildProcessor(CreateJpegBytes(), "image/jpeg");
        const string html = """
            <html><body>
              <img src="https://counterpunch.org/wp-content/uploads/Donate-Today.png" />
              <img src="https://example.com/photo.jpg" />
            </body></html>
            """;

        var result = await processor.ProcessAsync(
            sourceHtml: html, cleanHtml: html, baseUri: BaseUri,
            needEmbedded: false, needLinked: true, needNoImages: false,
            needInPageImages: true, needOutOfPageImages: false,
            imageLinkRewrite: true, pngConversionRequired: false,
            imageFetchLog: null, cancellationToken: CancellationToken.None,
            excludeImageUrls: [pattern]);

        var remaining = Assert.Single(result.AllInPageImages!);
        Assert.Equal("https://example.com/photo.jpg", remaining.OriginalUrl);
        Assert.Equal("image-0000.jpg", remaining.LocalName);
    }

    [Fact]
    public async Task ProcessAsync_ExcludeImageUrls_RemovesPictureWrapperAndItsSources()
    {
        var processor = BuildProcessor(CreateJpegBytes(), "image/jpeg");
        const string html = """
            <html><body>
              <picture>
                <source srcset="https://example.com/donate/btn.webp" type="image/webp" />
                <img src="https://example.com/donate/btn.png" />
              </picture>
              <img src="https://example.com/photo.jpg" />
            </body></html>
            """;

        var result = await processor.ProcessAsync(
            sourceHtml: html, cleanHtml: html, baseUri: BaseUri,
            needEmbedded: false, needLinked: true, needNoImages: false,
            needInPageImages: true, needOutOfPageImages: false,
            imageLinkRewrite: true, pngConversionRequired: false,
            imageFetchLog: null, cancellationToken: CancellationToken.None,
            excludeImageUrls: ["/donate"]);

        Assert.NotNull(result.CleanHtmlLinkedImages);
        Assert.DoesNotContain("<picture", result.CleanHtmlLinkedImages, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<source", result.CleanHtmlLinkedImages, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("image-0000.jpg", result.CleanHtmlLinkedImages);
    }

    [Fact]
    public async Task ProcessAsync_ExcludeImageUrls_RemovesEmptyAnchorAndFigureButKeepsCaption()
    {
        var processor = BuildProcessor(CreateJpegBytes(), "image/jpeg");
        const string html = """
            <html><body>
              <p><a href="https://example.com/donate"><img src="https://example.com/donate/btn.png" /></a></p>
              <figure><img src="https://example.com/logo.png" /></figure>
              <figure><img src="https://example.com/site-logo.png" /><figcaption>Our logo</figcaption></figure>
              <p>Body text</p>
            </body></html>
            """;

        var result = await processor.ProcessAsync(
            sourceHtml: html, cleanHtml: html, baseUri: BaseUri,
            needEmbedded: false, needLinked: true, needNoImages: false,
            needInPageImages: true, needOutOfPageImages: false,
            imageLinkRewrite: true, pngConversionRequired: false,
            imageFetchLog: null, cancellationToken: CancellationToken.None,
            excludeImageUrls: ["/donate", "/logo"]);

        var output = result.CleanHtmlLinkedImages!;

        // The <a> that wrapped only the excluded image is gone.
        Assert.DoesNotContain("<a ", output, StringComparison.OrdinalIgnoreCase);

        // The image-only <figure> is gone; the captioned one survives.
        Assert.Equal(1, CountOccurrences(output, "<figure"));
        Assert.Contains("Our logo", output);
        Assert.Contains("Body text", output);
        Assert.DoesNotContain("donate", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessAsync_ExcludeImageUrls_ExcludedImageDoesNotReturnViaOutOfPageInventory()
    {
        var processor = BuildCountingProcessor(CreateJpegBytes(), "image/jpeg", out var handler);
        const string cleanHtml = """
            <html><body>
              <img src="https://example.com/photo-1.jpg" />
              <img src="https://counterpunch.org/wp-content/uploads/donate-today.png" />
            </body></html>
            """;
        const string sourceHtml = """
            <html><body>
              <img src="https://example.com/photo-1.jpg" />
              <img src="https://counterpunch.org/wp-content/uploads/donate-today.png" />
              <img src="https://example.com/sidebar-banner.jpg" />
            </body></html>
            """;

        var result = await processor.ProcessAsync(
            sourceHtml: sourceHtml, cleanHtml: cleanHtml, baseUri: BaseUri,
            needEmbedded: false, needLinked: true, needNoImages: false,
            needInPageImages: true, needOutOfPageImages: true,
            imageLinkRewrite: true, pngConversionRequired: false,
            imageFetchLog: null, cancellationToken: CancellationToken.None,
            excludeImageUrls: ["/donate"]);

        var inPage = Assert.Single(result.AllInPageImages!);
        Assert.Equal("https://example.com/photo-1.jpg", inPage.OriginalUrl);

        var outOfPage = Assert.Single(result.AllOutOfPageImages!);
        Assert.Equal("https://example.com/sidebar-banner.jpg", outOfPage.OriginalUrl);
        Assert.Equal("image-0000.jpg", outOfPage.LocalName);

        Assert.DoesNotContain(handler.Requests, u => u.Contains("/donate", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, result.ExcludedImageCount);   // 1 in-page removal + 1 out-of-page skip
    }

    [Fact]
    public async Task ProcessAsync_ExcludeImageUrls_BlankPatternsDoNotMatchEveryImage()
    {
        var processor = BuildProcessor(CreateJpegBytes(), "image/jpeg");
        const string html = """
            <html><body>
              <img src="https://example.com/photo-1.jpg" />
              <img src="https://example.com/photo-2.jpg" />
            </body></html>
            """;

        var result = await processor.ProcessAsync(
            sourceHtml: html, cleanHtml: html, baseUri: BaseUri,
            needEmbedded: false, needLinked: true, needNoImages: false,
            needInPageImages: true, needOutOfPageImages: false,
            imageLinkRewrite: true, pngConversionRequired: false,
            imageFetchLog: null, cancellationToken: CancellationToken.None,
            excludeImageUrls: ["", "   "]);

        Assert.Equal(2, result.AllInPageImages!.Length);
        Assert.Equal(0, result.ExcludedImageCount);
    }

    // --- excludeImageUrls helpers ---

    [Fact]
    public void FilterExcludedImages_OptionAbsent_ReturnsInputUnchanged()
    {
        const string html = """<p><img src="https://example.com/donate/x.png" /></p>""";

        var result = ImageProcessor.FilterExcludedImages(html, BaseUri, null, out var removedCount);

        Assert.Equal(0, removedCount);
        Assert.Same(html, result);
    }

    [Fact]
    public void FilterExcludedImages_SubstringHit_RemovesImageAndEmptyAnchor()
    {
        const string html = """
            <p>Before</p>
            <p><a href="https://example.com/donate"><img src="https://example.com/donate/btn.png" /></a></p>
            <p>After</p>
            """;

        var result = ImageProcessor.FilterExcludedImages(html, BaseUri, ["/donate"], out var removedCount);

        Assert.Equal(1, removedCount);
        Assert.DoesNotContain("<img", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<a ", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Before", result);
        Assert.Contains("After", result);
    }

    [Fact]
    public void IsImageExcluded_NullOrBlankPatterns_ReturnFalse()
    {
        const string url = "https://example.com/donate/button.png";

        Assert.False(ImageProcessor.IsImageExcluded(url, null));
        Assert.False(ImageProcessor.IsImageExcluded(url, Array.Empty<string>()));
        Assert.False(ImageProcessor.IsImageExcluded(url, ["", "   "]));
    }

    [Theory]
    [InlineData("https://example.com/donate/button.png", "/donate", true)]
    [InlineData("https://example.com/DONATE/button.png", "/donate", true)]
    [InlineData("https://example.com/wp-content/uploads/site-logo.png", "/logo", false)]
    [InlineData("https://example.com/wp-content/uploads/logo.png", "/logo", true)]
    [InlineData("https://example.com/donate/button.png", "/logo", false)]
    public void IsImageExcluded_MatchesAbsoluteUrlSubstringCaseInsensitively(string url, string pattern, bool expected)
        => Assert.Equal(expected, ImageProcessor.IsImageExcluded(url, [pattern]));

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    // --- Helpers ---

    private static ImageProcessor BuildCountingProcessor(
        byte[] responseBytes, string contentType, out CountingHttpMessageHandler handler)
    {
        handler = new CountingHttpMessageHandler(responseBytes, contentType);
        return new ImageProcessor(NullLogger<ImageProcessor>.Instance, new HttpClient(handler), MaxEmbedBytes);
    }

    private sealed class CountingHttpMessageHandler(byte[] responseBytes, string contentType) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new ByteArrayContent(responseBytes);
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            return Task.FromResult(response);
        }
    }

    private sealed class FakeHttpMessageHandler(byte[] responseBytes, string contentType) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new ByteArrayContent(responseBytes);
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            return Task.FromResult(response);
        }
    }
}
