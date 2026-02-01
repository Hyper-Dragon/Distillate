using Distillate.Core.Configuration;
using Xunit;

namespace Distillate.Tests.Configuration;

public class DistillateOptionsTests
{
    [Fact]
    public void DistillateOptions_DefaultValues_AreCorrect()
    {
        var options = new DistillateOptions();

        Assert.Equal(1, options.Concurrency);
        Assert.Equal(180_000, options.MaxTimeMs);
        Assert.Equal(200 * 1024 * 1024, options.MaxImageBytes);
        Assert.Equal("normal", options.GcMode);
        Assert.Equal(10_000, options.StabilizationMs);
        Assert.Equal(2048, options.MaxUrlLength);
        Assert.Equal("/usr/bin/chromium", options.ChromiumPath);
        Assert.Equal(8080, options.Port);
        Assert.Equal(100, options.ScrollDelayMs);
        Assert.Equal(512, options.MaxV8HeapSizeMb);
        Assert.False(options.IsAggressiveGc);
        Assert.Equal("rest", options.Mode);
        Assert.False(options.IsLiveMode);
    }

    [Fact]
    public void DistillateOptions_AggressiveGcMode_IsDetected()
    {
        var options = new DistillateOptions { GcMode = "aggressive" };
        Assert.True(options.IsAggressiveGc);

        options = new DistillateOptions { GcMode = "Aggressive" };
        Assert.True(options.IsAggressiveGc);

        options = new DistillateOptions { GcMode = "AGGRESSIVE" };
        Assert.True(options.IsAggressiveGc);
    }

    [Fact]
    public void DistillateOptions_NormalGcMode_IsNotAggressive()
    {
        var options = new DistillateOptions { GcMode = "normal" };
        Assert.False(options.IsAggressiveGc);

        options = new DistillateOptions { GcMode = "whatever" };
        Assert.False(options.IsAggressiveGc);
    }

    [Fact]
    public void DistillateOptions_DefaultMode_IsRest()
    {
        var options = new DistillateOptions();
        Assert.Equal("rest", options.Mode);
        Assert.False(options.IsLiveMode);
    }

    [Fact]
    public void DistillateOptions_LiveMode_IsDetected()
    {
        var options = new DistillateOptions { Mode = "live" };
        Assert.True(options.IsLiveMode);

        options = new DistillateOptions { Mode = "Live" };
        Assert.True(options.IsLiveMode);

        options = new DistillateOptions { Mode = "LIVE" };
        Assert.True(options.IsLiveMode);
    }

    [Fact]
    public void DistillateOptions_RestMode_IsNotLive()
    {
        var options = new DistillateOptions { Mode = "rest" };
        Assert.False(options.IsLiveMode);

        options = new DistillateOptions { Mode = "whatever" };
        Assert.False(options.IsLiveMode);
    }
}
