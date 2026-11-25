using System.Text.RegularExpressions;
using BiblicalExegesisMcp.Domain.Models;

namespace BiblicalExegesisMcp.Domain.Normalization;

public static partial class StrongCodeNormalizer
{
    [GeneratedRegex(@"^(?<prefix>[GgHh])?0*(?<num>\d{1,5})(?<suffix>[A-Za-z])?$", RegexOptions.Compiled)]
    private static partial Regex StrongRegex();

    [GeneratedRegex(@"\{(?<code>[Hh]\d{1,5}[A-Za-z]?)\}", RegexOptions.Compiled)]
    private static partial Regex TahotBracedStrongRegex();

    [GeneratedRegex(@"\{(?<code>[Hh]\d{1,5}[A-Za-z]?)=(?<lemma>[^=}\s][^=}]*)=", RegexOptions.Compiled)]
    private static partial Regex TahotExpandedLemmaRegex();

    /// <summary>
    /// Normalizes Greek or Hebrew Strong inputs like "3056", "G3056", "g26", "G0025a", "H1", "H7225G"
    /// into canonical G0001..G5624(+suffix) or H0001..H8674(+suffix).
    /// Bare numeric inputs without a prefix default to 'G' (backward-compatible with Greek NT tools).
    /// </summary>
    public static StrongCodeParts? TryNormalize(string? raw, char defaultPrefix = 'G')
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var trimmed = raw.Trim();
        var match = StrongRegex().Match(trimmed);
        if (!match.Success)
            return null;

        if (!int.TryParse(match.Groups["num"].Value, out var number) || number <= 0)
            return null;

        var prefix = match.Groups["prefix"].Success
            ? char.ToUpperInvariant(match.Groups["prefix"].Value[0])
            : char.ToUpperInvariant(defaultPrefix);

        var suffix = match.Groups["suffix"].Success
            ? match.Groups["suffix"].Value.ToUpperInvariant()
            : null;

        var baseCode = number < 10000 ? $"{prefix}{number:D4}" : $"{prefix}{number}";
        var fullCode = suffix is null ? baseCode : $"{baseCode}{suffix}";

        var canonicalLimit = prefix == 'H' ? 8674 : 5624;
        var isExtended = suffix is not null || number > canonicalLimit;

        return new StrongCodeParts(fullCode, baseCode, number, isExtended, suffix, prefix);
    }

    public static bool LooksLikeStrongCode(string? input) => TryNormalize(input) is not null;

    /// <summary>
    /// Extracts primary and secondary Strong codes and Robinson morphology from STEPBible TAGNT fields
    /// such as "G0976=N-NSF", "G2532=CONJ + G1473=P-1NS", or "H1732|G1138«G1138=N-GSM-P".
    /// </summary>
    public static (StrongCodeParts? Primary, string[] SecondaryCodes, string? MorphCode) ParseTagntGrammarField(string? rawField)
    {
        if (string.IsNullOrWhiteSpace(rawField))
            return (null, [], null);

        var segments = rawField.Split(['+', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        StrongCodeParts? primary = null;
        var secondary = new List<string>();
        string? primaryMorph = null;

        foreach (var seg in segments)
        {
            var cleanSeg = seg.Trim('{', '}', '[', ']', '(', ')', ' ');
            var eqIdx = cleanSeg.IndexOf('=');
            var strongPart = eqIdx >= 0 ? cleanSeg[..eqIdx].Trim() : cleanSeg;
            var morphPart = eqIdx >= 0 ? cleanSeg[(eqIdx + 1)..].Trim() : null;

            if (strongPart.Contains('|'))
            {
                var pipeParts = strongPart.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                strongPart = pipeParts.FirstOrDefault(p => p.StartsWith('G') || p.StartsWith('g')) ?? pipeParts[^1];
            }
            if (strongPart.Contains('«'))
            {
                strongPart = strongPart.Split('«', 2)[0].Trim();
            }

            if (!string.IsNullOrEmpty(morphPart))
            {
                var spaceIdx = morphPart.IndexOfAny([' ', ',', ';', '}']);
                if (spaceIdx > 0)
                    morphPart = morphPart[..spaceIdx];
            }

            var parsed = TryNormalize(strongPart, 'G');
            if (parsed is null)
                continue;

            if (primary is null)
            {
                primary = parsed;
                primaryMorph = string.IsNullOrWhiteSpace(morphPart) ? null : morphPart;
            }
            else
            {
                secondary.Add(parsed.BaseStrongCode);
                if (parsed.StrongCode != parsed.BaseStrongCode)
                    secondary.Add(parsed.StrongCode);
            }
        }

        return (primary, secondary.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), primaryMorph);
    }

    /// <summary>
    /// Extracts primary Hebrew/Aramaic Strong (inside braces "{H7225G}"), prefix/suffix Strongs (e.g. H9003, H9009),
    /// and exact dictionary lemma from STEPBible TAHOT columns.
    /// </summary>
    public static (StrongCodeParts? Primary, string[] PrefixSuffixCodes, string? Lemma) ParseTahotStrongField(
        string? dStrongsRaw,
        string? rootStrongCol,
        string? expandedTagsCol)
    {
        StrongCodeParts? primary = null;
        var affixes = new List<string>();

        // 1. Prefer the braced root Strong inside dStrongs (e.g. "H9003/{H7225G}\H9016")
        if (!string.IsNullOrWhiteSpace(dStrongsRaw))
        {
            // Ignore punctuation Strongs after '\' (such as \H9016 sof pasuq, \H9014 maqqef)
            var beforeBackslash = dStrongsRaw.Split('\\', 2)[0];

            var bracedMatch = TahotBracedStrongRegex().Match(beforeBackslash);
            if (bracedMatch.Success)
                primary = TryNormalize(bracedMatch.Groups["code"].Value, 'H');

            var parts = beforeBackslash.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                var clean = part.Trim('{', '}', ' ');
                if (string.IsNullOrEmpty(clean))
                    continue;

                var parsed = TryNormalize(clean, 'H');
                if (parsed is null)
                    continue;

                if (primary is null)
                {
                    primary = parsed;
                }
                else if (!string.Equals(parsed.StrongCode, primary.StrongCode, StringComparison.OrdinalIgnoreCase))
                {
                    affixes.Add(parsed.StrongCode);
                }
            }
        }

        // 2. Fallback to Root dStrong+Instance column (e.g. "H0853_A" -> "H0853")
        if (primary is null && !string.IsNullOrWhiteSpace(rootStrongCol))
        {
            var rootClean = rootStrongCol.Split('_', 2)[0].Trim();
            primary = TryNormalize(rootClean, 'H');
        }

        // 3. Extract exact Hebrew/Aramaic lemma from Expanded Strong tags (e.g. "{H7225G=רֵאשִׁית=: beginning}")
        string? lemma = null;
        if (!string.IsNullOrWhiteSpace(expandedTagsCol))
        {
            var lemmaMatch = TahotExpandedLemmaRegex().Match(expandedTagsCol);
            if (lemmaMatch.Success)
            {
                lemma = lemmaMatch.Groups["lemma"].Value.Trim();
                if (primary is null)
                    primary = TryNormalize(lemmaMatch.Groups["code"].Value, 'H');
            }
        }

        return (primary, affixes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), string.IsNullOrWhiteSpace(lemma) ? null : lemma);
    }
}
