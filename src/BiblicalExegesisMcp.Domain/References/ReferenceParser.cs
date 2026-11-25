using System.Text.RegularExpressions;
using BiblicalExegesisMcp.Domain.Models;

namespace BiblicalExegesisMcp.Domain.References;

public static partial class ReferenceParser
{
    public const int MaxVersesPerRange = 30;

    // Matches formats like "Gn 1:1", "Sl 23:1-6", "Jo 1:1", "João 1.1-5", "1Co 13:4-7", "1 Samuel 17:45", "Jó 19:25"
    [GeneratedRegex(@"^\s*(?<book>(?:[1-3]\s*|I{1,3}\s+)?[\p{L}\.]+(?:\s+[\p{L}]+)*?)\s*[\.\s]\s*(?<ch>\d{1,3})\s*[:\.,v]\s*(?<v1>\d{1,3})(?:\s*[-–—]\s*(?<v2>\d{1,3}))?\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex RefRegex();

    public static (BibleReference? Reference, string? Error) Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return (null, "Referência bíblica vazia. Exemplos válidos: 'Gn 1:1', 'Sl 23:1-6', 'Jo 1:1', '1Co 13:4-7'.");

        var match = RefRegex().Match(input);
        if (!match.Success)
            return (null, $"Formato de referência não reconhecido: '{input}'. Use formatos como 'Gn 1:1', 'Sl 23:1-6', 'Jo 1:1-5' ou 'Rom 8:28'.");

        var bookRaw = match.Groups["book"].Value;
        var book = BibleBookCatalog.FindByAlias(bookRaw);
        if (book is null)
            return (null, $"Livro bíblico não reconhecido: '{bookRaw}'. Este servidor cobre todos os 66 livros (Gênesis a Apocalipse).");

        if (!short.TryParse(match.Groups["ch"].Value, out var chapter) || chapter <= 0)
            return (null, $"Capítulo inválido em '{input}'.");

        if (!short.TryParse(match.Groups["v1"].Value, out var startVerse) || startVerse <= 0)
            return (null, $"Versículo inicial inválido em '{input}'.");

        var endVerse = startVerse;
        if (match.Groups["v2"].Success)
        {
            if (!short.TryParse(match.Groups["v2"].Value, out endVerse) || endVerse < startVerse)
                return (null, $"Faixa de versículos inválida em '{input}': o versículo final deve ser maior ou igual ao inicial.");
        }

        var count = endVerse - startVerse + 1;
        if (count > MaxVersesPerRange)
            return (null, $"Faixa muito extensa ({count} versículos). O limite por consulta é de {MaxVersesPerRange} versículos para preservar a precisão e o contexto.");

        return (new BibleReference(book, chapter, startVerse, endVerse), null);
    }

    public static BibleEdition ParseEdition(string? edition, BibleEdition defaultEdition = BibleEdition.F35)
    {
        if (string.IsNullOrWhiteSpace(edition))
            return defaultEdition;

        return edition.Trim().ToLowerInvariant() switch
        {
            "f35" or "family35" or "family_35" or "familia35" or "familia_35" or "pickering" => BibleEdition.F35,
            "tr" or "textus_receptus" or "scrivener" or "kjv" => BibleEdition.Tr,
            "byz" or "byzantine" or "bizantino" or "rp" or "majority" => BibleEdition.Byz,
            "na28" or "na27" or "na" or "nestle" or "critical" => BibleEdition.Na28,
            "sbl" or "sblgnt" => BibleEdition.Sbl,
            "mt" or "tm" or "wlc" or "bhs" or "masoretic" or "massoretico" or "qere" or "leningrad" => BibleEdition.Mt,
            "ketiv" or "kethib" or "ktiv" => BibleEdition.Ketiv,
            "all" or "todas" => BibleEdition.All,
            _ => defaultEdition
        };
    }

    public static BibleEdition ParseEditionForBook(string? edition, BibleBook book) =>
        ParseEdition(edition, book.IsOldTestament ? BibleEdition.Mt : BibleEdition.F35);

    public static string EditionDisplayName(BibleEdition edition) => edition switch
    {
        BibleEdition.F35 => "Família 35 (F35 — Wilbur Pickering)",
        BibleEdition.Tr => "Textus Receptus (TR)",
        BibleEdition.Byz => "Texto Bizantino (Byz / Robinson-Pierpont)",
        BibleEdition.Na28 => "Nestle-Aland 28 (NA28)",
        BibleEdition.Sbl => "SBLGNT (SBL)",
        BibleEdition.Mt => "Texto Massorético (TM — Códice de Leningrado / WLC com leitura Qere)",
        BibleEdition.Ketiv => "Texto Massorético (TM — Leitura Consonantal Ketiv)",
        BibleEdition.All => "Todas as Edições",
        _ => edition.ToString()
    };

    public static DetailLevel ParseDetailLevel(string? level)
    {
        if (string.IsNullOrWhiteSpace(level))
            return DetailLevel.Standard;

        return level.Trim().ToLowerInvariant() switch
        {
            "brief" or "resumo" or "short" => DetailLevel.Brief,
            "full" or "completo" or "complete" => DetailLevel.Full,
            _ => DetailLevel.Standard
        };
    }
}
