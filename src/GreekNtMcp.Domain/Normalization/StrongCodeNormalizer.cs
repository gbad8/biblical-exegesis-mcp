using System.Text.RegularExpressions;
using GreekNtMcp.Domain.Models;

namespace GreekNtMcp.Domain.Normalization;

public static partial class StrongCodeNormalizer
{
    [GeneratedRegex(@"^[Gg]?0*(\d{1,5})([A-Za-z])?$", RegexOptions.Compiled)]
    private static partial Regex StrongRegex();

    /// <summary>
    /// Normalizes inputs like "3056", "G3056", "g26", "G0025a", "G0001G" into canonical G0001..G5624(+suffix).
    /// </summary>
    public static StrongCodeParts? TryNormalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var trimmed = raw.Trim();
        var match = StrongRegex().Match(trimmed);
        if (!match.Success)
            return null;

        if (!int.TryParse(match.Groups[1].Value, out var number) || number <= 0)
            return null;

        var suffixRaw = match.Groups[2].Success ? match.Groups[2].Value : null;
        // STEPBible uses uppercase disambiguation letters like G0001G or lowercase like G0025a;
        // preserve suffix as uppercase if single disambiguation tag or normalize consistently to lowercase.
        // We normalize suffix to uppercase (matching STEPBible dStrong convention e.g. G0025A / G0001G)
        // while keeping base_strong_code as G0001..G5624.
        var suffix = suffixRaw?.ToUpperInvariant();
        var baseCode = number < 10000 ? $"G{number:D4}" : $"G{number}";
        var fullCode = suffix is null ? baseCode : $"{baseCode}{suffix}";
        var isExtended = suffix is not null || number > 5624;

        return new StrongCodeParts(fullCode, baseCode, number, isExtended, suffix);
    }

    public static bool LooksLikeStrongCode(string? input) => TryNormalize(input) is not null;

    /// <summary>
    /// Extracts primary and secondary Strong codes and Robinson morphology from STEPBible TAGNT fields
    /// such as "G0976=N-NSF", "G2532=CONJ + G1473=P-1NS", or "G0025G=V-AAI-3S".
    /// </summary>
    public static (StrongCodeParts? Primary, string[] SecondaryCodes, string? MorphCode) ParseTagntGrammarField(string? rawField)
    {
        if (string.IsNullOrWhiteSpace(rawField))
            return (null, [], null);

        // Split compound entries separated by '+' or '/' (e.g., crasis κἀγώ = G2532=CONJ + G1473=P-1NS)
        var segments = rawField.Split(['+', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        StrongCodeParts? primary = null;
        var secondary = new List<string>();
        string? primaryMorph = null;

        foreach (var seg in segments)
        {
            // Clean enclosing brackets/braces that STEPBible sometimes uses for variants e.g. {G3588=T-NSM}
            var cleanSeg = seg.Trim('{', '}', '[', ']', '(', ')', ' ');
            var eqIdx = cleanSeg.IndexOf('=');
            var strongPart = eqIdx >= 0 ? cleanSeg[..eqIdx].Trim() : cleanSeg;
            var morphPart = eqIdx >= 0 ? cleanSeg[(eqIdx + 1)..].Trim() : null;

            // Handle STEPBible cross-mapped formats like "H1732|G1138«G1138" or "G2264G«G2264"
            if (strongPart.Contains('|'))
            {
                var pipeParts = strongPart.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                strongPart = pipeParts.FirstOrDefault(p => p.StartsWith('G') || p.StartsWith('g')) ?? pipeParts[^1];
            }
            if (strongPart.Contains('«'))
            {
                // Prefer left side (disambiguated dStrong e.g. G2264G) over right side (base G2264)
                strongPart = strongPart.Split('«', 2)[0].Trim();
            }

            // Strip any trailing tags after space or comma in morphPart
            if (!string.IsNullOrEmpty(morphPart))
            {
                var spaceIdx = morphPart.IndexOfAny([' ', ',', ';', '}']);
                if (spaceIdx > 0)
                    morphPart = morphPart[..spaceIdx];
            }

            var parsed = TryNormalize(strongPart);
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
}
