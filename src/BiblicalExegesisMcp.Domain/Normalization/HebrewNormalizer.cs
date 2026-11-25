using System.Text;

namespace BiblicalExegesisMcp.Domain.Normalization;

public static class HebrewNormalizer
{
    /// <summary>
    /// Detects Hebrew/Aramaic Unicode characters (U+0590..U+05FF).
    /// </summary>
    public static bool ContainsHebrew(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return false;

        foreach (var ch in input)
        {
            if (ch >= '\u0590' && ch <= '\u05FF')
                return true;
        }
        return false;
    }

    /// <summary>
    /// Normalizes Biblical Hebrew/Aramaic to pure consonant skeleton for deterministic search:
    /// strips cantillation (te'amim U+0591..U+05AF), vowel points (niqqud U+05B0..U+05C7),
    /// maqqef/sof-pasuq/punctuation/slashes, and unifies final (sofit) letters (ך->כ, ם->מ, ן->נ, ף->פ, ץ->צ).
    /// </summary>
    public static string NormalizeHebrew(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            // Keep only the 22 Hebrew consonant letters (א U+05D0 .. ת U+05EA)
            if (ch >= '\u05D0' && ch <= '\u05EA')
            {
                sb.Append(NormalizeSofit(ch));
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Cleans a STEPBible TAHOT Hebrew token such as "הָ/אָֽרֶץ\׃" or "עַל\־" into:
    /// - SurfaceForm: clean continuous Masoretic word without '/' or '\' (e.g. "הָאָֽרֶץ")
    /// - SegmentedForm: morphological slash-segmented form without trailing punctuation (e.g. "הָ/אָֽרֶץ")
    /// - PunctuationAfter: trailing Masoretic punctuation (e.g. "׃" sof pasuq, "־" maqqef)
    /// </summary>
    public static (string SurfaceForm, string SegmentedForm, string? PunctuationAfter) CleanTahotSurface(string? rawHebrew)
    {
        if (string.IsNullOrWhiteSpace(rawHebrew))
            return (string.Empty, string.Empty, null);

        var trimmed = rawHebrew.Trim().Normalize(NormalizationForm.FormC);
        string coreSegmented;
        string? puncAfter = null;

        var backslashIdx = trimmed.IndexOf('\\');
        if (backslashIdx >= 0)
        {
            coreSegmented = trimmed[..backslashIdx].Trim();
            var trailing = trimmed[(backslashIdx + 1)..].Replace("/", "").Replace("\\", "").Trim();
            puncAfter = string.IsNullOrEmpty(trailing) ? null : trailing;
        }
        else
        {
            coreSegmented = trimmed;
        }

        // Also handle trailing sof pasuq (׃) or maqqef (־) if attached without backslash
        if (puncAfter is null && coreSegmented.Length > 1 && coreSegmented[^1] is '\u05C3' or '\u05BE')
        {
            puncAfter = coreSegmented[^1].ToString();
            coreSegmented = coreSegmented[..^1];
        }

        var surface = coreSegmented.Replace("/", "").Replace("\\", "");
        return (surface, coreSegmented, puncAfter);
    }

    private static char NormalizeSofit(char ch) => ch switch
    {
        'ך' => 'כ',
        'ם' => 'מ',
        'ן' => 'נ',
        'ף' => 'פ',
        'ץ' => 'צ',
        _ => ch
    };
}
