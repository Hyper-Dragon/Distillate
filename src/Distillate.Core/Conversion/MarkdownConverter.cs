using ReverseMarkdown;

namespace Distillate.Core.Conversion;

public class MarkdownConverter
{
    private readonly Converter _converter;

    public MarkdownConverter()
    {
        var config = new Config
        {
            UnknownTags = Config.UnknownTagsOption.Bypass,
            GithubFlavored = false,
            RemoveComments = true,
            SmartHrefHandling = true
        };

        _converter = new Converter(config);
    }

    public string? ConvertToMarkdown(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;

        try
        {
            return _converter.Convert(html)?.Trim();
        }
        catch
        {
            return null;
        }
    }
}
