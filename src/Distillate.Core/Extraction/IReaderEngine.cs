namespace Distillate.Core.Extraction;

public interface IReaderEngine
{
    string Name { get; }
    ExtractionResult Extract(string html, Uri baseUri);
}
