using Distillate.Core.Images;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace Distillate.Tests.Images;

public class ImageProcessorTests
{
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

    /// <summary>Returns true when the byte array starts with the PNG magic number (0x89 0x50 0x4E 0x47).</summary>
    private static bool IsPng(byte[] bytes) =>
        bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;

    /// <summary>Creates an <see cref="ImageProcessor"/> backed by a no-op HTTP client (not needed for ConvertToPng).</summary>
    private static ImageProcessor MakeProcessor() =>
        new(NullLogger<ImageProcessor>.Instance, new HttpClient(), maxEmbedBytes: 10 * 1024 * 1024);

    // --- ConvertToPng ---

    [Fact]
    public void ConvertToPng_JpegInput_ReturnsPngBytes()
    {
        var processor = MakeProcessor();
        var jpegBytes = MakeJpeg(100, 100);

        var result = processor.ConvertToPng(jpegBytes);

        Assert.True(IsPng(result), "Expected PNG magic bytes in output");
    }

    [Fact]
    public void ConvertToPng_LargeJpeg_ReturnsPngWithResizedDimensions()
    {
        // 1920 × 1080 landscape — exceeds the 720 px landscape limit
        var processor = MakeProcessor();
        var jpegBytes = MakeJpeg(1920, 1080);

        var result = processor.ConvertToPng(jpegBytes);

        Assert.True(IsPng(result), "Expected PNG magic bytes in output");

        using var decoded = SKBitmap.Decode(result);
        Assert.NotNull(decoded);
        Assert.Equal(720, decoded.Height);
        Assert.Equal(1280, decoded.Width);
    }

    [Fact]
    public void ConvertToPng_SmallJpeg_ReturnsPngWithOriginalDimensions()
    {
        // 320 × 240 landscape — within the 720 px landscape limit, no resize expected
        var processor = MakeProcessor();
        var jpegBytes = MakeJpeg(320, 240);

        var result = processor.ConvertToPng(jpegBytes);

        Assert.True(IsPng(result), "Expected PNG magic bytes in output");

        using var decoded = SKBitmap.Decode(result);
        Assert.NotNull(decoded);
        Assert.Equal(320, decoded.Width);
        Assert.Equal(240, decoded.Height);
    }

    [Fact]
    public void ConvertToPng_PortraitJpegAboveLimit_ReturnsPngWithResizedHeight()
    {
        // 600 × 2160 portrait — exceeds the 1080 px portrait limit
        var processor = MakeProcessor();
        var jpegBytes = MakeJpeg(600, 2160);

        var result = processor.ConvertToPng(jpegBytes);

        Assert.True(IsPng(result), "Expected PNG magic bytes in output");

        using var decoded = SKBitmap.Decode(result);
        Assert.NotNull(decoded);
        Assert.Equal(1080, decoded.Height);
    }

    // --- CalculatePngResizeDimensions ---

    [Fact]
    public void CalculatePngResizeDimensions_PortraitBelowLimit_ReturnsOriginal()
    {
        // 800 × 1000 — portrait, height 1000 < 1080 limit
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
        // 900 × 2160 — portrait, 2× portrait limit → should become 450 × 1080
        var (w, h) = ImageProcessor.CalculatePngResizeDimensions(900, 2160);

        Assert.Equal(1080, h);
        Assert.Equal(450, w);
    }

    [Fact]
    public void CalculatePngResizeDimensions_LandscapeBelowLimit_ReturnsOriginal()
    {
        // 1280 × 600 — landscape, height 600 < 720 limit
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
        // 2560 × 1440 — landscape, 2× landscape limit → should become 1280 × 720
        var (w, h) = ImageProcessor.CalculatePngResizeDimensions(2560, 1440);

        Assert.Equal(720, h);
        Assert.Equal(1280, w);
    }

    [Fact]
    public void CalculatePngResizeDimensions_SquareAboveLandscapeLimit_ScalesDown()
    {
        // Square image: equal width and height, treated as landscape (height not > width)
        // 1000 × 1000 → max height 720 → scale = 0.72 → width = 720
        var (w, h) = ImageProcessor.CalculatePngResizeDimensions(1000, 1000);

        Assert.Equal(720, h);
        Assert.Equal(720, w);
    }

    [Fact]
    public void CalculatePngResizeDimensions_PortraitAspectRatioPreserved()
    {
        // 400 × 4000 — very tall portrait
        // scale = 1080 / 4000 = 0.27 → width = round(400 * 0.27) = round(108) = 108
        var (w, h) = ImageProcessor.CalculatePngResizeDimensions(400, 4000);

        Assert.Equal(1080, h);
        Assert.Equal((int)Math.Round(400 * (1080f / 4000f)), w);
    }

    [Fact]
    public void CalculatePngResizeDimensions_LandscapeAspectRatioPreserved()
    {
        // 3840 × 2160 — 4K landscape
        // scale = 720 / 2160 = 1/3 → width = round(3840 / 3) = 1280
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
}
