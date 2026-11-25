using System.Text;
using BiblicalExegesisMcp.Domain.Normalization;

namespace BiblicalExegesisMcp.Etl.Parsers;

public sealed record TbeshRow(
    string StrongCode,
    string BaseStrongCode,
    int? StrongNumber,
    bool IsExtended,
    string Language,
    string Lemma,
    string LemmaNormalized,
    string? Transliteration,
    string? TransliterationNormalized,
    string? PosCode,
    string? ShortGloss);

/// <summary>
/// Parses STEPBible TBESH extracting ONLY the STEPBible CC BY 4.0 fields
/// (eStrong#, dStrong, Hebrew lemma, Transliteration, Morph, Short Gloss).
/// Deliberately excludes column 7 (Meaning) which carries a third-party Online Bible copyright notice;
/// full Hebrew lexical definitions are sourced from open-licensed openscriptures/HebrewLexicon (Strong + BDB).
/// </summary>
public static class TbeshParser
{
    public static List<TbeshRow> ParseFile(string filePath)
    {
        var rows = new List<TbeshRow>(14000);
        using var reader = new StreamReader(filePath, Encoding.UTF8);

        bool inData = false;
        while (reader.ReadLine() is { } line)
        {
            if (!inData)
            {
                if (line.StartsWith("eStrong", StringComparison.OrdinalIgnoreCase) &&
                    line.Contains("\tdStrong", StringComparison.OrdinalIgnoreCase))
                {
                    inData = true;
                }
                continue;
            }

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("==="))
                continue;

            var cols = line.Split('\t');
            if (cols.Length < 7)
                continue;

            var eStrongRaw = cols[0].Trim();
            if (!eStrongRaw.StartsWith('H') && !eStrongRaw.StartsWith('h'))
                continue;

            var dStrongToken = ExtractFirstHebrewStrongToken(cols[1]) ?? eStrongRaw;
            var parts = StrongCodeNormalizer.TryNormalize(dStrongToken, 'H')
                        ?? StrongCodeNormalizer.TryNormalize(eStrongRaw, 'H');
            if (parts is null)
                continue;

            var lemmaRaw = cols[3].Trim().Normalize(NormalizationForm.FormC);
            var primaryLemma = FirstHeadword(lemmaRaw);
            var translit = NullIfEmpty(cols[4].Trim());
            var pos = NullIfEmpty(cols[5].Trim());
            var gloss = NullIfEmpty(cols[6].Trim());
            var lang = pos is not null && pos.StartsWith("A:", StringComparison.OrdinalIgnoreCase) ? "arc" : "heb";

            rows.Add(new TbeshRow(
                StrongCode: parts.StrongCode,
                BaseStrongCode: parts.BaseStrongCode,
                StrongNumber: parts.StrongNumber,
                IsExtended: parts.IsExtended,
                Language: lang,
                Lemma: lemmaRaw,
                LemmaNormalized: HebrewNormalizer.NormalizeHebrew(primaryLemma),
                Transliteration: translit,
                TransliterationNormalized: GreekNormalizer.NormalizeLatin(translit),
                PosCode: pos,
                ShortGloss: gloss));
        }

        return rows;
    }

    private static string? ExtractFirstHebrewStrongToken(string? field)
    {
        if (string.IsNullOrWhiteSpace(field))
            return null;

        var trimmed = field.Trim();
        var stopIdx = trimmed.IndexOfAny([' ', '=', '\t']);
        var token = stopIdx > 0 ? trimmed[..stopIdx] : trimmed;
        return token.StartsWith('H') || token.StartsWith('h') ? token : null;
    }

    private static string FirstHeadword(string lemma)
    {
        var commaIdx = lemma.IndexOf(',');
        return commaIdx > 0 ? lemma[..commaIdx].Trim() : lemma;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
