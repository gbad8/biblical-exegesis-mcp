using System.Text;
using System.Text.RegularExpressions;
using BiblicalExegesisMcp.Domain.Models;
using BiblicalExegesisMcp.Domain.Morphology;
using BiblicalExegesisMcp.Domain.Normalization;
using BiblicalExegesisMcp.Domain.References;

namespace BiblicalExegesisMcp.Etl.Parsers;

public static partial class TahotParser
{
    // Matches e.g. "Gen.1.1#01=L", "Psa.51.1(51.3)#01=L", "Psa.22.16(17)#05=L(d)", "Gen.27.3#06=Q"
    [GeneratedRegex(@"^(?<book>[1-3]?[A-Za-z]{2,4})\.(?<ch>\d+)\.(?<v>\d+)(?:\((?<hebref>[^)]+)\))?#(?<pos>\d+)=(?<src>[^\t\s]+)$", RegexOptions.Compiled)]
    private static partial Regex TahotIdRegex();

    /// <summary>
    /// Streams tokens from a STEPBible TAHOT TSV file without loading the entire file into memory
    /// (critical for staying well within 1 GB RAM limits on Oracle Cloud E2.1.Micro).
    /// </summary>
    public static IEnumerable<OtToken> StreamFile(string filePath)
    {
        using var reader = new StreamReader(filePath, Encoding.UTF8);
        var subPosTracker = new Dictionary<(short BookId, short Chapter, short Verse, short WordPos), short>();

        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line) || line[0] == '#' || line[0] == '=' || line[0] == '\t')
                continue;

            var firstTab = line.IndexOf('\t');
            if (firstTab <= 0)
                continue;

            var idCol = line[..firstTab].Trim();
            var idMatch = TahotIdRegex().Match(idCol);
            if (!idMatch.Success)
                continue;

            var book = BibleBookCatalog.FindByOsisOrUsfm(idMatch.Groups["book"].Value);
            if (book is null || !book.IsOldTestament)
                continue;

            var chapter = short.Parse(idMatch.Groups["ch"].Value);
            var verse = short.Parse(idMatch.Groups["v"].Value);
            var wordPos = short.Parse(idMatch.Groups["pos"].Value);
            var sourceType = idMatch.Groups["src"].Value.Trim();

            string? hebrewRef = null;
            if (idMatch.Groups["hebref"].Success)
            {
                var rawHeb = idMatch.Groups["hebref"].Value.Trim();
                hebrewRef = rawHeb.Contains('.') ? $"{book.OsisCode}.{rawHeb}" : $"{book.OsisCode}.{chapter}.{rawHeb}";
            }

            var key = (book.BookId, chapter, verse, wordPos);
            subPosTracker.TryGetValue(key, out var subPos);
            subPosTracker[key] = (short)(subPos + 1);

            var cols = line.Split('\t');
            if (cols.Length < 6)
                continue;

            var (surfaceForm, segmentedForm, puncAfter) = HebrewNormalizer.CleanTahotSurface(cols[1]);
            if (string.IsNullOrEmpty(surfaceForm))
                continue;

            var translit = NullIfEmpty(cols[2].Trim());
            var gloss = NullIfEmpty(cols[3].Trim());
            var dStrongsCol = cols[4].Trim();
            var morphCol = NullIfEmpty(cols[5].Trim());
            var meaningVar = cols.Length > 6 ? cols[6].Trim() : string.Empty;
            var spellingVar = cols.Length > 7 ? cols[7].Trim() : string.Empty;
            var rootCol = cols.Length > 8 ? cols[8].Trim() : null;
            var expandedCol = cols.Length > 11 ? cols[11].Trim() : null;

            var (primaryStrong, affixes, lemma) = StrongCodeNormalizer.ParseTahotStrongField(
                dStrongsCol, rootCol, expandedCol);

            var lang = OshmMorphDecoder.IsAramaic(morphCol) ? "arc" : "heb";

            // Classify MT (Leningrad/Qere/Restored) vs Ketiv vs LXX-only ('X')
            var primarySourceChar = char.ToUpperInvariant(sourceType[0]);
            bool inMt = primarySourceChar is 'L' or 'Q' or 'R';
            bool inKetiv = primarySourceChar is 'L' or 'K' or 'R';

            var variantNote = BuildVariantNote(sourceType, meaningVar, spellingVar);

            yield return new OtToken
            {
                BookId = book.BookId,
                Chapter = chapter,
                Verse = verse,
                HebrewRef = hebrewRef,
                WordPosition = wordPos,
                SubPosition = subPos,
                SurfaceForm = surfaceForm,
                SegmentedForm = segmentedForm,
                SurfaceNormalized = HebrewNormalizer.NormalizeHebrew(surfaceForm),
                PunctuationAfter = puncAfter,
                Transliteration = translit,
                ContextualGloss = gloss,
                StrongCode = primaryStrong?.StrongCode,
                BaseStrongCode = primaryStrong?.BaseStrongCode,
                PrefixSuffixStrongs = affixes,
                Lemma = lemma,
                LemmaNormalized = HebrewNormalizer.NormalizeHebrew(lemma),
                MorphCode = morphCol,
                Language = lang,
                SourceType = sourceType,
                InMt = inMt,
                InKetiv = inKetiv,
                VariantNote = variantNote
            };
        }
    }

    private static string? BuildVariantNote(string sourceType, string meaningVar, string spellingVar)
    {
        var notes = new List<string>();
        var primary = char.ToUpperInvariant(sourceType[0]);
        if (primary == 'Q') notes.Add("Qere (leitura massorética)");
        else if (primary == 'K') notes.Add("Ketiv (texto consonantal)");
        else if (primary == 'X') notes.Add("Acréscimo exclusivo da LXX");

        if (!string.IsNullOrWhiteSpace(meaningVar))
            notes.Add(meaningVar);
        if (!string.IsNullOrWhiteSpace(spellingVar))
            notes.Add($"Ortografia: {spellingVar}");

        return notes.Count == 0 ? null : string.Join(" | ", notes);
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
