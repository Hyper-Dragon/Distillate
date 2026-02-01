using System.Text;
using System.Text.RegularExpressions;
using ReverseMarkdown;

namespace Distillate.Core.Conversion;

public partial class MarkdownConverter
{
    private readonly Converter _converter = new(new Config
    {
        UnknownTags = Config.UnknownTagsOption.Bypass,
        GithubFlavored = false,
        RemoveComments = true,
        SmartHrefHandling = true
    });

    public string? ConvertToMarkdown(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;

        try
        {
            var markdown = _converter.Convert(html);
            return markdown is null ? null : NormaliseImageMarkdown(markdown).Trim();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Guarantees a single canonical shape for every markdown image: inline
    /// <c>![alt](target)</c>, sitting on its own paragraph whenever it is in block position.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A consumer must never have to repair image syntax. Two malformed shapes are repaired:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     A reference-style image with an <em>empty</em> identifier followed by an inline target —
    ///     <c>![alt][](image-0000.png)</c>. That is not valid CommonMark: the renderer looks for a
    ///     reference definition named "" and fails, so the markup survives to the output as literal
    ///     text. The empty identifier is dropped, keeping both alt text and target.
    ///   </item>
    ///   <item>
    ///     A destination separated from its label by whitespace — <c>![alt] (image-0000.png)</c> —
    ///     which is likewise literal text. Only targets that carry a file extension are joined, so
    ///     prose such as <c>![diagram] (see below)</c> is left alone.
    ///   </item>
    /// </list>
    /// <para>
    /// Images glued to prose (<c>![](image-0000.png)On Tuesday…</c>) are then moved onto their own
    /// paragraph, because in this pipeline a locally rewritten image is a figure, not an inline
    /// character. Lines that legitimately keep an image inline are untouched: images inside a link
    /// label, image runs with no prose neighbour, fenced/indented code, list items and block
    /// quotes.
    /// </para>
    /// </remarks>
    internal static string NormaliseImageMarkdown(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return markdown;

        // 1. ![alt][](target) — empty reference identifier plus inline destination.
        var result = EmptyReferenceImageRegex().Replace(markdown, "![$1]($2)");

        // 2. ![alt] (target.ext) — destination detached by whitespace.
        result = DetachedImageTargetRegex().Replace(result, "![$1]($2)");

        // 3. Block-level images on their own paragraph.
        return SeparateBlockLevelImages(result);
    }

    /// <summary>
    /// Moves images glued to prose onto their own paragraph, line by line, while leaving code
    /// blocks, list items, block quotes and images inside a link label untouched.
    /// </summary>
    private static string SeparateBlockLevelImages(string markdown)
    {
        var lines = markdown.Split('\n');
        var builder = new StringBuilder(markdown.Length + 16);
        string? fence = null;

        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
                builder.Append('\n');

            var line = lines[i];
            var trimmed = line.TrimStart();

            if (fence is not null)
            {
                builder.Append(line);
                if (IsClosingFence(trimmed, fence))
                    fence = null;
                continue;
            }

            if (trimmed.StartsWith("```", StringComparison.Ordinal) ||
                trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                fence = trimmed[..3];
                builder.Append(line);
                continue;
            }

            // Indented code, list items and block quotes own their line layout: a paragraph
            // break inserted there would change the block structure, not just the spacing.
            if (trimmed.Length == 0 ||
                line.StartsWith("    ", StringComparison.Ordinal) ||
                line.StartsWith('\t') ||
                ListOrQuoteLineRegex().IsMatch(trimmed))
            {
                builder.Append(line);
                continue;
            }

            builder.Append(SeparateGluedImagesOnLine(line));
        }

        return builder.ToString();
    }

    private static string SeparateGluedImagesOnLine(string line)
    {
        var matches = ImageRegex().Matches(line);
        if (matches.Count == 0)
            return line;

        // Positions where a paragraph break has to be inserted; applied back-to-front so the
        // earlier offsets stay valid.
        var breaks = new List<int>();
        var cursor = 0;
        var depth = 0;

        foreach (Match match in matches)
        {
            // Track link-label nesting up to this image. The image's own '[' is inside the match,
            // so it cannot inflate the depth measured here.
            for (var i = cursor; i < match.Index; i++)
                depth = AdvanceBracketDepth(depth, line, i);

            var insideLinkLabel = depth > 0;
            var beforeIndex = match.Index - 1;
            var afterIndex = match.Index + match.Length;
            var gluedBefore = beforeIndex >= 0 && IsProseChar(line[beforeIndex]);
            var gluedAfter = afterIndex < line.Length && IsProseChar(line[afterIndex]);

            if (!insideLinkLabel && (gluedBefore || gluedAfter))
            {
                if (gluedBefore)
                    breaks.Add(match.Index);
                if (gluedAfter)
                    breaks.Add(afterIndex);
            }

            cursor = afterIndex;
        }

        if (breaks.Count == 0)
            return line;

        var builder = new StringBuilder(line.Length + breaks.Count * 2);
        var position = 0;
        foreach (var insertAt in breaks.OrderBy(p => p))
        {
            builder.Append(line, position, insertAt - position);
            builder.Append("\n\n");
            position = insertAt;
        }
        builder.Append(line, position, line.Length - position);
        return builder.ToString();
    }

    private static int AdvanceBracketDepth(int depth, string line, int index)
    {
        var c = line[index];
        if (c is not ('[' or ']'))
            return depth;

        // An escaped bracket (\[ or \]) is literal text, not structure.
        var backslashes = 0;
        for (var i = index - 1; i >= 0 && line[i] == '\\'; i--)
            backslashes++;

        if (backslashes % 2 == 1)
            return depth;

        return c == '[' ? depth + 1 : Math.Max(0, depth - 1);
    }

    /// <summary>
    /// True for a character that reads as prose next to an image. Image delimiters and link
    /// brackets are excluded so that adjacent images are never separated mid-syntax.
    /// </summary>
    private static bool IsProseChar(char c) =>
        !char.IsWhiteSpace(c) && c is not ('!' or '[' or ']' or '(' or ')');

    private static bool IsClosingFence(string trimmed, string fence) =>
        trimmed.TrimEnd().Length >= fence.Length &&
        trimmed.TrimEnd().All(c => c == fence[0]);

    [GeneratedRegex(@"!\[([^\]]*)\]\[\]\s*\(\s*([^)]*?)\s*\)")]
    private static partial Regex EmptyReferenceImageRegex();

    [GeneratedRegex(@"!\[([^\]]*)\]\s+\(\s*([^\s()]+\.[A-Za-z0-9]{1,8}(?:\s+""[^""]*"")?)\s*\)")]
    private static partial Regex DetachedImageTargetRegex();

    [GeneratedRegex(@"!\[[^\]]*\]\([^)\s]*\)")]
    private static partial Regex ImageRegex();

    [GeneratedRegex(@"^(?:[-*+]\s|\d{1,9}[.)]\s|>)")]
    private static partial Regex ListOrQuoteLineRegex();
}
