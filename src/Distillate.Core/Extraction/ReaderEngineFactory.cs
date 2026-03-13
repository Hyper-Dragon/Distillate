using Microsoft.Extensions.Logging;

namespace Distillate.Core.Extraction;

public class ReaderEngineFactory
{
    private readonly ILogger<SmartReaderEngine> _smartReaderLogger;

    public ReaderEngineFactory(ILogger<SmartReaderEngine> smartReaderLogger)
    {
        _smartReaderLogger = smartReaderLogger;
    }

    public IReaderEngine GetEngine(string? engineName = null)
    {
        return new SmartReaderEngine(_smartReaderLogger);
    }
}
