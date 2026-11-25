using GreekNtMcp.Domain.Models;
using GreekNtMcp.Domain.Normalization;

namespace GreekNtMcp.Domain.References;

public static class BibleBookCatalog
{
    public static readonly IReadOnlyList<BibleBook> NewTestamentBooks =
    [
        new(40, "Mat", "MAT", "Matthew", "Mateus",
            ["mat", "mt", "matt", "matthew", "mateus", "sao mateus"]),
        new(41, "Mrk", "MRK", "Mark", "Marcos",
            ["mrk", "mk", "mc", "mar", "mark", "marcos", "sao marcos"]),
        new(42, "Luk", "LUK", "Luke", "Lucas",
            ["luk", "lk", "lc", "luc", "luke", "lucas", "sao lucas"]),
        new(43, "Jhn", "JHN", "John", "João",
            ["jhn", "jn", "jo", "joh", "john", "joao", "sao joao"]),
        new(44, "Act", "ACT", "Acts", "Atos",
            ["act", "ac", "at", "acts", "atos", "atos dos apostolos"]),
        new(45, "Rom", "ROM", "Romans", "Romanos",
            ["rom", "rm", "ro", "romans", "romanos"]),
        new(46, "1Co", "1CO", "1 Corinthians", "1 Coríntios",
            ["1co", "1 co", "1cor", "1 cor", "1corinthians", "1 corinthians", "1corintios", "1 corintios", "ico", "i co", "i corintios"]),
        new(47, "2Co", "2CO", "2 Corinthians", "2 Coríntios",
            ["2co", "2 co", "2cor", "2 cor", "2corinthians", "2 corinthians", "2corintios", "2 corintios", "iico", "ii co", "ii corintios"]),
        new(48, "Gal", "GAL", "Galatians", "Gálatas",
            ["gal", "gl", "ga", "galatians", "galatas"]),
        new(49, "Eph", "EPH", "Ephesians", "Efésios",
            ["eph", "ef", "ep", "ephesians", "efesios"]),
        new(50, "Php", "PHP", "Philippians", "Filipenses",
            ["php", "fp", "fl", "phil", "philippians", "filipenses"]),
        new(51, "Col", "COL", "Colossians", "Colossenses",
            ["col", "cl", "colossians", "colossenses"]),
        new(52, "1Th", "1TH", "1 Thessalonians", "1 Tessalonicenses",
            ["1th", "1 th", "1ts", "1 ts", "1thess", "1 thess", "1thessalonians", "1 thessalonians", "1tessalonicenses", "1 tessalonicenses"]),
        new(53, "2Th", "2TH", "2 Thessalonians", "2 Tessalonicenses",
            ["2th", "2 th", "2ts", "2 ts", "2thess", "2 thess", "2thessalonians", "2 thessalonians", "2tessalonicenses", "2 tessalonicenses"]),
        new(54, "1Ti", "1TI", "1 Timothy", "1 Timóteo",
            ["1ti", "1 ti", "1tm", "1 tm", "1tim", "1 tim", "1timothy", "1 timothy", "1timoteo", "1 timoteo"]),
        new(55, "2Ti", "2TI", "2 Timothy", "2 Timóteo",
            ["2ti", "2 ti", "2tm", "2 tm", "2tim", "2 tim", "2timothy", "2 timothy", "2timoteo", "2 timoteo"]),
        new(56, "Tit", "TIT", "Titus", "Tito",
            ["tit", "tt", "ti", "titus", "tito"]),
        new(57, "Phm", "PHM", "Philemon", "Filemom",
            ["phm", "fm", "flm", "phlm", "philemon", "filemom", "filemon"]),
        new(58, "Heb", "HEB", "Hebrews", "Hebreus",
            ["heb", "hb", "he", "hebrews", "hebreus"]),
        new(59, "Jas", "JAS", "James", "Tiago",
            ["jas", "tg", "jam", "jms", "james", "tiago"]),
        new(60, "1Pe", "1PE", "1 Peter", "1 Pedro",
            ["1pe", "1 pe", "1pt", "1 pt", "1pet", "1 pet", "1peter", "1 peter", "1pedro", "1 pedro"]),
        new(61, "2Pe", "2PE", "2 Peter", "2 Pedro",
            ["2pe", "2 pe", "2pt", "2 pt", "2pet", "2 pet", "2peter", "2 peter", "2pedro", "2 pedro"]),
        new(62, "1Jn", "1JN", "1 John", "1 João",
            ["1jn", "1 jn", "1jo", "1 jo", "1jhn", "1 jhn", "1john", "1 john", "1joao", "1 joao"]),
        new(63, "2Jn", "2JN", "2 John", "2 João",
            ["2jn", "2 jn", "2jo", "2 jo", "2jhn", "2 jhn", "2john", "2 john", "2joao", "2 joao"]),
        new(64, "3Jn", "3JN", "3 John", "3 João",
            ["3jn", "3 jn", "3jo", "3 jo", "3jhn", "3 jhn", "3john", "3 john", "3joao", "3 joao"]),
        new(65, "Jud", "JUD", "Jude", "Judas",
            ["jud", "jd", "jude", "judas"]),
        new(66, "Rev", "REV", "Revelation", "Apocalipse",
            ["rev", "ap", "apc", "apoc", "revelation", "apocalipse"])
    ];

    private static readonly Dictionary<string, BibleBook> AliasLookup;
    private static readonly Dictionary<short, BibleBook> ById;
    private static readonly Dictionary<string, BibleBook> ByOsis;
    private static readonly Dictionary<string, BibleBook> ByUsfm;

    static BibleBookCatalog()
    {
        AliasLookup = new Dictionary<string, BibleBook>(StringComparer.OrdinalIgnoreCase);
        ById = new Dictionary<short, BibleBook>();
        ByOsis = new Dictionary<string, BibleBook>(StringComparer.OrdinalIgnoreCase);
        ByUsfm = new Dictionary<string, BibleBook>(StringComparer.OrdinalIgnoreCase);

        foreach (var book in NewTestamentBooks)
        {
            ById[book.BookId] = book;
            ByOsis[book.OsisCode] = book;
            ByUsfm[book.UsfmCode] = book;

            foreach (var alias in book.Aliases)
            {
                var norm = GreekNormalizer.NormalizeLatin(alias);
                AliasLookup[norm] = book;
                AliasLookup[norm.Replace(" ", "")] = book;
            }
        }
    }

    public static BibleBook? FindByAlias(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var norm = GreekNormalizer.NormalizeLatin(input.Replace(".", " "));
        if (AliasLookup.TryGetValue(norm, out var book))
            return book;

        var compact = norm.Replace(" ", "");
        return AliasLookup.TryGetValue(compact, out book) ? book : null;
    }

    public static BibleBook? FindByOsisOrUsfm(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var trimmed = code.Trim();
        if (ByOsis.TryGetValue(trimmed, out var book))
            return book;
        if (ByUsfm.TryGetValue(trimmed, out book))
            return book;
        return FindByAlias(trimmed);
    }

    public static BibleBook? FindById(short bookId) => ById.GetValueOrDefault(bookId);
}
