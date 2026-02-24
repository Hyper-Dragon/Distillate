using AngleSharp;
using AngleSharp.Html.Parser;

namespace Distillate.Core.Images;

public static class LinkExtractor
{
    public static LinkExtractionResult Extract(string html, Uri baseUri)
    {
        var config = AngleSharp.Configuration.Default;
        var context = BrowsingContext.New(config);
        var parser = context.GetService<IHtmlParser>()!;
        var document = parser.ParseDocument(html);

        var linksInPage = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var linksMedia = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var linksAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var anchor in document.QuerySelectorAll("a[href]"))
        {
            var href = anchor.GetAttribute("href");
            if (TryMakeAbsolute(href, baseUri, out var abs))
                linksInPage.Add(abs);
        }

        foreach (var img in document.QuerySelectorAll("img[src]"))
        {
            var src = img.GetAttribute("src");
            if (TryMakeAbsolute(src, baseUri, out var abs))
                linksMedia.Add(abs);
        }

        foreach (var video in document.QuerySelectorAll("video[src]"))
        {
            var src = video.GetAttribute("src");
            if (TryMakeAbsolute(src, baseUri, out var abs))
                linksMedia.Add(abs);
        }

        foreach (var source in document.QuerySelectorAll("source[src]"))
        {
            var src = source.GetAttribute("src");
            if (TryMakeAbsolute(src, baseUri, out var abs))
                linksMedia.Add(abs);
        }

        foreach (var link in document.QuerySelectorAll("link[href]"))
        {
            var href = link.GetAttribute("href");
            if (TryMakeAbsolute(href, baseUri, out var abs))
                linksAssets.Add(abs);
        }

        foreach (var script in document.QuerySelectorAll("script[src]"))
        {
            var src = script.GetAttribute("src");
            if (TryMakeAbsolute(src, baseUri, out var abs))
                linksAssets.Add(abs);
        }

        return new LinkExtractionResult(
            linksInPage.ToArray(),
            linksMedia.ToArray(),
            linksAssets.ToArray());
    }

    private static bool TryMakeAbsolute(string? raw, Uri baseUri, out string absolute)
    {
        absolute = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        if (raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return false;
        if (raw.StartsWith('#'))
            return false;

        if (Uri.TryCreate(raw, UriKind.Absolute, out var uri) &&
            (uri.Scheme == "http" || uri.Scheme == "https"))
        {
            absolute = uri.AbsoluteUri;
            return true;
        }

        if (Uri.TryCreate(baseUri, raw, out var resolved) &&
            (resolved.Scheme == "http" || resolved.Scheme == "https"))
        {
            absolute = resolved.AbsoluteUri;
            return true;
        }

        return false;
    }
}

public record LinkExtractionResult(
    string[] LinksInPage,
    string[] LinksMedia,
    string[] LinksAssets);
