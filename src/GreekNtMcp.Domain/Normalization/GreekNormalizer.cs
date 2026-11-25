using System.Globalization;
using System.Text;

namespace GreekNtMcp.Domain.Normalization;

public static class GreekNormalizer
{
    private static readonly HashSet<char> GreekPunctuation =
    [
        '.', ',', ';', ':', '·', ';', '—', '–', '-', '(', ')', '[', ']', '{', '}',
        '"', '\'', '’', '‘', '«', '»', '“', '”', '!', '?', '†', '⸂', '⸃', '⸀', '⸁',
        '⸄', '⸅', '¶', '/', '\\', '*'
    ];

    /// <summary>
    /// Normalizes polytonic Greek text for deterministic accent-insensitive search:
    /// FormD -> strip NonSpacingMark -> lowercase -> final sigma (ς -> σ) -> strip punctuation -> FormC.
    /// </summary>
    public static string NormalizeGreek(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var decomposed = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);

        foreach (var ch in decomposed)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
                continue;

            if (GreekPunctuation.Contains(ch) || char.IsWhiteSpace(ch))
                continue;

            var lower = char.ToLowerInvariant(ch);
            if (lower == 'ς')
                lower = 'σ';

            // Keep Greek letters (Basic Greek U+0370..U+03FF and Extended Greek U+1F00..U+1FFF) and Latin/digits
            if (char.IsLetterOrDigit(lower))
                sb.Append(lower);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Normalizes Latin transliteration or book name aliases (strips diacritics, lowercases, keeps alphanumerics).
    /// </summary>
    public static string NormalizeLatin(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var decomposed = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);

        foreach (var ch in decomposed)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark)
                continue;

            if (char.IsLetterOrDigit(ch))
                sb.Append(char.ToLowerInvariant(ch));
            else if (ch is ' ' or '-' or '_')
                sb.Append(' ');
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public static bool ContainsGreek(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return false;

        foreach (var ch in input)
        {
            if ((ch >= '\u0370' && ch <= '\u03FF') || (ch >= '\u1F00' && ch <= '\u1FFF'))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Separates leading and trailing punctuation from a Greek surface word while preserving NFC polytonic spelling.
    /// </summary>
    public static (string? Before, string Core, string? After) SplitPunctuation(string rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return (null, string.Empty, null);

        var s = rawToken.Trim().Normalize(NormalizationForm.FormC);
        int start = 0;
        while (start < s.Length && IsBoundaryPunctuation(s[start]))
            start++;

        int end = s.Length - 1;
        while (end >= start && IsBoundaryPunctuation(s[end]))
            end--;

        if (start > end)
            return (null, string.Empty, s);

        var before = start > 0 ? s[..start] : null;
        var core = s[start..(end + 1)];
        var after = end < s.Length - 1 ? s[(end + 1)..] : null;
        return (before, core, after);
    }

    private static bool IsBoundaryPunctuation(char c)
    {
        // Keep internal elision apostrophe only if inside word; boundary punctuation gets split
        return GreekPunctuation.Contains(c) && c != '\'' && c != '’'
            || c is '(' or ')' or '[' or ']' or '«' or '»' or '"' or '“' or '”' or ',' or '.' or ';' or '·' or ';' or ':' or '!' or '?' or '—' or '–';
    }
}
