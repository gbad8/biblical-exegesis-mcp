namespace GreekNtMcp.Domain.Models;

public enum BibleEdition
{
    F35,
    Tr,
    Byz,
    Na28,
    Sbl,
    All
}

public enum DetailLevel
{
    Brief,
    Standard,
    Full
}

public sealed record BibleBook(
    short BookId,
    string OsisCode,
    string UsfmCode,
    string NameEn,
    string NamePt,
    string[] Aliases);

public sealed record BibleReference(
    BibleBook Book,
    short Chapter,
    short StartVerse,
    short EndVerse)
{
    public string DisplayPt => StartVerse == EndVerse
        ? $"{Book.NamePt} {Chapter}:{StartVerse}"
        : $"{Book.NamePt} {Chapter}:{StartVerse}-{EndVerse}";

    public string DisplayEn => StartVerse == EndVerse
        ? $"{Book.NameEn} {Chapter}:{StartVerse}"
        : $"{Book.NameEn} {Chapter}:{StartVerse}-{EndVerse}";
}

public sealed record StrongCodeParts(
    string StrongCode,
    string BaseStrongCode,
    int? StrongNumber,
    bool IsExtended,
    string? Suffix);

public sealed class LexiconEntry
{
    public string StrongCode { get; set; } = string.Empty;
    public string BaseStrongCode { get; set; } = string.Empty;
    public int? StrongNumber { get; set; }
    public bool IsExtended { get; set; }
    public string Lemma { get; set; } = string.Empty;
    public string LemmaNormalized { get; set; } = string.Empty;
    public string? Transliteration { get; set; }
    public string? TransliterationNormalized { get; set; }
    public string? PosCode { get; set; }
    public string? ShortGloss { get; set; }
    public string? TbesgMeaning { get; set; }
    public string? StrongDerivation { get; set; }
    public string? StrongDefinition { get; set; }
    public string? StrongKjvDefinition { get; set; }
    public int OccurrencesF35 { get; set; }
    public int OccurrencesTr { get; set; }
    public int OccurrencesByz { get; set; }
    public int OccurrencesNa28 { get; set; }
}

public sealed class AbbottSmithEntry
{
    public int Id { get; set; }
    public string? StrongCode { get; set; }
    public string Headword { get; set; } = string.Empty;
    public string HeadwordNormalized { get; set; } = string.Empty;
    public string? GrammaticalInfo { get; set; }
    public string? Etymology { get; set; }
    public string[] LxxEquivalents { get; set; } = [];
    public string[] Synonyms { get; set; } = [];
    public string? SensesJson { get; set; }
    public string FullMarkdown { get; set; } = string.Empty;
}

public sealed class LsjEntry
{
    public int Id { get; set; }
    public string StrongCode { get; set; } = string.Empty;
    public string BaseStrongCode { get; set; } = string.Empty;
    public string? Headword { get; set; }
    public string? HeadwordNormalized { get; set; }
    public string? SummaryMarkdown { get; set; }
    public string FullMarkdown { get; set; } = string.Empty;
    public int CharLength { get; set; }
}

public sealed class NtToken
{
    public long Id { get; set; }
    public short BookId { get; set; }
    public short Chapter { get; set; }
    public short Verse { get; set; }
    public short WordPosition { get; set; }
    public short SubPosition { get; set; }
    public string SurfaceForm { get; set; } = string.Empty;
    public string SurfaceNormalized { get; set; } = string.Empty;
    public string? PunctuationBefore { get; set; }
    public string? PunctuationAfter { get; set; }
    public string? Transliteration { get; set; }
    public string? ContextualGloss { get; set; }
    public string? StrongCode { get; set; }
    public string? BaseStrongCode { get; set; }
    public string[] SecondaryStrongCodes { get; set; } = [];
    public string? Lemma { get; set; }
    public string? LemmaNormalized { get; set; }
    public string? MorphCode { get; set; }
    public string? EditionGroup { get; set; }
    public string? EditionsRaw { get; set; }
    public bool InTr { get; set; }
    public bool InByz { get; set; }
    public bool InNa28 { get; set; }
    public bool InSbl { get; set; }
    public string? VariantNote { get; set; }
}

public sealed class F35Verse
{
    public short BookId { get; set; }
    public short Chapter { get; set; }
    public short Verse { get; set; }
    public string VerseText { get; set; } = string.Empty;
}

public sealed class F35Token
{
    public long Id { get; set; }
    public short BookId { get; set; }
    public short Chapter { get; set; }
    public short Verse { get; set; }
    public short WordPosition { get; set; }
    public string SurfaceForm { get; set; } = string.Empty;
    public string SurfaceNormalized { get; set; } = string.Empty;
    public string? PunctuationBefore { get; set; }
    public string? PunctuationAfter { get; set; }
    public string? Transliteration { get; set; }
    public string? ContextualGloss { get; set; }
    public string? StrongCode { get; set; }
    public string? BaseStrongCode { get; set; }
    public string[] SecondaryStrongCodes { get; set; } = [];
    public string? Lemma { get; set; }
    public string? LemmaNormalized { get; set; }
    public string? MorphCode { get; set; }
    public string AlignmentStatus { get; set; } = "untagged_f35_variant";
}

public sealed record LookupStrongResult(
    LexiconEntry PrimaryEntry,
    IReadOnlyList<LexiconEntry> ExtendedSubEntries,
    IReadOnlyList<AbbottSmithEntry> AbbottSmithEntries,
    IReadOnlyList<LsjEntry> LsjEntries);

public sealed class SearchLemmaHit
{
    public string StrongCode { get; set; } = string.Empty;
    public string BaseStrongCode { get; set; } = string.Empty;
    public string Lemma { get; set; } = string.Empty;
    public string? Transliteration { get; set; }
    public string? PosCode { get; set; }
    public string? ShortGloss { get; set; }
    public int OccurrencesF35 { get; set; }
    public int OccurrencesTr { get; set; }
    public string MatchType { get; set; } = string.Empty;
    public double Score { get; set; }
}

public sealed class WordOccurrenceRow
{
    public short BookId { get; set; }
    public string BookNamePt { get; set; } = string.Empty;
    public string OsisCode { get; set; } = string.Empty;
    public short Chapter { get; set; }
    public short Verse { get; set; }
    public short WordPosition { get; set; }
    public string SurfaceForm { get; set; } = string.Empty;
    public string? StrongCode { get; set; }
    public string? MorphCode { get; set; }
    public string? ContextualGloss { get; set; }
    public string? AlignmentStatus { get; set; }
}

public sealed class BookCountRow
{
    public string BookNamePt { get; set; } = string.Empty;
    public int Count { get; set; }
}

public sealed record WordOccurrenceContext(
    WordOccurrenceRow Hit,
    string HighlightedGreekVerse);

public sealed record WordOccurrencesResult(
    string ResolvedLabel,
    string? ResolvedStrongCode,
    string? ResolvedLemma,
    BibleEdition Edition,
    int TotalOccurrences,
    int UntaggedF35Count,
    IReadOnlyList<BookCountRow> BookDistribution,
    IReadOnlyList<WordOccurrenceContext> Items);

public sealed class VerseTokenView
{
    public short BookId { get; set; }
    public short Chapter { get; set; }
    public short Verse { get; set; }
    public short WordPosition { get; set; }
    public string SurfaceForm { get; set; } = string.Empty;
    public string SurfaceNormalized { get; set; } = string.Empty;
    public string? PunctuationBefore { get; set; }
    public string? PunctuationAfter { get; set; }
    public string? Transliteration { get; set; }
    public string? ContextualGloss { get; set; }
    public string? StrongCode { get; set; }
    public string? BaseStrongCode { get; set; }
    public string[] SecondaryStrongCodes { get; set; } = [];
    public string? Lemma { get; set; }
    public string? MorphCode { get; set; }
    public string? AlignmentStatus { get; set; }
    public bool InTr { get; set; }
    public bool InByz { get; set; }
    public bool InNa28 { get; set; }
    public bool InSbl { get; set; }
    public string? EditionsRaw { get; set; }
}

public sealed record VerseEditionComparison(
    short Verse,
    string? F35Text,
    string? TrText,
    string? ByzText,
    string? Na28Text,
    bool F35DiffersFromTr,
    bool TrDiffersFromByz,
    bool PrimaryDiffersFromNa28);

public sealed record ParsedVerseBlock(
    short Chapter,
    short Verse,
    string GreekText,
    IReadOnlyList<VerseTokenView> Tokens,
    VerseEditionComparison? Comparison);
