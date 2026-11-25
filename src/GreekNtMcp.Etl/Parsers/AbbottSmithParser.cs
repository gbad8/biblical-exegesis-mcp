using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using GreekNtMcp.Domain.Models;
using GreekNtMcp.Domain.Normalization;

namespace GreekNtMcp.Etl.Parsers;

public static partial class AbbottSmithParser
{
    [GeneratedRegex(@"SYN\.?\s*[:.]?\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SynRegex();

    [GeneratedRegex(@"[ \t]{2,}", RegexOptions.Compiled)]
    private static partial Regex MultiSpaceRegex();

    public static List<AbbottSmithEntry> ParseFile(string xmlPath)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            IgnoreComments = true
        };

        using var reader = XmlReader.Create(xmlPath, settings);
        var doc = XDocument.Load(reader);
        return ParseDocument(doc);
    }

    public static List<AbbottSmithEntry> ParseDocument(XDocument doc)
    {
        var results = new List<AbbottSmithEntry>(5600);
        var entries = doc.Descendants().Where(e => e.Name.LocalName is "entry" or "entryFree");

        foreach (var el in entries)
        {
            var nAttr = el.Attribute("n")?.Value?.Trim() ?? string.Empty;
            string? strongRaw = null;
            string? headwordFromN = null;

            if (nAttr.Contains('|'))
            {
                var split = nAttr.Split('|', 2);
                headwordFromN = split[0].Trim();
                strongRaw = split[1].Trim();
            }
            else if (!string.IsNullOrWhiteSpace(nAttr))
            {
                headwordFromN = nAttr;
            }

            strongRaw ??= el.Attribute("strong")?.Value?.Trim();
            var strongParts = StrongCodeNormalizer.TryNormalize(strongRaw);

            var formEl = el.Elements().FirstOrDefault(c => c.Name.LocalName == "form");
            var orthEl = formEl?.Descendants().FirstOrDefault(c => c.Name.LocalName == "orth")
                         ?? el.Descendants().FirstOrDefault(c => c.Name.LocalName == "orth");

            var headword = (orthEl?.Value ?? headwordFromN ?? string.Empty).Trim().Normalize(NormalizationForm.FormC);
            if (string.IsNullOrWhiteSpace(headword))
                continue;

            var headwordNorm = GreekNormalizer.NormalizeGreek(FirstHeadword(headword));
            var gramInfo = formEl is not null ? CollapseSpaces(RenderInline(formEl)) : null;

            var etymEl = el.Elements().FirstOrDefault(c => c.Name.LocalName == "etym");
            var etym = etymEl is not null ? CollapseSpaces(RenderInline(etymEl)) : null;

            // Extract Hebrew/Aramaic LXX equivalents from <foreign xml:lang="heb"|"arc">
            var lxxList = new List<string>();
            foreach (var f in el.Descendants().Where(d => d.Name.LocalName == "foreign"))
            {
                var lang = f.Attributes().FirstOrDefault(a => a.Name.LocalName == "lang")?.Value?.ToLowerInvariant();
                if (lang is "heb" or "arc")
                {
                    var word = f.Value.Trim();
                    var strongRef = f.Attribute("n")?.Value?.Trim();
                    if (!string.IsNullOrEmpty(word))
                    {
                        var item = string.IsNullOrEmpty(strongRef) ? word : $"{word} ({strongRef})";
                        if (!lxxList.Contains(item))
                            lxxList.Add(item);
                    }
                }
            }

            // Extract SYN. synonyms
            var synonyms = new List<string>();
            foreach (var synNote in el.Descendants().Where(d =>
                         d.Name.LocalName == "re"
                         || (d.Name.LocalName == "note" && string.Equals(d.Attribute("type")?.Value, "synonyms", StringComparison.OrdinalIgnoreCase))))
            {
                foreach (var g in synNote.Descendants().Where(d => d.Name.LocalName == "foreign"))
                {
                    var w = g.Value.Trim();
                    if (!string.IsNullOrEmpty(w) && !synonyms.Contains(w))
                        synonyms.Add(w);
                }
            }

            var senses = new List<object>();
            var md = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(gramInfo))
                md.AppendLine(gramInfo);
            if (!string.IsNullOrWhiteSpace(etym))
                md.AppendLine($"*Etimologia:* {etym}");

            var topSenses = el.Elements().Where(c => c.Name.LocalName == "sense").ToList();
            if (topSenses.Count > 0)
            {
                md.AppendLine();
                foreach (var senseEl in topSenses)
                    RenderSense(senseEl, 0, md, senses, synonyms);
            }
            else
            {
                // Fallback if entry has inline text without explicit <sense>
                var body = CollapseSpaces(RenderInline(el));
                if (!string.IsNullOrWhiteSpace(body))
                    md.AppendLine(body);
            }

            // Also scan any trailing <re> / synonym notes
            foreach (var re in el.Elements().Where(c => c.Name.LocalName is "re" or "note"))
            {
                var noteType = re.Attribute("type")?.Value;
                if (string.Equals(noteType, "occurrencesNT", StringComparison.OrdinalIgnoreCase))
                    continue;
                var txt = CollapseSpaces(RenderInline(re));
                if (!string.IsNullOrWhiteSpace(txt))
                {
                    md.AppendLine();
                    md.AppendLine(txt);
                }
            }

            results.Add(new AbbottSmithEntry
            {
                StrongCode = strongParts?.BaseStrongCode,
                Headword = headword,
                HeadwordNormalized = headwordNorm,
                GrammaticalInfo = NullIfEmpty(gramInfo),
                Etymology = NullIfEmpty(etym),
                LxxEquivalents = lxxList.ToArray(),
                Synonyms = synonyms.ToArray(),
                SensesJson = senses.Count > 0 ? JsonSerializer.Serialize(senses) : null,
                FullMarkdown = md.ToString().Trim()
            });
        }

        return results;
    }

    private static void RenderSense(
        XElement senseEl,
        int depth,
        StringBuilder md,
        List<object> sensesOut,
        List<string> synonyms)
    {
        var num = senseEl.Attribute("n")?.Value?.Trim();
        var childSenses = senseEl.Elements().Where(c => c.Name.LocalName == "sense").ToList();

        var ownTextSb = new StringBuilder();
        foreach (var node in senseEl.Nodes())
        {
            if (node is XElement child && child.Name.LocalName == "sense")
                continue;
            AppendInlineNode(node, ownTextSb);
        }

        var ownText = CollapseSpaces(ownTextSb.ToString());
        if (!string.IsNullOrWhiteSpace(ownText))
        {
            // Check inline SYN. block
            var synMatch = SynRegex().Match(ownText);
            if (synMatch.Success)
            {
                foreach (var g in senseEl.Descendants().Where(d => d.Name.LocalName == "foreign"))
                {
                    var lang = g.Attributes().FirstOrDefault(a => a.Name.LocalName == "lang")?.Value;
                    if (lang == "grc" && synMatch.Groups[1].Value.Contains(g.Value, StringComparison.Ordinal))
                    {
                        var w = g.Value.Trim();
                        if (!string.IsNullOrEmpty(w) && !synonyms.Contains(w))
                            synonyms.Add(w);
                    }
                }
            }

            var indent = new string(' ', depth * 2);
            var prefix = string.IsNullOrEmpty(num) ? (depth > 0 ? "- " : "") : $"- **{num}** ";
            md.AppendLine($"{indent}{prefix}{ownText}");
            sensesOut.Add(new { level = depth, number = num, text = ownText });
        }

        foreach (var child in childSenses)
            RenderSense(child, depth + 1, md, sensesOut, synonyms);
    }

    private static string RenderInline(XElement container)
    {
        var sb = new StringBuilder();
        foreach (var node in container.Nodes())
            AppendInlineNode(node, sb);
        return sb.ToString();
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
                    case "orth":
                        sb.Append("**").Append(el.Value.Trim()).Append("**");
                        break;
                    case "gloss":
                        sb.Append("**").Append(RenderInline(el).Trim()).Append("**");
                        break;
                    case "emph":
                    case "hi":
                        sb.Append('*').Append(RenderInline(el).Trim()).Append('*');
                        break;
                    case "foreign":
                        sb.Append(el.Value);
                        break;
                    case "ref":
                        sb.Append(el.Value);
                        break;
                    case "pb":
                        break;
                    case "lb":
                        sb.Append(' ');
                        break;
                    default:
                        sb.Append(RenderInline(el));
                        break;
                }
                break;
        }
    }

    private static string FirstHeadword(string headword)
    {
        var idx = headword.IndexOfAny([',', ' ']);
        return idx > 0 ? headword[..idx].Trim() : headword;
    }

    private static string CollapseSpaces(string s) =>
        MultiSpaceRegex().Replace(s.Replace('\r', ' ').Replace('\n', ' '), " ").Trim();

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
