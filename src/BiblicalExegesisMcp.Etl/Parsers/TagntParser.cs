using System.Text;
using System.Text.RegularExpressions;
using BiblicalExegesisMcp.Domain.Models;
using BiblicalExegesisMcp.Domain.Normalization;
using BiblicalExegesisMcp.Domain.References;

namespace BiblicalExegesisMcp.Etl.Parsers;

public static partial class TagntParser
{
    // Matches e.g. "Mat.1.1#01=NKO", "Mat.17.14[17.15]#01=NKO", "1Co.13.4#05=N(K)O", "Jhn.7.53#01=K(O)"
    [GeneratedRegex(@"^(?<book>[1-3]?[A-Za-z]{2,4})\.(?<ch>\d+)\.(?<v>\d+)(?:[\[\(\{][^#\t\s]*[\]\)\}])?#(?<pos>\d+)=(?<grp>[^\t\s]+)$", RegexOptions.Compiled)]
    private static partial Regex TokenIdRegex();

    public static List<NtToken> ParseFile(string filePath)
    {
        var tokens = new List<NtToken>(90000);
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
            var idMatch = TokenIdRegex().Match(idCol);
            if (!idMatch.Success)
                continue;

            var book = BibleBookCatalog.FindByOsisOrUsfm(idMatch.Groups["book"].Value);
            if (book is null)
                continue;

            var chapter = short.Parse(idMatch.Groups["ch"].Value);
            var verse = short.Parse(idMatch.Groups["v"].Value);
            var wordPos = short.Parse(idMatch.Groups["pos"].Value);
            var editionGroup = idMatch.Groups["grp"].Value.Trim();

            var key = (book.BookId, chapter, verse, wordPos);
            subPosTracker.TryGetValue(key, out var subPos);
            subPosTracker[key] = (short)(subPos + 1);

            var cols = line.Split('\t');
            if (cols.Length < 6)
                continue;

            // Col 1: Greek (Transliteration), e.g. "Βίβλος (Biblos)" or "Ἀβραάμ. (Abraam)"
            var (rawGreek, translit) = SplitGreekAndTranslit(cols[1]);
            var (puncBefore, surfaceCore, puncAfter) = GreekNormalizer.SplitPunctuation(rawGreek);
            if (string.IsNullOrEmpty(surfaceCore))
                surfaceCore = rawGreek.Trim().Normalize(NormalizationForm.FormC);

            // Col 2: English contextual translation
            var gloss = NullIfEmpty(cols[2].Trim());

            // Col 3: dStrongs = Grammar (e.g. "G0976=N-NSF", "G2532=CONJ + G1473=P-1NS")
            var (primaryStrong, secondaryStrongs, morphCode) = StrongCodeNormalizer.ParseTagntGrammarField(cols[3]);

            // Fallback to Col 11 (sStrong+Instance e.g. G0976 or G5207_A) if Col 3 lacked a Greek Strong
            if (primaryStrong is null && cols.Length > 11)
            {
                var sStrongRaw = cols[11].Trim();
                var underIdx = sStrongRaw.IndexOf('_');
                if (underIdx > 0) sStrongRaw = sStrongRaw[..underIdx];
                primaryStrong = StrongCodeNormalizer.TryNormalize(sStrongRaw);
            }

            // Include Alt Strongs from Col 12 if present (e.g. crasis G2504 vs G2532+G1473)
            var secList = new List<string>(secondaryStrongs);
            if (cols.Length > 12 && !string.IsNullOrWhiteSpace(cols[12]))
            {
                foreach (var altSeg in cols[12].Split(['+', ',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
                {
                    var cleanAlt = altSeg.Split('_')[0].Trim('{', '}', '[', ']', '(', ')');
                    var altParsed = StrongCodeNormalizer.TryNormalize(cleanAlt);
                    if (altParsed is not null && altParsed.BaseStrongCode != primaryStrong?.BaseStrongCode)
                    {
                        if (!secList.Contains(altParsed.BaseStrongCode))
                            secList.Add(altParsed.BaseStrongCode);
                    }
                }
            }

            // Col 4: Dictionary form = Gloss (e.g. "βίβλος=book", "Δαυείδ, Δαυίδ, Δαβίδ=David")
            var lemma = ExtractLemma(cols[4]);

            // Col 5: editions (e.g. "NA28+NA27+Tyn+SBL+WH+Treg+TR+Byz")
            var editionsRaw = cols[5].Trim();
            var (inTr, inByz, inNa28, inSbl) = ParseEditionFlags(editionsRaw, editionGroup);

            // Col 6 (Meaning variants) & Col 7 (Spelling variants)
            var meaningVar = cols.Length > 6 ? cols[6].Trim() : string.Empty;
            var spellingVar = cols.Length > 7 ? cols[7].Trim() : string.Empty;
            var variantNote = BuildVariantNote(meaningVar, spellingVar);

            tokens.Add(new NtToken
            {
                BookId = book.BookId,
                Chapter = chapter,
                Verse = verse,
                WordPosition = wordPos,
                SubPosition = subPos,
                SurfaceForm = surfaceCore,
                SurfaceNormalized = GreekNormalizer.NormalizeGreek(surfaceCore),
                PunctuationBefore = puncBefore,
                PunctuationAfter = puncAfter,
                Transliteration = translit,
                ContextualGloss = gloss,
                StrongCode = primaryStrong?.StrongCode,
                BaseStrongCode = primaryStrong?.BaseStrongCode,
                SecondaryStrongCodes = secList.ToArray(),
                Lemma = lemma,
                LemmaNormalized = GreekNormalizer.NormalizeGreek(lemma),
                MorphCode = morphCode,
                EditionGroup = editionGroup,
                EditionsRaw = NullIfEmpty(editionsRaw),
                InTr = inTr,
                InByz = inByz,
                InNa28 = inNa28,
                InSbl = inSbl,
                VariantNote = variantNote
            });
        }

        return tokens;
    }

    private static (string Greek, string? Translit) SplitGreekAndTranslit(string raw)
    {
        var trimmed = raw.Trim();
        var openParen = trimmed.LastIndexOf('(');
        var closeParen = trimmed.LastIndexOf(')');

        if (openParen > 0 && closeParen > openParen)
        {
            var greek = trimmed[..openParen].Trim();
            var translit = trimmed[(openParen + 1)..closeParen].Trim();
            return (greek, NullIfEmpty(translit));
        }

        return (trimmed, null);
    }

    private static string? ExtractLemma(string rawDictCol)
    {
        if (string.IsNullOrWhiteSpace(rawDictCol))
            return null;

        var eqIdx = rawDictCol.IndexOf('=');
        var formsPart = (eqIdx >= 0 ? rawDictCol[..eqIdx] : rawDictCol).Trim();
        var commaIdx = formsPart.IndexOf(',');
        var lemma = (commaIdx > 0 ? formsPart[..commaIdx] : formsPart).Trim().Normalize(NormalizationForm.FormC);
        return NullIfEmpty(lemma);
    }

    private static (bool InTr, bool InByz, bool InNa28, bool InSbl) ParseEditionFlags(string editionsRaw, string editionGroup)
    {
        if (!string.IsNullOrWhiteSpace(editionsRaw))
        {
            // Tokenize edition codes while ignoring "moved »2: ..." displacement notes
            var primaryPart = editionsRaw.Split(["moved", ";", "("], StringSplitOptions.RemoveEmptyEntries)[0];
            var tags = primaryPart.Split(['+', ' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            bool hasTr = tags.Any(t => t.Equals("TR", StringComparison.OrdinalIgnoreCase));
            bool hasByz = tags.Any(t => t.Equals("Byz", StringComparison.OrdinalIgnoreCase));
            bool hasNa28 = tags.Any(t => t.Equals("NA28", StringComparison.OrdinalIgnoreCase) || t.Equals("NA27", StringComparison.OrdinalIgnoreCase));
            bool hasSbl = tags.Any(t => t.Equals("SBL", StringComparison.OrdinalIgnoreCase));

            // Also check full editionsRaw if word is moved in TR/Byz/NA28/SBL (it still belongs to that edition!)
            if (!hasTr && editionsRaw.Contains("TR", StringComparison.Ordinal))
                hasTr = ContainsEditionToken(editionsRaw, "TR");
            if (!hasByz && editionsRaw.Contains("Byz", StringComparison.OrdinalIgnoreCase))
                hasByz = ContainsEditionToken(editionsRaw, "Byz");
            if (!hasNa28 && (editionsRaw.Contains("NA28", StringComparison.OrdinalIgnoreCase) || editionsRaw.Contains("NA27", StringComparison.OrdinalIgnoreCase)))
                hasNa28 = ContainsEditionToken(editionsRaw, "NA28") || ContainsEditionToken(editionsRaw, "NA27");
            if (!hasSbl && editionsRaw.Contains("SBL", StringComparison.OrdinalIgnoreCase))
                hasSbl = ContainsEditionToken(editionsRaw, "SBL");

            return (hasTr, hasByz, hasNa28, hasSbl);
        }

        // Fallback if editions column is empty: infer from editionGroup (NKO, K, N, O)
        var upper = editionGroup.ToUpperInvariant();
        var inTrFallback = upper.Contains('K');
        var inNaFallback = upper.Contains('N');
        return (inTrFallback, inTrFallback, inNaFallback, inNaFallback);
    }

    private static bool ContainsEditionToken(string raw, string token)
    {
        return Regex.IsMatch(raw, $@"(?<![A-Za-z0-9]){Regex.Escape(token)}(?![A-Za-z0-9])", RegexOptions.IgnoreCase);
    }

    private static string? BuildVariantNote(string meaningVar, string spellingVar)
    {
        if (string.IsNullOrWhiteSpace(meaningVar) && string.IsNullOrWhiteSpace(spellingVar))
            return null;
        if (string.IsNullOrWhiteSpace(meaningVar))
            return $"Ortografia: {spellingVar}";
        if (string.IsNullOrWhiteSpace(spellingVar))
            return meaningVar;
        return $"{meaningVar} | Ortografia: {spellingVar}";
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
