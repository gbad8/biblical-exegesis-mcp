using System.Text;
using BiblicalExegesisMcp.Domain.Models;
using BiblicalExegesisMcp.Domain.Normalization;

namespace BiblicalExegesisMcp.Etl.Parsers;

public sealed record TbesgRow(
    string StrongCode,
    string BaseStrongCode,
    int? StrongNumber,
    bool IsExtended,
    string Lemma,
    string LemmaNormalized,
    string? Transliteration,
    string? TransliterationNormalized,
    string? PosCode,
    string? ShortGloss,
    string? MeaningMarkdown);

public static class StepBibleLexiconParser
{
    public static List<TbesgRow> ParseTbesg(string filePath)
    {
        var rows = new List<TbesgRow>(10000);
        using var reader = new StreamReader(filePath, Encoding.UTF8);

        bool inData = false;
        while (reader.ReadLine() is { } line)
        {
            if (!inData)
            {
                if (line.StartsWith("eStrong\tdStrong", StringComparison.OrdinalIgnoreCase))
                    inData = true;
                continue;
            }

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("==="))
                continue;

            var cols = line.Split('\t');
            if (cols.Length < 7)
                continue;

            var eStrongRaw = cols[0].Trim();
            if (!eStrongRaw.StartsWith('G') && !eStrongRaw.StartsWith('g'))
                continue;

            var dStrongToken = ExtractFirstStrongToken(cols[1]) ?? eStrongRaw;
            var parts = StrongCodeNormalizer.TryNormalize(dStrongToken)
                        ?? StrongCodeNormalizer.TryNormalize(eStrongRaw);
            if (parts is null)
                continue;

            var lemmaRaw = cols[3].Trim().Normalize(NormalizationForm.FormC);
            var primaryLemma = FirstHeadword(lemmaRaw);
            var translit = NullIfEmpty(cols[4].Trim());
            var pos = NullIfEmpty(cols[5].Trim());
            var gloss = NullIfEmpty(cols[6].Trim());
            var meaningHtml = cols.Length >= 8 ? cols[7] : null;
            var meaningMd = NullIfEmpty(HtmlToMarkdownConverter.Convert(meaningHtml, includeAnchorTitles: false));

            rows.Add(new TbesgRow(
                StrongCode: parts.StrongCode,
                BaseStrongCode: parts.BaseStrongCode,
                StrongNumber: parts.StrongNumber,
                IsExtended: parts.IsExtended,
                Lemma: lemmaRaw,
                LemmaNormalized: GreekNormalizer.NormalizeGreek(primaryLemma),
                Transliteration: translit,
                TransliterationNormalized: GreekNormalizer.NormalizeLatin(translit),
                PosCode: pos,
                ShortGloss: gloss,
                MeaningMarkdown: meaningMd));
        }

        return rows;
    }

    public static List<LsjEntry> ParseTflsj(string filePath)
    {
        var entries = new List<LsjEntry>(6500);
        using var reader = new StreamReader(filePath, Encoding.UTF8);

        bool inData = false;
        while (reader.ReadLine() is { } line)
        {
            if (!inData)
            {
                if (line.StartsWith("eStrong\tdStrong", StringComparison.OrdinalIgnoreCase))
                    inData = true;
                continue;
            }

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("==="))
                continue;

            var cols = line.Split('\t');
            if (cols.Length < 8)
                continue;

            var eStrongRaw = cols[0].Trim();
            if (!eStrongRaw.StartsWith('G') && !eStrongRaw.StartsWith('g'))
                continue;

            var dStrongToken = ExtractFirstStrongToken(cols[1]) ?? eStrongRaw;
            var parts = StrongCodeNormalizer.TryNormalize(dStrongToken)
                        ?? StrongCodeNormalizer.TryNormalize(eStrongRaw);
            if (parts is null)
                continue;

            var headword = cols[3].Trim().Normalize(NormalizationForm.FormC);
            var meaningHtml = cols[7];
            var fullMd = HtmlToMarkdownConverter.Convert(meaningHtml, includeAnchorTitles: true);
            if (string.IsNullOrWhiteSpace(fullMd))
                continue;

            var summaryMd = HtmlToMarkdownConverter.BuildSummary(
                HtmlToMarkdownConverter.Convert(meaningHtml, includeAnchorTitles: false),
                maxChars: 750);

            entries.Add(new LsjEntry
            {
                StrongCode = parts.StrongCode,
                BaseStrongCode = parts.BaseStrongCode,
                Headword = headword,
                HeadwordNormalized = GreekNormalizer.NormalizeGreek(FirstHeadword(headword)),
                SummaryMarkdown = summaryMd,
                FullMarkdown = fullMd,
                CharLength = fullMd.Length
            });
        }

        return entries;
    }

    private static string? ExtractFirstStrongToken(string? field)
    {
        if (string.IsNullOrWhiteSpace(field))
            return null;

        var trimmed = field.Trim();
        var stopIdx = trimmed.IndexOfAny([' ', '=', '\t']);
        var token = stopIdx > 0 ? trimmed[..stopIdx] : trimmed;
        return token.StartsWith('G') || token.StartsWith('g') ? token : null;
    }

    private static string FirstHeadword(string lemma)
    {
        var commaIdx = lemma.IndexOf(',');
        return commaIdx > 0 ? lemma[..commaIdx].Trim() : lemma;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
