using BiblicalExegesisMcp.Domain.Models;
using BiblicalExegesisMcp.Domain.Normalization;

namespace BiblicalExegesisMcp.Domain.References;

public static class BibleBookCatalog
{
    public static readonly IReadOnlyList<BibleBook> OldTestamentBooks =
    [
        new(1, "Gen", "GEN", "Genesis", "Gênesis",
            ["gen", "gn", "ge", "genesis"], "OT"),
        new(2, "Exo", "EXO", "Exodus", "Êxodo",
            ["exo", "ex", "exod", "exodus", "exodo"], "OT"),
        new(3, "Lev", "LEV", "Leviticus", "Levítico",
            ["lev", "lv", "le", "leviticus", "levitico"], "OT"),
        new(4, "Num", "NUM", "Numbers", "Números",
            ["num", "nm", "nu", "numbers", "numeros"], "OT"),
        new(5, "Deu", "DEU", "Deuteronomy", "Deuteronômio",
            ["deu", "dt", "deut", "deuteronomy", "deuteronomio"], "OT"),
        new(6, "Jos", "JOS", "Joshua", "Josué",
            ["jos", "js", "josh", "joshua", "josue"], "OT"),
        new(7, "Jdg", "JDG", "Judges", "Juízes",
            ["jdg", "jz", "judg", "judges", "juizes"], "OT"),
        new(8, "Rut", "RUT", "Ruth", "Rute",
            ["rut", "rt", "ru", "ruth", "rute"], "OT"),
        new(9, "1Sa", "1SA", "1 Samuel", "1 Samuel",
            ["1sa", "1 sa", "1sm", "1 sm", "1sam", "1 sam", "1samuel", "1 samuel"], "OT"),
        new(10, "2Sa", "2SA", "2 Samuel", "2 Samuel",
            ["2sa", "2 sa", "2sm", "2 sm", "2sam", "2 sam", "2samuel", "2 samuel"], "OT"),
        new(11, "1Ki", "1KI", "1 Kings", "1 Reis",
            ["1ki", "1 ki", "1rs", "1 rs", "1kgs", "1 kgs", "1kings", "1 kings", "1reis", "1 reis"], "OT"),
        new(12, "2Ki", "2KI", "2 Kings", "2 Reis",
            ["2ki", "2 ki", "2rs", "2 rs", "2kgs", "2 kgs", "2kings", "2 kings", "2reis", "2 reis"], "OT"),
        new(13, "1Ch", "1CH", "1 Chronicles", "1 Crônicas",
            ["1ch", "1 ch", "1cr", "1 cr", "1chr", "1 chr", "1chronicles", "1 chronicles", "1cronicas", "1 cronicas"], "OT"),
        new(14, "2Ch", "2CH", "2 Chronicles", "2 Crônicas",
            ["2ch", "2 ch", "2cr", "2 cr", "2chr", "2 chr", "2chronicles", "2 chronicles", "2cronicas", "2 cronicas"], "OT"),
        new(15, "Ezr", "EZR", "Ezra", "Esdras",
            ["ezr", "ed", "esd", "ezra", "esdras"], "OT"),
        new(16, "Neh", "NEH", "Nehemiah", "Neemias",
            ["neh", "ne", "nehemiah", "neemias"], "OT"),
        new(17, "Est", "EST", "Esther", "Ester",
            ["est", "et", "esth", "esther", "ester"], "OT"),
        new(18, "Job", "JOB", "Job", "Jó",
            ["job", "jb", "jo_ot"], "OT"), // note: "jo" stays mapped to John (João) for NT backward compatibility; use "jó"/"jb"/"job" for Job!
        new(19, "Psa", "PSA", "Psalms", "Salmos",
            ["psa", "ps", "sl", "sal", "psalm", "psalms", "salmo", "salmos"], "OT"),
        new(20, "Pro", "PRO", "Proverbs", "Provérbios",
            ["pro", "pr", "pv", "prv", "prov", "proverbs", "proverbios"], "OT"),
        new(21, "Ecc", "ECC", "Ecclesiastes", "Eclesiastes",
            ["ecc", "ec", "eccl", "qoh", "ecclesiastes", "eclesiastes"], "OT"),
        new(22, "Sng", "SNG", "Song of Solomon", "Cânticos",
            ["sng", "ct", "cnt", "can", "sol", "sos", "song", "song of solomon", "song of songs", "canticos", "cantares"], "OT"),
        new(23, "Isa", "ISA", "Isaiah", "Isaías",
            ["isa", "is", "isaiah", "isaias"], "OT"),
        new(24, "Jer", "JER", "Jeremiah", "Jeremias",
            ["jer", "jr", "jeremiah", "jeremias"], "OT"),
        new(25, "Lam", "LAM", "Lamentations", "Lamentações",
            ["lam", "lm", "lamentations", "lamentacoes"], "OT"),
        new(26, "Ezk", "EZK", "Ezekiel", "Ezequiel",
            ["ezk", "eze", "ez", "ezek", "ezekiel", "ezequiel"], "OT"),
        new(27, "Dan", "DAN", "Daniel", "Daniel",
            ["dan", "dn", "da", "daniel"], "OT"),
        new(28, "Hos", "HOS", "Hosea", "Oseias",
            ["hos", "os", "hosea", "oseias"], "OT"),
        new(29, "Jol", "JOL", "Joel", "Joel",
            ["jol", "joe", "jl", "joel"], "OT"),
        new(30, "Amo", "AMO", "Amos", "Amós",
            ["amo", "am", "amos"], "OT"),
        new(31, "Oba", "OBA", "Obadiah", "Obadias",
            ["oba", "ob", "obad", "obadiah", "obadias"], "OT"),
        new(32, "Jon", "JON", "Jonah", "Jonas",
            ["jon", "jn_ot", "jnh", "jonah", "jonas"], "OT"),
        new(33, "Mic", "MIC", "Micah", "Miqueias",
            ["mic", "mq", "mi", "micah", "miqueias"], "OT"),
        new(34, "Nam", "NAM", "Nahum", "Naum",
            ["nam", "nah", "na", "nahum", "naum"], "OT"),
        new(35, "Hab", "HAB", "Habakkuk", "Habacuque",
            ["hab", "hc", "habakkuk", "habacuque"], "OT"),
        new(36, "Zep", "ZEP", "Zephaniah", "Sofonias",
            ["zep", "sf", "sof", "zeph", "zephaniah", "sofonias"], "OT"),
        new(37, "Hag", "HAG", "Haggai", "Ageu",
            ["hag", "ag", "haggai", "ageu"], "OT"),
        new(38, "Zec", "ZEC", "Zechariah", "Zacarias",
            ["zec", "zc", "zac", "zech", "zechariah", "zacarias"], "OT"),
        new(39, "Mal", "MAL", "Malachi", "Malaquias",
            ["mal", "ml", "malachi", "malaquias"], "OT")
    ];

    public static readonly IReadOnlyList<BibleBook> NewTestamentBooks =
    [
        new(40, "Mat", "MAT", "Matthew", "Mateus",
            ["mat", "mt", "matt", "matthew", "mateus", "sao mateus"], "NT"),
        new(41, "Mrk", "MRK", "Mark", "Marcos",
            ["mrk", "mk", "mc", "mar", "mark", "marcos", "sao marcos"], "NT"),
        new(42, "Luk", "LUK", "Luke", "Lucas",
            ["luk", "lk", "lc", "luc", "luke", "lucas", "sao lucas"], "NT"),
        new(43, "Jhn", "JHN", "John", "João",
            ["jhn", "jn", "jo", "joh", "john", "joao", "sao joao"], "NT"),
        new(44, "Act", "ACT", "Acts", "Atos",
            ["act", "ac", "at", "acts", "atos", "atos dos apostolos"], "NT"),
        new(45, "Rom", "ROM", "Romans", "Romanos",
            ["rom", "rm", "ro", "romans", "romanos"], "NT"),
        new(46, "1Co", "1CO", "1 Corinthians", "1 Coríntios",
            ["1co", "1 co", "1cor", "1 cor", "1corinthians", "1 corinthians", "1corintios", "1 corintios", "ico", "i co", "i corintios"], "NT"),
        new(47, "2Co", "2CO", "2 Corinthians", "2 Coríntios",
            ["2co", "2 co", "2cor", "2 cor", "2corinthians", "2 corinthians", "2corintios", "2 corintios", "iico", "ii co", "ii corintios"], "NT"),
        new(48, "Gal", "GAL", "Galatians", "Gálatas",
            ["gal", "gl", "ga", "galatians", "galatas"], "NT"),
        new(49, "Eph", "EPH", "Ephesians", "Efésios",
            ["eph", "ef", "ep", "ephesians", "efesios"], "NT"),
        new(50, "Php", "PHP", "Philippians", "Filipenses",
            ["php", "fp", "fl", "phil", "philippians", "filipenses"], "NT"),
        new(51, "Col", "COL", "Colossians", "Colossenses",
            ["col", "cl", "colossians", "colossenses"], "NT"),
        new(52, "1Th", "1TH", "1 Thessalonians", "1 Tessalonicenses",
            ["1th", "1 th", "1ts", "1 ts", "1thess", "1 thess", "1thessalonians", "1 thessalonians", "1tessalonicenses", "1 tessalonicenses"], "NT"),
        new(53, "2Th", "2TH", "2 Thessalonians", "2 Tessalonicenses",
            ["2th", "2 th", "2ts", "2 ts", "2thess", "2 thess", "2thessalonians", "2 thessalonians", "2tessalonicenses", "2 tessalonicenses"], "NT"),
        new(54, "1Ti", "1TI", "1 Timothy", "1 Timóteo",
            ["1ti", "1 ti", "1tm", "1 tm", "1tim", "1 tim", "1timothy", "1 timothy", "1timoteo", "1 timoteo"], "NT"),
        new(55, "2Ti", "2TI", "2 Timothy", "2 Timóteo",
            ["2ti", "2 ti", "2tm", "2 tm", "2tim", "2 tim", "2timothy", "2 timothy", "2timoteo", "2 timoteo"], "NT"),
        new(56, "Tit", "TIT", "Titus", "Tito",
            ["tit", "tt", "ti", "titus", "tito"], "NT"),
        new(57, "Phm", "PHM", "Philemon", "Filemom",
            ["phm", "fm", "flm", "phlm", "philemon", "filemom", "filemon"], "NT"),
        new(58, "Heb", "HEB", "Hebrews", "Hebreus",
            ["heb", "hb", "he", "hebrews", "hebreus"], "NT"),
        new(59, "Jas", "JAS", "James", "Tiago",
            ["jas", "tg", "jam", "jms", "james", "tiago"], "NT"),
        new(60, "1Pe", "1PE", "1 Peter", "1 Pedro",
            ["1pe", "1 pe", "1pt", "1 pt", "1pet", "1 pet", "1peter", "1 peter", "1pedro", "1 pedro"], "NT"),
        new(61, "2Pe", "2PE", "2 Peter", "2 Pedro",
            ["2pe", "2 pe", "2pt", "2 pt", "2pet", "2 pet", "2peter", "2 peter", "2pedro", "2 pedro"], "NT"),
        new(62, "1Jn", "1JN", "1 John", "1 João",
            ["1jn", "1 jn", "1jo", "1 jo", "1jhn", "1 jhn", "1john", "1 john", "1joao", "1 joao"], "NT"),
        new(63, "2Jn", "2JN", "2 John", "2 João",
            ["2jn", "2 jn", "2jo", "2 jo", "2jhn", "2 jhn", "2john", "2 john", "2joao", "2 joao"], "NT"),
        new(64, "3Jn", "3JN", "3 John", "3 João",
            ["3jn", "3 jn", "3jo", "3 jo", "3jhn", "3 jhn", "3john", "3 john", "3joao", "3 joao"], "NT"),
        new(65, "Jud", "JUD", "Jude", "Judas",
            ["jud", "jd", "jude", "judas"], "NT"),
        new(66, "Rev", "REV", "Revelation", "Apocalipse",
            ["rev", "ap", "apc", "apoc", "revelation", "apocalipse"], "NT")
    ];

    public static readonly IReadOnlyList<BibleBook> AllBooks =
        OldTestamentBooks.Concat(NewTestamentBooks).ToList();

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

        foreach (var book in AllBooks)
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

        // Disambiguate accented "Jó" (Job) vs unaccented "Jo" (João)
        var trimmed = input.Trim();
        if (trimmed.Equals("Jó", StringComparison.OrdinalIgnoreCase))
            return ById[18];

        var norm = GreekNormalizer.NormalizeLatin(trimmed.Replace(".", " "));
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
