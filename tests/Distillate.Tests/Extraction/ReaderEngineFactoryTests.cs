using Distillate.Core.Extraction;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Distillate.Tests.Extraction;

public class ReaderEngineFactoryTests
{
    private readonly ReaderEngineFactory _factory;

    public ReaderEngineFactoryTests()
    {
        var readabilityLogger = Mock.Of<ILogger<ReadabilityEngine>>();
        var smartReaderLogger = Mock.Of<ILogger<SmartReaderEngine>>();
        _factory = new ReaderEngineFactory(readabilityLogger, smartReaderLogger);
    }

    [Theory]
    [InlineData("readability")]
    [InlineData("Readability")]
    [InlineData("READABILITY")]
    public void GetEngine_Readability_ReturnsReadabilityEngine(string engineName)
    {
        var engine = _factory.GetEngine(engineName);
        
        Assert.IsType<ReadabilityEngine>(engine);
        Assert.Equal("readability", engine.Name);
    }

    [Theory]
    [InlineData("smartreader")]
    [InlineData("SmartReader")]
    [InlineData("SMARTREADER")]
    public void GetEngine_SmartReader_ReturnsSmartReaderEngine(string engineName)
    {
        var engine = _factory.GetEngine(engineName);
        
        Assert.IsType<SmartReaderEngine>(engine);
        Assert.Equal("smartreader", engine.Name);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData("invalid")]
    public void GetEngine_Unknown_DefaultsToReadability(string engineName)
    {
        var engine = _factory.GetEngine(engineName);
        
        Assert.IsType<ReadabilityEngine>(engine);
    }

    [Fact]
    public void GetFallbackEngine_FromReadability_ReturnsSmartReader()
    {
        var fallback = _factory.GetFallbackEngine("readability");
        
        Assert.IsType<SmartReaderEngine>(fallback);
    }

    [Fact]
    public void GetFallbackEngine_FromSmartReader_ReturnsReadability()
    {
        var fallback = _factory.GetFallbackEngine("smartreader");
        
        Assert.IsType<ReadabilityEngine>(fallback);
    }
}
