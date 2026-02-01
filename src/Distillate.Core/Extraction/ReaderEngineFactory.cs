using Microsoft.Extensions.Logging;

namespace Distillate.Core.Extraction;

public class ReaderEngineFactory(ILogger<SmartReaderEngine> smartReaderLogger)
{
    public IReaderEngine GetEngine(string? _ = null)
    {
        return new SmartReaderEngine(smartReaderLogger);
    }
}
