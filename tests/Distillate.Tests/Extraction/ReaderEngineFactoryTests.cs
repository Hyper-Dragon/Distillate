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
        var smartReaderLogger = Mock.Of<ILogger<SmartReaderEngine>>();
        _factory = new ReaderEngineFactory(smartReaderLogger);
    }

    [Fact]
    public void GetEngine_ReturnsSmartReaderEngine()
    {
        var engine = _factory.GetEngine();

        Assert.IsType<SmartReaderEngine>(engine);
        Assert.Equal("smartreader", engine.Name);
    }

    [Fact]
    public void GetEngine_WithAnyName_ReturnsSmartReader()
    {
        var engine = _factory.GetEngine("anything");

        Assert.IsType<SmartReaderEngine>(engine);
        Assert.Equal("smartreader", engine.Name);
    }
}
