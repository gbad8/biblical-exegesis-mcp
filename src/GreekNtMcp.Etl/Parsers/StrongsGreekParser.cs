using System.Text;
using System.Xml;
using System.Xml.Linq;
using GreekNtMcp.Domain.Models;
using GreekNtMcp.Domain.Normalization;

namespace GreekNtMcp.Etl.Parsers;

public static class StrongsGreekParser
{
    public static List<LexiconEntry> ParseFile(string xmlPath)
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

    public static List<LexiconEntry> ParseDocument(XDocument doc)
    {
        var entries = new List<LexiconEntry>(5700);
        var entryElements = doc.Descendants().Where(e => e.Name.LocalName == "entry");

        foreach (var el in entryElements)
        {
            var rawStrong = el.Attribute("strongs")?.Value ?? el.Element("strongs")?.Value;
            var parts = StrongCodeNormalizer.TryNormalize(rawStrong);
            if (parts is null)
                continue;

            var greekEl = el.Elements().FirstOrDefault(e => e.Name.LocalName == "greek");
            var lemma = (greekEl?.Attribute("unicode")?.Value ?? string.Empty).Trim().Normalize(NormalizationForm.FormC);
            var translit = greekEl?.Attribute("translit")?.Value?.Trim();

            var derivationEl = el.Elements().FirstOrDefault(e => e.Name.LocalName == "strongs_derivation");
            var defEl = el.Elements().FirstOrDefault(e => e.Name.LocalName == "strongs_def");
            var kjvEl = el.Elements().FirstOrDefault(e => e.Name.LocalName == "kjv_def");

            var derivation = RenderInlineContent(derivationEl);
            var definition = RenderInlineContent(defEl);
            var kjvDef = CleanKjvDef(RenderInlineContent(kjvEl));

            // Capture any trailing explanatory notes placed directly under <entry> after <kjv_def>
            if (kjvEl is not null)
            {
                var trailing = RenderTrailingNodes(kjvEl);
                if (!string.IsNullOrWhiteSpace(trailing))
                {
                    definition = string.IsNullOrWhiteSpace(definition)
                        ? trailing
                        : $"{definition}\n\n{trailing}";
                }
            }

            entries.Add(new LexiconEntry
            {
                StrongCode = parts.StrongCode,
                BaseStrongCode = parts.BaseStrongCode,
                StrongNumber = parts.StrongNumber,
                IsExtended = parts.IsExtended,
                Lemma = lemma,
                LemmaNormalized = GreekNormalizer.NormalizeGreek(lemma),
                Transliteration = translit,
                TransliterationNormalized = GreekNormalizer.NormalizeLatin(translit),
                StrongDerivation = NullIfEmpty(derivation),
                StrongDefinition = NullIfEmpty(definition),
                StrongKjvDefinition = NullIfEmpty(kjvDef)
            });
        }

        return entries;
    }

    private static string RenderInlineContent(XElement? container)
    {
        if (container is null)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var node in container.Nodes())
            AppendNode(node, sb);

        return CollapseWhitespace(sb.ToString());
    }

    private static string RenderTrailingNodes(XElement afterElement)
    {
        var sb = new StringBuilder();
        var next = afterElement.NextNode;
        while (next is not null)
        {
            AppendNode(next, sb);
            next = next.NextNode;
        }
        return CollapseWhitespace(sb.ToString());
    }

    private static void AppendNode(XNode node, StringBuilder sb)
    {
        switch (node)
        {
            case XText text:
                sb.Append(text.Value);
                break;
            case XElement el:
                switch (el.Name.LocalName)
                {
                    case "strongsref":
                        var lang = el.Attribute("language")?.Value?.ToUpperInvariant();
                        var num = el.Attribute("strongs")?.Value?.Trim();
                        if (!string.IsNullOrEmpty(num))
                        {
                            var prefix = lang == "HEBREW" ? "H" : "G";
                            if (int.TryParse(num, out var n))
                                sb.Append($"[{prefix}{n:D4}]");
                            else
                                sb.Append($"[{prefix}{num}]");
                        }
                        break;
                    case "greek":
                        var u = el.Attribute("unicode")?.Value;
                        var tr = el.Attribute("translit")?.Value;
                        if (!string.IsNullOrEmpty(u))
                            sb.Append(string.IsNullOrEmpty(tr) ? u : $"{u} ({tr})");
                        break;
                    case "pronunciation":
                        var pron = el.Attribute("strongs")?.Value;
                        if (!string.IsNullOrEmpty(pron))
                            sb.Append($"/{pron}/");
                        break;
                    default:
                        sb.Append(el.Value);
                        break;
                }
                break;
        }
    }

    private static string CleanKjvDef(string raw) =>
        raw.TrimStart(':', '-', ' ').Trim();

    private static string CollapseWhitespace(string s) =>
        string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
