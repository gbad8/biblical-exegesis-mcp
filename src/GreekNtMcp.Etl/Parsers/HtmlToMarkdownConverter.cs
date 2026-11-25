using System.Net;
using System.Text.RegularExpressions;

namespace GreekNtMcp.Etl.Parsers;

public static partial class HtmlToMarkdownConverter
{
    [GeneratedRegex(@"<a\s+[^>]*title=""([^""]*)""[^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled)]
    private static partial Regex AnchorWithTitleRegex();

    [GeneratedRegex(@"<ref=['""][^'""]*['""]>(.*?)</ref>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled)]
    private static partial Regex RefTagRegex();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex BrTagRegex();

    [GeneratedRegex(@"<b>(.*?)</b>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled)]
    private static partial Regex BoldTagRegex();

    [GeneratedRegex(@"<i>(.*?)</i>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled)]
    private static partial Regex ItalicTagRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Compiled)]
    private static partial Regex AnyHtmlTagRegex();

    [GeneratedRegex(@"[ \t]{2,}", RegexOptions.Compiled)]
    private static partial Regex MultiSpaceRegex();

    [GeneratedRegex(@"\n{3,}", RegexOptions.Compiled)]
    private static partial Regex MultiNewlineRegex();

    /// <summary>
    /// Converts STEPBible TBESG/TFLSJ HTML markup into clean, readable Markdown.
    /// When includeAnchorTitles is true, preserves LSJ bibliographic citations hidden inside <a title="..."> attributes.
    /// </summary>
    public static string Convert(string? html, bool includeAnchorTitles = true)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var s = html;

        // Convert <a title="4th c.BC: Aristoteles ...">Refs 4th c.BC+</a>
        s = AnchorWithTitleRegex().Replace(s, m =>
        {
            var title = WebUtility.HtmlDecode(m.Groups[1].Value).Trim();
            var label = WebUtility.HtmlDecode(m.Groups[2].Value).Trim();
            if (!includeAnchorTitles || string.IsNullOrWhiteSpace(title))
                return label;
            return $"{label} ({title})";
        });

        // <ref='Mat.1.1'>Mat.1:1</ref> -> Mat.1:1
        s = RefTagRegex().Replace(s, "$1");

        // Line breaks and STEPBible indentation markers (__1. / ___a.)
        s = BrTagRegex().Replace(s, "\n");
        s = s.Replace("____", "      - ").Replace("___", "    - ").Replace("__", "  - ");

        // Bold & italic
        s = BoldTagRegex().Replace(s, m =>
        {
            var inner = m.Groups[1].Value.Trim();
            return string.IsNullOrEmpty(inner) ? string.Empty : $"**{inner}**";
        });
        s = ItalicTagRegex().Replace(s, m =>
        {
            var inner = m.Groups[1].Value.Trim();
            return string.IsNullOrEmpty(inner) ? string.Empty : $"*{inner}*";
        });

        // Strip any remaining HTML tags
        s = AnyHtmlTagRegex().Replace(s, string.Empty);
        s = WebUtility.HtmlDecode(s);

        // Normalize whitespace while keeping intentional line breaks
        var lines = s.Split('\n').Select(l => MultiSpaceRegex().Replace(l.TrimEnd(), " "));
        s = string.Join('\n', lines);
        s = MultiNewlineRegex().Replace(s, "\n\n").Trim();

        return s;
    }

    public static string BuildSummary(string fullMarkdown, int maxChars = 750)
    {
        if (string.IsNullOrWhiteSpace(fullMarkdown))
            return string.Empty;

        if (fullMarkdown.Length <= maxChars)
            return fullMarkdown;

        var cut = fullMarkdown[..maxChars];
        var lastStop = cut.LastIndexOfAny(['.', ';', '\n']);
        if (lastStop > maxChars / 2)
            cut = cut[..(lastStop + 1)];

        return cut.TrimEnd() + " …";
    }
}
