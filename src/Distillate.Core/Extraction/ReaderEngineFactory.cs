using Microsoft.Extensions.Logging;

namespace Distillate.Core.Extraction;

public class ReaderEngineFactory
{
    private readonly ILogger<ReadabilityEngine> _readabilityLogger;
    private readonly ILogger<SmartReaderEngine> _smartReaderLogger;

    public ReaderEngineFactory(
        ILogger<ReadabilityEngine> readabilityLogger,
        ILogger<SmartReaderEngine> smartReaderLogger)
    {
        _readabilityLogger = readabilityLogger;
        _smartReaderLogger = smartReaderLogger;
    }

    public IReaderEngine GetEngine(string engineName)
    {
        return engineName.ToLowerInvariant() switch
        {
            "smartreader" => new SmartReaderEngine(_smartReaderLogger),
            "readability" => new ReadabilityEngine(_readabilityLogger),
            _ => new ReadabilityEngine(_readabilityLogger)
        };
    }

    public IReaderEngine GetFallbackEngine(string currentEngineName)
    {
        return currentEngineName.ToLowerInvariant() switch
        {
            "smartreader" => new ReadabilityEngine(_readabilityLogger),
            _ => new SmartReaderEngine(_smartReaderLogger)
        };
    }
}
