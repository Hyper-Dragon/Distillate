using Distillate.Core.Images;

namespace Distillate.Tests.Images;

public class ImageProcessorTests
{
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
