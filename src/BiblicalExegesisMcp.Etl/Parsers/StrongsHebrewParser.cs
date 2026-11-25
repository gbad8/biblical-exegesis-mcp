using System.Text;
using System.Xml;
using System.Xml.Linq;
using BiblicalExegesisMcp.Domain.Models;
using BiblicalExegesisMcp.Domain.Normalization;

namespace BiblicalExegesisMcp.Etl.Parsers;

public static class StrongsHebrewParser
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
        var entries = new List<LexiconEntry>(8700);

        foreach (var el in doc.Descendants().Where(e => e.Name.LocalName == "entry"))
        {
            var rawId = el.Attribute("id")?.Value?.Trim();
            var parts = StrongCodeNormalizer.TryNormalize(rawId, 'H');
            if (parts is null)
                continue;

            var wEl = el.Elements().FirstOrDefault(c => c.Name.LocalName == "w");
            var lemma = (wEl?.Value ?? string.Empty).Trim().Normalize(NormalizationForm.FormC);
            var translit = wEl?.Attribute("xlit")?.Value?.Trim();
            var pos = wEl?.Attribute("pos")?.Value?.Trim();
            var xmlLang = wEl?.Attributes().FirstOrDefault(a => a.Name.LocalName == "lang")?.Value?.ToLowerInvariant();
            var lang = xmlLang == "arc" ? "arc" : "heb";

            var sourceEl = el.Elements().FirstOrDefault(c => c.Name.LocalName == "source");
            var meaningEl = el.Elements().FirstOrDefault(c => c.Name.LocalName == "meaning");
            var usageEl = el.Elements().FirstOrDefault(c => c.Name.LocalName == "usage");

            var derivation = RenderInline(sourceEl);
            var definition = RenderInline(meaningEl);
            var usage = RenderInline(usageEl);

            entries.Add(new LexiconEntry
            {
                StrongCode = parts.StrongCode,
                BaseStrongCode = parts.BaseStrongCode,
                StrongNumber = parts.StrongNumber,
                IsExtended = parts.IsExtended,
                Language = lang,
                Lemma = lemma,
                LemmaNormalized = HebrewNormalizer.NormalizeHebrew(lemma),
                Transliteration = translit,
                TransliterationNormalized = GreekNormalizer.NormalizeLatin(translit),
                PosCode = NullIfEmpty(pos),
                StrongDerivation = NullIfEmpty(derivation),
                StrongDefinition = NullIfEmpty(definition),
                StrongKjvDefinition = NullIfEmpty(usage)
            });
        }

        return entries;
    }

    private static string RenderInline(XElement? container)
    {
        if (container is null)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var node in container.Nodes())
            AppendNode(node, sb);

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
                    case "w":
                        var src = el.Attribute("src")?.Value?.Trim();
                        if (!string.IsNullOrEmpty(src))
                        {
                            var norm = StrongCodeNormalizer.TryNormalize(src, 'H');
                            sb.Append(norm is not null ? $"[{norm.StrongCode}]" : $"[{src}]");
                        }
                        else
                        {
                            sb.Append(el.Value);
                        }
                        break;
                    case "def":
                        sb.Append("**").Append(el.Value.Trim()).Append("**");
                        break;
                    default:
                        sb.Append(RenderInline(el));
                        break;
                }
                break;
        }
    }

    private static string CollapseWhitespace(string s) =>
        string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
