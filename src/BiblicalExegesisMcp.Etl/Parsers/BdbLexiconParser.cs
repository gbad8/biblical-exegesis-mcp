using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using BiblicalExegesisMcp.Domain.Models;
using BiblicalExegesisMcp.Domain.Normalization;

namespace BiblicalExegesisMcp.Etl.Parsers;

public sealed record LexicalIndexMetadata(
    string StrongCode,
    string? BdbId,
    string? RootHebrew,
    string? TwotRef,
    string? ShortDef);

public static partial class BdbLexiconParser
{
    [GeneratedRegex(@"[ \t]{2,}", RegexOptions.Compiled)]
    private static partial Regex MultiSpaceRegex();

    /// <summary>
    /// Parses openscriptures/HebrewLexicon LexicalIndex.xml to bridge BDB IDs (e.g. "a.ac.aa")
    /// to canonical Hebrew Strong's codes, trilateral roots, and TWOT references.
    /// </summary>
    public static (Dictionary<string, List<LexicalIndexMetadata>> ByBdbId, Dictionary<string, LexicalIndexMetadata> ByStrongCode)
        ParseLexicalIndex(string xmlPath)
    {
        using var reader = XmlReader.Create(xmlPath, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
        return ParseLexicalIndexDocument(XDocument.Load(reader));
    }

    public static (Dictionary<string, List<LexicalIndexMetadata>> ByBdbId, Dictionary<string, LexicalIndexMetadata> ByStrongCode)
        ParseLexicalIndexDocument(XDocument doc)
    {
        // First pass: map internal LexicalIndex entry id (e.g. "aaf") -> root Hebrew consonants (e.g. "אבד")
        var idToRoot = new Dictionary<string, string>(StringComparer.Ordinal);
        var entries = doc.Descendants().Where(e => e.Name.LocalName == "entry").ToList();

        foreach (var el in entries)
        {
            var id = el.Attribute("id")?.Value?.Trim();
            var etym = el.Elements().FirstOrDefault(c => c.Name.LocalName == "etym");
            var root = etym?.Attribute("root")?.Value?.Trim();
            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(root))
                idToRoot[id] = root;
        }

        var byBdbId = new Dictionary<string, List<LexicalIndexMetadata>>(StringComparer.OrdinalIgnoreCase);
        var byStrong = new Dictionary<string, LexicalIndexMetadata>(StringComparer.OrdinalIgnoreCase);

        foreach (var el in entries)
        {
            var xref = el.Elements().FirstOrDefault(c => c.Name.LocalName == "xref");
            if (xref is null)
                continue;

            var bdbId = xref.Attribute("bdb")?.Value?.Trim();
            var strongRaw = xref.Attribute("strong")?.Value?.Trim();
            var twot = xref.Attribute("twot")?.Value?.Trim();
            var def = el.Elements().FirstOrDefault(c => c.Name.LocalName == "def")?.Value?.Trim();

            var etym = el.Elements().FirstOrDefault(c => c.Name.LocalName == "etym");
            var root = etym?.Attribute("root")?.Value?.Trim();
            if (string.IsNullOrEmpty(root) && etym is not null)
            {
                var parentId = etym.Value.Trim();
                if (idToRoot.TryGetValue(parentId, out var parentRoot))
                    root = parentRoot;
            }

            var strongParts = StrongCodeNormalizer.TryNormalize(strongRaw, 'H');
            if (strongParts is null)
                continue;

            var meta = new LexicalIndexMetadata(strongParts.BaseStrongCode, bdbId, root, twot, def);
            byStrong.TryAdd(strongParts.BaseStrongCode, meta);

            if (!string.IsNullOrEmpty(bdbId))
            {
                if (!byBdbId.TryGetValue(bdbId, out var list))
                {
                    list = [];
                    byBdbId[bdbId] = list;
                }
                if (!list.Any(m => m.StrongCode == meta.StrongCode))
                    list.Add(meta);
            }
        }

        return (byBdbId, byStrong);
    }

    /// <summary>
    /// Parses BrownDriverBriggs.xml into clean Markdown entries mapped to Strong's codes via LexicalIndex.
    /// </summary>
    public static List<BdbEntry> ParseBdbFile(
        string xmlPath,
        Dictionary<string, List<LexicalIndexMetadata>> indexByBdbId)
    {
        using var reader = XmlReader.Create(xmlPath, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
        return ParseBdbDocument(XDocument.Load(reader), indexByBdbId);
    }

    public static List<BdbEntry> ParseBdbDocument(
        XDocument doc,
        Dictionary<string, List<LexicalIndexMetadata>> indexByBdbId)
    {
        var results = new List<BdbEntry>(12000);

        foreach (var el in doc.Descendants().Where(e => e.Name.LocalName == "entry"))
        {
            var bdbId = el.Attribute("id")?.Value?.Trim();
            if (string.IsNullOrEmpty(bdbId))
                continue;

            var entryType = el.Attribute("type")?.Value?.Trim();
            var firstWord = el.Elements().FirstOrDefault(c => c.Name.LocalName == "w")?.Value?.Trim()
                            ?? el.Descendants().FirstOrDefault(c => c.Name.LocalName == "w")?.Value?.Trim()
                            ?? string.Empty;
            firstWord = firstWord.Normalize(NormalizationForm.FormC);
            if (string.IsNullOrWhiteSpace(firstWord))
                continue;

            var headNorm = HebrewNormalizer.NormalizeHebrew(firstWord);
            var fullMd = RenderBdbEntryMarkdown(el);
            if (string.IsNullOrWhiteSpace(fullMd))
                continue;

            var summaryMd = HtmlToMarkdownConverter.BuildSummary(fullMd, maxChars: 800);

            if (indexByBdbId.TryGetValue(bdbId, out var metas) && metas.Count > 0)
            {
                foreach (var meta in metas)
                {
                    results.Add(new BdbEntry
                    {
                        BdbId = bdbId,
                        StrongCode = meta.StrongCode,
                        BaseStrongCode = meta.StrongCode,
                        Headword = firstWord,
                        HeadwordNormalized = headNorm,
                        EntryType = entryType,
                        TwotRef = meta.TwotRef,
                        SummaryMarkdown = summaryMd,
                        FullMarkdown = fullMd,
                        CharLength = fullMd.Length
                    });
                }
            }
            else
            {
                // Keep unmapped BDB roots/entries searchable by headword_normalized fallback
                results.Add(new BdbEntry
                {
                    BdbId = bdbId,
                    StrongCode = null,
                    BaseStrongCode = null,
                    Headword = firstWord,
                    HeadwordNormalized = headNorm,
                    EntryType = entryType,
                    TwotRef = null,
                    SummaryMarkdown = summaryMd,
                    FullMarkdown = fullMd,
                    CharLength = fullMd.Length
                });
            }
        }

        return results;
    }

    private static string RenderBdbEntryMarkdown(XElement entryEl)
    {
        var sb = new StringBuilder();
        var headerSb = new StringBuilder();

        foreach (var node in entryEl.Nodes())
        {
            if (node is XElement child && child.Name.LocalName is "sense" or "status" or "page")
                continue;
            AppendInlineNode(node, headerSb);
        }

        var header = CollapseSpaces(headerSb.ToString());
        if (!string.IsNullOrWhiteSpace(header))
            sb.AppendLine(header);

        foreach (var senseEl in entryEl.Elements().Where(c => c.Name.LocalName == "sense"))
        {
            RenderSense(senseEl, 0, sb);
        }

        return sb.ToString().Trim();
    }

    private static void RenderSense(XElement senseEl, int depth, StringBuilder sb)
    {
        var num = senseEl.Attribute("n")?.Value?.Trim();
        var ownSb = new StringBuilder();

        foreach (var node in senseEl.Nodes())
        {
            if (node is XElement child && child.Name.LocalName is "sense" or "status" or "page")
                continue;
            AppendInlineNode(node, ownSb);
        }

        var own = CollapseSpaces(ownSb.ToString());
        if (!string.IsNullOrWhiteSpace(own))
        {
            var indent = new string(' ', depth * 2);
            var prefix = string.IsNullOrEmpty(num) ? "- " : $"- **{num}.** ";
            sb.AppendLine($"{indent}{prefix}{own}");
        }

        foreach (var childSense in senseEl.Elements().Where(c => c.Name.LocalName == "sense"))
            RenderSense(childSense, depth + 1, sb);
    }

    private static void AppendInlineNode(XNode node, StringBuilder sb)
    {
        switch (node)
        {
            case XText text:
                sb.Append(text.Value);
                break;
            case XElement el:
                switch (el.Name.LocalName)
                {
                    case "status":
                    case "page":
                        break;
                    case "def":
                        sb.Append("**").Append(el.Value.Trim()).Append("**");
                        break;
                    case "stem":
                        sb.Append("**[").Append(el.Value.Trim()).Append("]**");
                        break;
                    case "pos":
                    case "asp":
                    case "em":
                        sb.Append('*').Append(el.Value.Trim()).Append('*');
                        break;
                    case "w":
                    case "ref":
                    case "foreign":
                        sb.Append(el.Value);
                        break;
                    default:
                        foreach (var child in el.Nodes())
                            AppendInlineNode(child, sb);
                        break;
                }
                break;
        }
    }

    private static string CollapseSpaces(string s) =>
        MultiSpaceRegex().Replace(s.Replace('\r', ' ').Replace('\n', ' ').Replace('\u00A0', ' '), " ").Trim();
}
