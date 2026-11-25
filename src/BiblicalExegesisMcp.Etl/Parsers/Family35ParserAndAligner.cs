using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using BiblicalExegesisMcp.Domain.Models;
using BiblicalExegesisMcp.Domain.Normalization;
using BiblicalExegesisMcp.Domain.References;

namespace BiblicalExegesisMcp.Etl.Parsers;

public sealed record Family35AlignmentStats(
    int TotalVerses,
    int TotalTokens,
    int ExactVerseMatches,
    int UnambiguousFormMatches,
    int UntaggedVariants);

public static class Family35ParserAndAligner
{
    public static (List<F35Verse> Verses, List<F35Token> Tokens, Family35AlignmentStats Stats) ParseAndAlignFromZip(
        string zipPath,
        IReadOnlyList<NtToken> tagntTokens)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var xmlEntry = archive.Entries.FirstOrDefault(e =>
                           e.Name.Equals("grcf35_vpl.xml", StringComparison.OrdinalIgnoreCase))
                       ?? archive.Entries.FirstOrDefault(e =>
                           e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && !e.Name.Contains("metadata", StringComparison.OrdinalIgnoreCase))
                       ?? throw new InvalidOperationException($"grcf35_vpl.xml não encontrado dentro de {zipPath}");

        using var stream = xmlEntry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
        var doc = XDocument.Load(reader);
        return ParseAndAlignDocument(doc, tagntTokens);
    }

    public static (List<F35Verse> Verses, List<F35Token> Tokens, Family35AlignmentStats Stats) ParseAndAlignDocument(
        XDocument doc,
        IReadOnlyList<NtToken> tagntTokens)
    {
        // Build lookup of TAGNT verse tokens (prioritizing Byz, then TR, then all TAGNT for that verse)
        var byzByVerse = tagntTokens
            .Where(t => t.InByz)
            .GroupBy(t => (t.BookId, t.Chapter, t.Verse))
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.WordPosition).ThenBy(t => t.SubPosition).ToList());

        var trByVerse = tagntTokens
            .Where(t => t.InTr)
            .GroupBy(t => (t.BookId, t.Chapter, t.Verse))
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.WordPosition).ThenBy(t => t.SubPosition).ToList());

        var allByVerse = tagntTokens
            .GroupBy(t => (t.BookId, t.Chapter, t.Verse))
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.WordPosition).ThenBy(t => t.SubPosition).ToList());

        // Build Level-2 conservative unambiguous surface form lookup across the entire NT corpus:
        // Only surface_form (exact polytonic spelling) with a single unique (BaseStrongCode, MorphCode) qualifies!
        var unambiguousFormMap = BuildUnambiguousFormMap(tagntTokens);

        var verses = new List<F35Verse>(8000);
        var f35Tokens = new List<F35Token>(142000);

        int exactMatches = 0;
        int formMatches = 0;
        int untaggedCount = 0;

        foreach (var vEl in doc.Descendants().Where(e => e.Name.LocalName == "v"))
        {
            var bookCode = vEl.Attribute("b")?.Value?.Trim();
            var chRaw = vEl.Attribute("c")?.Value?.Trim();
            var vRaw = vEl.Attribute("v")?.Value?.Trim();
            var verseText = vEl.Value.Trim().Normalize(NormalizationForm.FormC);

            if (string.IsNullOrEmpty(bookCode) || string.IsNullOrEmpty(verseText))
                continue;

            var book = BibleBookCatalog.FindByOsisOrUsfm(bookCode);
            if (book is null)
                continue;

            if (!short.TryParse(chRaw, out var chapter) || !short.TryParse(vRaw, out var verse))
                continue;

            verses.Add(new F35Verse
            {
                BookId = book.BookId,
                Chapter = chapter,
                Verse = verse,
                VerseText = verseText
            });

            var rawWords = verseText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var key = (book.BookId, chapter, verse);

            // Candidate pools for Level-1 sequential verse alignment (Byz first, then TR, then any TAGNT variant in verse)
            var primaryPool = byzByVerse.GetValueOrDefault(key) ?? [];
            var secondaryPool = trByVerse.GetValueOrDefault(key) ?? [];
            var tertiaryPool = allByVerse.GetValueOrDefault(key) ?? [];

            var usedPrimary = new bool[primaryPool.Count];
            var usedSecondary = new bool[secondaryPool.Count];
            var usedTertiary = new bool[tertiaryPool.Count];

            int primaryCursor = 0;
            short wordPos = 0;

            foreach (var rawWord in rawWords)
            {
                var (puncBefore, core, puncAfter) = GreekNormalizer.SplitPunctuation(rawWord);
                if (string.IsNullOrWhiteSpace(core) || !GreekNormalizer.ContainsGreek(core))
                    continue;

                wordPos++;
                var norm = GreekNormalizer.NormalizeGreek(core);

                NtToken? matched = TryMatchInPool(primaryPool, usedPrimary, norm, ref primaryCursor)
                                   ?? TryMatchAnywhere(secondaryPool, usedSecondary, norm)
                                   ?? TryMatchAnywhere(tertiaryPool, usedTertiary, norm);

                string alignmentStatus;
                if (matched is not null)
                {
                    alignmentStatus = "exact_verse_match";
                    exactMatches++;
                }
                else if (unambiguousFormMap.TryGetValue(core, out var uniqueToken))
                {
                    matched = uniqueToken;
                    alignmentStatus = "unambiguous_form_match";
                    formMatches++;
                }
                else
                {
                    alignmentStatus = "untagged_f35_variant";
                    untaggedCount++;
                }

                f35Tokens.Add(new F35Token
                {
                    BookId = book.BookId,
                    Chapter = chapter,
                    Verse = verse,
                    WordPosition = wordPos,
                    SurfaceForm = core,
                    SurfaceNormalized = norm,
                    PunctuationBefore = puncBefore,
                    PunctuationAfter = puncAfter,
                    Transliteration = matched?.Transliteration,
                    ContextualGloss = alignmentStatus == "exact_verse_match" ? matched?.ContextualGloss : null,
                    StrongCode = matched?.StrongCode,
                    BaseStrongCode = matched?.BaseStrongCode,
                    SecondaryStrongCodes = matched?.SecondaryStrongCodes ?? [],
                    Lemma = matched?.Lemma,
                    LemmaNormalized = matched?.LemmaNormalized,
                    MorphCode = matched?.MorphCode,
                    AlignmentStatus = alignmentStatus
                });
            }
        }

        var stats = new Family35AlignmentStats(
            verses.Count,
            f35Tokens.Count,
            exactMatches,
            formMatches,
            untaggedCount);

        return (verses, f35Tokens, stats);
    }

    private static NtToken? TryMatchInPool(List<NtToken> pool, bool[] used, string norm, ref int cursor)
    {
        // Search in a local window ahead of cursor first to preserve sequential alignment on repeated words (e.g. καί, τόν)
        int windowEnd = Math.Min(pool.Count, cursor + 6);
        for (int i = cursor; i < windowEnd; i++)
        {
            if (!used[i] && IsEquivalentGreek(pool[i].SurfaceNormalized, norm))
            {
                used[i] = true;
                cursor = i + 1;
                return pool[i];
            }
        }

        // Fallback: search anywhere unused in the verse
        for (int i = 0; i < pool.Count; i++)
        {
            if (!used[i] && IsEquivalentGreek(pool[i].SurfaceNormalized, norm))
            {
                used[i] = true;
                return pool[i];
            }
        }

        return null;
    }

    private static NtToken? TryMatchAnywhere(List<NtToken> pool, bool[] used, string norm)
    {
        for (int i = 0; i < pool.Count; i++)
        {
            if (!used[i] && IsEquivalentGreek(pool[i].SurfaceNormalized, norm))
            {
                used[i] = true;
                return pool[i];
            }
        }
        return null;
    }

    /// <summary>
    /// Exact normalized Greek match, plus standard Koine movable-nu (ν-ephelkystikon) equivalence
    /// (e.g. ἐγέννησεν vs ἐγέννησε, εἶπεν vs εἶπε) which never alters lemma or morphology.
    /// </summary>
    private static bool IsEquivalentGreek(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.Ordinal))
            return true;

        // Movable-nu equivalence (e.g. ειπεν / ειπε, εστιν / εστι, πασιν / πασι)
        if (a.Length == b.Length + 1 && a.EndsWith('ν') && a.AsSpan(0, b.Length).SequenceEqual(b))
            return true;
        if (b.Length == a.Length + 1 && b.EndsWith('ν') && b.AsSpan(0, a.Length).SequenceEqual(a))
            return true;

        return false;
    }

    private static Dictionary<string, NtToken> BuildUnambiguousFormMap(IReadOnlyList<NtToken> tagntTokens)
    {
        var grouped = new Dictionary<string, (NtToken First, bool Ambiguous)>(StringComparer.Ordinal);

        foreach (var tok in tagntTokens)
        {
            if (string.IsNullOrEmpty(tok.SurfaceForm) || string.IsNullOrEmpty(tok.BaseStrongCode) || string.IsNullOrEmpty(tok.MorphCode))
                continue;

            if (!grouped.TryGetValue(tok.SurfaceForm, out var existing))
            {
                grouped[tok.SurfaceForm] = (tok, false);
            }
            else if (!existing.Ambiguous)
            {
                if (existing.First.BaseStrongCode != tok.BaseStrongCode || existing.First.MorphCode != tok.MorphCode)
                    grouped[tok.SurfaceForm] = (existing.First, true);
            }
        }

        var result = new Dictionary<string, NtToken>(StringComparer.Ordinal);
        foreach (var (form, (tok, ambiguous)) in grouped)
        {
            if (!ambiguous)
                result[form] = tok;
        }
        return result;
    }
}
