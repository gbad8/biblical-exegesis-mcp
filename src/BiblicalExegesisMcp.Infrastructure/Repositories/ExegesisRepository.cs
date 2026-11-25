using System.Text;
using Dapper;
using BiblicalExegesisMcp.Domain.Models;
using BiblicalExegesisMcp.Domain.Normalization;
using BiblicalExegesisMcp.Domain.References;
using Npgsql;

namespace BiblicalExegesisMcp.Infrastructure.Repositories;

public sealed record HealthStatus(
    bool DatabaseConnected,
    int LexiconCount,
    int AbbottSmithCount,
    int LsjCount,
    int BdbCount,
    int TagntTokenCount,
    int F35TokenCount,
    int OtTokenCount,
    IReadOnlyList<EtlAuditRow> Sources);

public sealed class EtlAuditRow
{
    public string SourceName { get; set; } = string.Empty;
    public string? VersionRef { get; set; }
    public int RecordCount { get; set; }
    public DateTimeOffset IngestedAt { get; set; }
}

internal sealed class HealthCountsRow
{
    public int Lex { get; set; }
    public int Asm { get; set; }
    public int Lsj { get; set; }
    public int Bdb { get; set; }
    public int Nt { get; set; }
    public int F35 { get; set; }
    public int Ot { get; set; }
}

public sealed class ExegesisRepository
{
    private readonly NpgsqlDataSource _dataSource;

    static ExegesisRepository()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public ExegesisRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<HealthStatus> CheckHealthAsync(CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        var counts = await conn.QuerySingleAsync<HealthCountsRow>(
            """
            SELECT
                (SELECT COUNT(*)::int FROM lexicon_entries)      AS lex,
                (SELECT COUNT(*)::int FROM lexicon_abbott_smith) AS asm,
                (SELECT COUNT(*)::int FROM lexicon_lsj)          AS lsj,
                (SELECT COUNT(*)::int FROM lexicon_bdb)          AS bdb,
                (SELECT COUNT(*)::int FROM nt_tokens)            AS nt,
                (SELECT COUNT(*)::int FROM f35_tokens)           AS f35,
                (SELECT COUNT(*)::int FROM ot_tokens)            AS ot;
            """);

        var sources = (await conn.QueryAsync<EtlAuditRow>(
            """
            SELECT source_name, version_ref, record_count, ingested_at
            FROM etl_metadata
            ORDER BY source_name;
            """)).ToList();

        return new HealthStatus(
            DatabaseConnected: true,
            LexiconCount: counts.Lex,
            AbbottSmithCount: counts.Asm,
            LsjCount: counts.Lsj,
            BdbCount: counts.Bdb,
            TagntTokenCount: counts.Nt,
            F35TokenCount: counts.F35,
            OtTokenCount: counts.Ot,
            Sources: sources);
    }

    public async Task<LookupStrongResult?> LookupStrongAsync(string rawCode, CancellationToken ct = default)
    {
        var parsed = StrongCodeNormalizer.TryNormalize(rawCode);
        if (parsed is null)
            return null;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        var primary = await conn.QueryFirstOrDefaultAsync<LexiconEntry>(
            """
            SELECT *
            FROM lexicon_entries
            WHERE strong_code = @Exact
               OR strong_code = @Base
            ORDER BY (strong_code = @Exact) DESC, is_extended ASC, strong_code ASC
            LIMIT 1;
            """,
            new { Exact = parsed.StrongCode, Base = parsed.BaseStrongCode });

        if (primary is null)
            return null;

        var subEntries = (await conn.QueryAsync<LexiconEntry>(
            """
            SELECT *
            FROM lexicon_entries
            WHERE base_strong_code = @Base
              AND strong_code <> @PrimaryCode
            ORDER BY strong_code ASC;
            """,
            new { Base = primary.BaseStrongCode, PrimaryCode = primary.StrongCode })).ToList();

        List<AbbottSmithEntry> abbottSmith = [];
        List<LsjEntry> lsj = [];
        List<BdbEntry> bdb = [];

        if (parsed.IsHebrew || primary.Language is "heb" or "arc")
        {
            bdb = (await conn.QueryAsync<BdbEntry>(
                """
                SELECT *
                FROM lexicon_bdb
                WHERE strong_code = @Exact
                   OR base_strong_code = @Base
                ORDER BY (strong_code = @Exact) DESC, id ASC;
                """,
                new { Exact = primary.StrongCode, Base = primary.BaseStrongCode })).ToList();

            if (bdb.Count == 0 && !string.IsNullOrEmpty(primary.LemmaNormalized))
            {
                bdb = (await conn.QueryAsync<BdbEntry>(
                    """
                    SELECT *
                    FROM lexicon_bdb
                    WHERE headword_normalized = @LemmaNorm
                    ORDER BY id ASC
                    LIMIT 3;
                    """,
                    new { LemmaNorm = primary.LemmaNormalized })).ToList();
            }
        }
        else
        {
            abbottSmith = (await conn.QueryAsync<AbbottSmithEntry>(
                """
                SELECT *
                FROM lexicon_abbott_smith
                WHERE strong_code = @Base
                   OR strong_code = @Exact
                ORDER BY id ASC;
                """,
                new { Base = primary.BaseStrongCode, Exact = primary.StrongCode })).ToList();

            if (abbottSmith.Count == 0 && !string.IsNullOrEmpty(primary.LemmaNormalized))
            {
                abbottSmith = (await conn.QueryAsync<AbbottSmithEntry>(
                    """
                    SELECT *
                    FROM lexicon_abbott_smith
                    WHERE headword_normalized = @LemmaNorm
                    ORDER BY id ASC
                    LIMIT 3;
                    """,
                    new { LemmaNorm = primary.LemmaNormalized })).ToList();
            }

            lsj = (await conn.QueryAsync<LsjEntry>(
                """
                SELECT *
                FROM lexicon_lsj
                WHERE strong_code = @Exact
                   OR base_strong_code = @Base
                ORDER BY (strong_code = @Exact) DESC, id ASC;
                """,
                new { Exact = primary.StrongCode, Base = primary.BaseStrongCode })).ToList();
        }

        return new LookupStrongResult(primary, subEntries, abbottSmith, lsj, bdb);
    }

    public async Task<IReadOnlyList<SearchLemmaHit>> SearchLemmaAsync(
        string query,
        bool fuzzy = true,
        int limit = 15,
        string testament = "all",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        limit = Math.Clamp(limit, 1, 50);
        var trimmed = query.Trim();
        var isGreek = GreekNormalizer.ContainsGreek(trimmed);
        var isHebrew = HebrewNormalizer.ContainsHebrew(trimmed);
        var isScript = isGreek || isHebrew;

        var scriptNorm = isHebrew
            ? HebrewNormalizer.NormalizeHebrew(trimmed)
            : GreekNormalizer.NormalizeGreek(trimmed);
        var latinNorm = GreekNormalizer.NormalizeLatin(trimmed);
        var strongParts = StrongCodeNormalizer.TryNormalize(trimmed);

        var testFilter = (testament ?? "all").Trim().ToLowerInvariant();
        if (isGreek) testFilter = "nt";
        else if (isHebrew) testFilter = "ot";

        var langClause = testFilter switch
        {
            "nt" or "greek" or "grego" => "AND language = 'grc'",
            "ot" or "at" or "hebrew" or "hebraico" => "AND language IN ('heb', 'arc')",
            _ => string.Empty
        };

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        if (fuzzy)
            await conn.ExecuteAsync("SET pg_trgm.similarity_threshold = 0.30;");

        var sql = $"""
            WITH ranked AS (
                SELECT
                    strong_code,
                    base_strong_code,
                    language,
                    lemma,
                    transliteration,
                    pos_code,
                    short_gloss,
                    occurrences_f35,
                    occurrences_tr,
                    occurrences_mt,
                    CASE
                        WHEN @StrongExact IS NOT NULL AND (strong_code = @StrongExact OR base_strong_code = @StrongBase) THEN 'exact_strong'
                        WHEN @IsScript AND lemma_normalized = @ScriptNorm THEN 'exact_script'
                        WHEN @IsHebrew AND root_hebrew = @ScriptNorm THEN 'exact_root'
                        WHEN NOT @IsScript AND transliteration_normalized = @LatinNorm THEN 'exact_translit'
                        WHEN @IsScript AND lemma_normalized LIKE @ScriptPrefix THEN 'prefix_script'
                        WHEN NOT @IsScript AND transliteration_normalized LIKE @LatinPrefix THEN 'prefix_translit'
                        WHEN NOT @IsScript AND short_gloss ILIKE @GlossExact THEN 'exact_gloss'
                        WHEN NOT @IsScript AND search_vector @@ websearch_to_tsquery('english', @RawQuery) THEN 'fts_gloss'
                        WHEN @Fuzzy AND @IsScript AND similarity(lemma_normalized, @ScriptNorm) >= 0.30 THEN 'fuzzy_script'
                        WHEN @Fuzzy AND NOT @IsScript AND similarity(COALESCE(transliteration_normalized, ''), @LatinNorm) >= 0.35 THEN 'fuzzy_translit'
                        WHEN @Fuzzy AND NOT @IsScript AND similarity(COALESCE(short_gloss, ''), @RawQuery) >= 0.30 THEN 'fuzzy_gloss'
                        ELSE NULL
                    END AS match_type,
                    CASE
                        WHEN @StrongExact IS NOT NULL AND strong_code = @StrongExact THEN 1.00
                        WHEN @StrongExact IS NOT NULL AND base_strong_code = @StrongBase THEN 0.98
                        WHEN @IsScript AND lemma_normalized = @ScriptNorm THEN 0.99
                        WHEN @IsHebrew AND root_hebrew = @ScriptNorm THEN 0.94
                        WHEN NOT @IsScript AND transliteration_normalized = @LatinNorm THEN 0.95
                        WHEN @IsScript AND lemma_normalized LIKE @ScriptPrefix THEN 0.85
                        WHEN NOT @IsScript AND transliteration_normalized LIKE @LatinPrefix THEN 0.82
                        WHEN NOT @IsScript AND short_gloss ILIKE @GlossExact THEN 0.80
                        WHEN NOT @IsScript AND search_vector @@ websearch_to_tsquery('english', @RawQuery)
                            THEN 0.65 + LEAST(0.14, ts_rank(search_vector, websearch_to_tsquery('english', @RawQuery)))
                        WHEN @Fuzzy AND @IsScript AND similarity(lemma_normalized, @ScriptNorm) >= 0.30
                            THEN 0.55 * similarity(lemma_normalized, @ScriptNorm)
                        WHEN @Fuzzy AND NOT @IsScript AND similarity(COALESCE(transliteration_normalized, ''), @LatinNorm) >= 0.35
                            THEN 0.55 * similarity(COALESCE(transliteration_normalized, ''), @LatinNorm)
                        WHEN @Fuzzy AND NOT @IsScript AND similarity(COALESCE(short_gloss, ''), @RawQuery) >= 0.30
                            THEN 0.50 * similarity(COALESCE(short_gloss, ''), @RawQuery)
                        ELSE 0.0
                    END AS score
                FROM lexicon_entries
                WHERE 1 = 1
                    {langClause}
                    AND (
                        (@StrongExact IS NOT NULL AND (strong_code = @StrongExact OR base_strong_code = @StrongBase))
                        OR (@IsScript AND lemma_normalized LIKE @ScriptPrefix)
                        OR (@IsHebrew AND root_hebrew = @ScriptNorm)
                        OR (NOT @IsScript AND transliteration_normalized LIKE @LatinPrefix)
                        OR (NOT @IsScript AND short_gloss ILIKE @GlossContains)
                        OR (NOT @IsScript AND search_vector @@ websearch_to_tsquery('english', @RawQuery))
                        OR (@Fuzzy AND @IsScript AND lemma_normalized % @ScriptNorm)
                        OR (@Fuzzy AND NOT @IsScript AND transliteration_normalized % @LatinNorm)
                        OR (@Fuzzy AND NOT @IsScript AND short_gloss % @RawQuery)
                    )
            )
            SELECT
                strong_code,
                base_strong_code,
                language,
                lemma,
                transliteration,
                pos_code,
                short_gloss,
                occurrences_f35,
                occurrences_tr,
                occurrences_mt,
                match_type,
                score
            FROM ranked
            WHERE match_type IS NOT NULL
            ORDER BY score DESC, (occurrences_f35 + occurrences_tr + occurrences_mt) DESC, strong_code ASC
            LIMIT @Limit;
            """;

        var rows = await conn.QueryAsync<SearchLemmaHit>(sql, new
        {
            RawQuery = trimmed,
            IsScript = isScript,
            IsHebrew = isHebrew,
            ScriptNorm = scriptNorm,
            ScriptPrefix = scriptNorm + "%",
            LatinNorm = latinNorm,
            LatinPrefix = latinNorm + "%",
            GlossExact = trimmed,
            GlossContains = "%" + trimmed + "%",
            StrongExact = strongParts?.StrongCode,
            StrongBase = strongParts?.BaseStrongCode,
            Fuzzy = fuzzy,
            Limit = limit
        });

        return rows.ToList();
    }

    public async Task<WordOccurrencesResult> GetWordOccurrencesAsync(
        string lemmaOrStrong,
        string? editionRaw = null,
        string? bookAlias = null,
        int limit = 30,
        int offset = 0,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 100);
        offset = Math.Max(0, offset);

        BibleBook? bookFilter = null;
        if (!string.IsNullOrWhiteSpace(bookAlias))
        {
            bookFilter = BibleBookCatalog.FindByAlias(bookAlias)
                ?? throw new ArgumentException($"Livro bíblico não reconhecido: '{bookAlias}'.");
        }

        var strongParts = StrongCodeNormalizer.TryNormalize(lemmaOrStrong);
        bool isHebrewInput = strongParts?.IsHebrew == true || HebrewNormalizer.ContainsHebrew(lemmaOrStrong);
        string? resolvedStrong = strongParts?.StrongCode;
        string? resolvedBaseStrong = strongParts?.BaseStrongCode;
        string? resolvedLemma = null;
        string? lemmaNorm = null;
        bool isOldTestament = isHebrewInput || bookFilter?.IsOldTestament == true;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        if (strongParts is not null)
        {
            var entry = await conn.QueryFirstOrDefaultAsync<LexiconEntry>(
                """
                SELECT * FROM lexicon_entries
                WHERE strong_code = @Exact OR base_strong_code = @Base
                ORDER BY (strong_code = @Exact) DESC, is_extended ASC
                LIMIT 1;
                """,
                new { Exact = strongParts.StrongCode, Base = strongParts.BaseStrongCode });

            resolvedLemma = entry?.Lemma;
            if (entry is not null)
                isOldTestament = entry.Language is "heb" or "arc";
        }
        else
        {
            lemmaNorm = isHebrewInput
                ? HebrewNormalizer.NormalizeHebrew(lemmaOrStrong)
                : GreekNormalizer.NormalizeGreek(lemmaOrStrong);

            var entry = await conn.QueryFirstOrDefaultAsync<LexiconEntry>(
                """
                SELECT * FROM lexicon_entries
                WHERE lemma_normalized = @LemmaNorm
                   OR transliteration_normalized = @LatinNorm
                ORDER BY is_extended ASC, (occurrences_f35 + occurrences_tr + occurrences_mt) DESC
                LIMIT 1;
                """,
                new { LemmaNorm = lemmaNorm, LatinNorm = GreekNormalizer.NormalizeLatin(lemmaOrStrong) });

            if (entry is not null)
            {
                resolvedStrong = entry.BaseStrongCode;
                resolvedBaseStrong = entry.BaseStrongCode;
                resolvedLemma = entry.Lemma;
                lemmaNorm = entry.LemmaNormalized;
                isOldTestament = entry.Language is "heb" or "arc";
            }
        }

        var label = resolvedStrong is not null && resolvedLemma is not null
            ? $"{resolvedLemma} ({resolvedStrong})"
            : resolvedStrong ?? lemmaOrStrong.Trim();

        if (isOldTestament)
        {
            var otEdition = ReferenceParser.ParseEdition(editionRaw, BibleEdition.Mt);
            if (otEdition is not (BibleEdition.Mt or BibleEdition.Ketiv or BibleEdition.All))
                otEdition = BibleEdition.Mt;

            return await QueryOtOccurrencesAsync(
                conn, label, resolvedStrong, resolvedBaseStrong, resolvedLemma, lemmaNorm,
                strongParts?.IsExtended == true, otEdition, bookFilter?.BookId, limit, offset);
        }

        var ntEdition = ReferenceParser.ParseEdition(editionRaw, BibleEdition.F35);
        if (ntEdition == BibleEdition.F35)
        {
            return await QueryF35OccurrencesAsync(
                conn, label, resolvedStrong, resolvedBaseStrong, resolvedLemma, lemmaNorm,
                strongParts?.IsExtended == true, bookFilter?.BookId, limit, offset);
        }

        return await QueryTagntOccurrencesAsync(
            conn, label, resolvedStrong, resolvedBaseStrong, resolvedLemma, lemmaNorm,
            strongParts?.IsExtended == true, ntEdition, bookFilter?.BookId, limit, offset);
    }

    private static async Task<WordOccurrencesResult> QueryOtOccurrencesAsync(
        NpgsqlConnection conn,
        string label,
        string? resolvedStrong,
        string? resolvedBaseStrong,
        string? resolvedLemma,
        string? lemmaNorm,
        bool exactExtendedStrong,
        BibleEdition edition,
        short? bookIdFilter,
        int limit,
        int offset)
    {
        var matchClause = exactExtendedStrong
            ? "(t.strong_code = @ExactStrong OR @ExactStrong = ANY(t.prefix_suffix_strongs))"
            : """
              (
                  (@BaseStrong IS NOT NULL AND (
                      t.base_strong_code = @BaseStrong
                      OR t.strong_code = @ExactStrong
                      OR @BaseStrong = ANY(t.prefix_suffix_strongs)
                  ))
                  OR (@BaseStrong IS NULL AND @LemmaNorm IS NOT NULL AND (t.lemma_normalized = @LemmaNorm OR t.surface_normalized = @LemmaNorm))
              )
              """;

        var edClause = edition switch
        {
            BibleEdition.Mt => " AND t.in_mt = TRUE",
            BibleEdition.Ketiv => " AND t.in_ketiv = TRUE",
            _ => string.Empty
        };
        var bookClause = bookIdFilter.HasValue ? " AND t.book_id = @BookId" : string.Empty;

        var param = new
        {
            ExactStrong = resolvedStrong,
            BaseStrong = resolvedBaseStrong,
            LemmaNorm = lemmaNorm,
            BookId = bookIdFilter,
            Limit = limit,
            Offset = offset
        };

        var total = await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*)::int FROM ot_tokens t WHERE {matchClause}{edClause}{bookClause};", param);

        var distribution = offset == 0 && total > 0
            ? (await conn.QueryAsync<BookCountRow>(
                $"""
                SELECT b.name_pt AS BookNamePt, COUNT(*)::int AS Count
                FROM ot_tokens t
                JOIN bible_books b ON b.book_id = t.book_id
                WHERE {matchClause}{edClause}{bookClause}
                GROUP BY b.book_id, b.name_pt
                ORDER BY b.book_id ASC;
                """, param)).ToList()
            : [];

        var hits = (await conn.QueryAsync<WordOccurrenceRow>(
            $"""
            SELECT
                t.book_id,
                b.name_pt AS book_name_pt,
                b.osis_code,
                t.chapter,
                t.verse,
                t.hebrew_ref,
                t.word_position,
                t.surface_form,
                t.strong_code,
                t.morph_code,
                t.contextual_gloss,
                NULL::text AS alignment_status,
                t.language
            FROM ot_tokens t
            JOIN bible_books b ON b.book_id = t.book_id
            WHERE {matchClause}{edClause}{bookClause}
            ORDER BY t.book_id ASC, t.chapter ASC, t.verse ASC, t.word_position ASC, t.sub_position ASC
            LIMIT @Limit OFFSET @Offset;
            """, param)).ToList();

        var contexts = await BuildHighlightedOtContextsAsync(conn, hits, edClause);

        return new WordOccurrencesResult(
            label, resolvedStrong, resolvedLemma, edition, IsOldTestament: true,
            total, 0, distribution, contexts);
    }

    private static async Task<IReadOnlyList<WordOccurrenceContext>> BuildHighlightedOtContextsAsync(
        NpgsqlConnection conn,
        List<WordOccurrenceRow> hits,
        string edClause)
    {
        if (hits.Count == 0)
            return [];

        var distinctVerses = hits.Select(h => (h.BookId, h.Chapter, h.Verse)).Distinct().ToList();
        var verseMap = new Dictionary<(short, short, short), List<VerseTokenView>>();

        foreach (var (bookId, chapter, verse) in distinctVerses)
        {
            var tokens = (await conn.QueryAsync<VerseTokenView>(
                $"""
                SELECT book_id, chapter, verse, word_position, surface_form, surface_normalized, punctuation_after
                FROM ot_tokens t
                WHERE t.book_id = @BookId AND t.chapter = @Chapter AND t.verse = @Verse {edClause}
                ORDER BY t.word_position ASC, t.sub_position ASC;
                """,
                new { BookId = bookId, Chapter = chapter, Verse = verse })).ToList();

            verseMap[(bookId, chapter, verse)] = tokens;
        }

        var result = new List<WordOccurrenceContext>(hits.Count);
        foreach (var hit in hits)
        {
            var tokens = verseMap.GetValueOrDefault((hit.BookId, hit.Chapter, hit.Verse)) ?? [];
            result.Add(new WordOccurrenceContext(hit, ReconstructHebrew(tokens, hit.WordPosition)));
        }

        return result;
    }

    private static async Task<WordOccurrencesResult> QueryF35OccurrencesAsync(
        NpgsqlConnection conn,
        string label,
        string? resolvedStrong,
        string? resolvedBaseStrong,
        string? resolvedLemma,
        string? lemmaNorm,
        bool exactExtendedStrong,
        short? bookIdFilter,
        int limit,
        int offset)
    {
        var whereClause = BuildNtMatchClause("t", exactExtendedStrong);
        var bookClause = bookIdFilter.HasValue ? " AND t.book_id = @BookId" : string.Empty;
        var param = new
        {
            ExactStrong = resolvedStrong,
            BaseStrong = resolvedBaseStrong,
            LemmaNorm = lemmaNorm,
            BookId = bookIdFilter,
            Limit = limit,
            Offset = offset
        };

        var total = await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*)::int FROM f35_tokens t WHERE {whereClause}{bookClause};", param);

        var untaggedCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*)::int FROM f35_tokens WHERE alignment_status = 'untagged_f35_variant';");

        var distribution = offset == 0 && total > 0
            ? (await conn.QueryAsync<BookCountRow>(
                $"""
                SELECT b.name_pt AS BookNamePt, COUNT(*)::int AS Count
                FROM f35_tokens t
                JOIN bible_books b ON b.book_id = t.book_id
                WHERE {whereClause}{bookClause}
                GROUP BY b.book_id, b.name_pt
                ORDER BY b.book_id ASC;
                """, param)).ToList()
            : [];

        var hits = (await conn.QueryAsync<WordOccurrenceRow>(
            $"""
            SELECT
                t.book_id,
                b.name_pt AS book_name_pt,
                b.osis_code,
                t.chapter,
                t.verse,
                NULL::text AS hebrew_ref,
                t.word_position,
                t.surface_form,
                t.strong_code,
                t.morph_code,
                t.contextual_gloss,
                t.alignment_status,
                'grc'::text AS language
            FROM f35_tokens t
            JOIN bible_books b ON b.book_id = t.book_id
            WHERE {whereClause}{bookClause}
            ORDER BY t.book_id ASC, t.chapter ASC, t.verse ASC, t.word_position ASC
            LIMIT @Limit OFFSET @Offset;
            """, param)).ToList();

        var contexts = await BuildHighlightedNtContextsAsync(conn, hits, isF35: true, BibleEdition.F35);

        return new WordOccurrencesResult(
            label, resolvedStrong, resolvedLemma, BibleEdition.F35, IsOldTestament: false,
            total, untaggedCount, distribution, contexts);
    }

    private static async Task<WordOccurrencesResult> QueryTagntOccurrencesAsync(
        NpgsqlConnection conn,
        string label,
        string? resolvedStrong,
        string? resolvedBaseStrong,
        string? resolvedLemma,
        string? lemmaNorm,
        bool exactExtendedStrong,
        BibleEdition edition,
        short? bookIdFilter,
        int limit,
        int offset)
    {
        var whereClause = BuildNtMatchClause("t", exactExtendedStrong);
        var edClause = NtEditionFilterSql("t", edition);
        var bookClause = bookIdFilter.HasValue ? " AND t.book_id = @BookId" : string.Empty;
        var param = new
        {
            ExactStrong = resolvedStrong,
            BaseStrong = resolvedBaseStrong,
            LemmaNorm = lemmaNorm,
            BookId = bookIdFilter,
            Limit = limit,
            Offset = offset
        };

        var total = await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*)::int FROM nt_tokens t WHERE ({whereClause}){edClause}{bookClause};", param);

        var distribution = offset == 0 && total > 0
            ? (await conn.QueryAsync<BookCountRow>(
                $"""
                SELECT b.name_pt AS BookNamePt, COUNT(*)::int AS Count
                FROM nt_tokens t
                JOIN bible_books b ON b.book_id = t.book_id
                WHERE ({whereClause}){edClause}{bookClause}
                GROUP BY b.book_id, b.name_pt
                ORDER BY b.book_id ASC;
                """, param)).ToList()
            : [];

        var hits = (await conn.QueryAsync<WordOccurrenceRow>(
            $"""
            SELECT
                t.book_id,
                b.name_pt AS book_name_pt,
                b.osis_code,
                t.chapter,
                t.verse,
                NULL::text AS hebrew_ref,
                t.word_position,
                t.surface_form,
                t.strong_code,
                t.morph_code,
                t.contextual_gloss,
                NULL::text AS alignment_status,
                'grc'::text AS language
            FROM nt_tokens t
            JOIN bible_books b ON b.book_id = t.book_id
            WHERE ({whereClause}){edClause}{bookClause}
            ORDER BY t.book_id ASC, t.chapter ASC, t.verse ASC, t.word_position ASC, t.sub_position ASC
            LIMIT @Limit OFFSET @Offset;
            """, param)).ToList();

        var contexts = await BuildHighlightedNtContextsAsync(conn, hits, isF35: false, edition);

        return new WordOccurrencesResult(
            label, resolvedStrong, resolvedLemma, edition, IsOldTestament: false,
            total, 0, distribution, contexts);
    }

    private static string BuildNtMatchClause(string alias, bool exactExtendedStrong)
    {
        if (exactExtendedStrong)
            return $"({alias}.strong_code = @ExactStrong OR @ExactStrong = ANY({alias}.secondary_strong_codes))";

        return $"""
            (
                (@BaseStrong IS NOT NULL AND (
                    {alias}.base_strong_code = @BaseStrong
                    OR {alias}.strong_code = @ExactStrong
                    OR @BaseStrong = ANY({alias}.secondary_strong_codes)
                ))
                OR (@BaseStrong IS NULL AND @LemmaNorm IS NOT NULL AND ({alias}.lemma_normalized = @LemmaNorm OR {alias}.surface_normalized = @LemmaNorm))
            )
            """;
    }

    private static string NtEditionFilterSql(string alias, BibleEdition edition) => edition switch
    {
        BibleEdition.Tr => $" AND {alias}.in_tr = TRUE",
        BibleEdition.Byz => $" AND {alias}.in_byz = TRUE",
        BibleEdition.Na28 => $" AND {alias}.in_na28 = TRUE",
        BibleEdition.Sbl => $" AND {alias}.in_sbl = TRUE",
        _ => string.Empty
    };

    private static async Task<IReadOnlyList<WordOccurrenceContext>> BuildHighlightedNtContextsAsync(
        NpgsqlConnection conn,
        List<WordOccurrenceRow> hits,
        bool isF35,
        BibleEdition edition)
    {
        if (hits.Count == 0)
            return [];

        var distinctVerses = hits.Select(h => (h.BookId, h.Chapter, h.Verse)).Distinct().ToList();
        var verseTokenMap = new Dictionary<(short, short, short), List<VerseTokenView>>();

        foreach (var (bookId, chapter, verse) in distinctVerses)
        {
            var sql = isF35
                ? """
                  SELECT book_id, chapter, verse, word_position, surface_form, surface_normalized,
                         punctuation_before, punctuation_after
                  FROM f35_tokens
                  WHERE book_id = @BookId AND chapter = @Chapter AND verse = @Verse
                  ORDER BY word_position ASC;
                  """
                : $"""
                   SELECT book_id, chapter, verse, word_position, surface_form, surface_normalized,
                          punctuation_before, punctuation_after
                   FROM nt_tokens t
                   WHERE t.book_id = @BookId AND t.chapter = @Chapter AND t.verse = @Verse {NtEditionFilterSql("t", edition)}
                   ORDER BY t.word_position ASC, t.sub_position ASC;
                   """;

            var tokens = (await conn.QueryAsync<VerseTokenView>(sql, new { BookId = bookId, Chapter = chapter, Verse = verse })).ToList();
            verseTokenMap[(bookId, chapter, verse)] = tokens;
        }

        var result = new List<WordOccurrenceContext>(hits.Count);
        foreach (var hit in hits)
        {
            var tokens = verseTokenMap.GetValueOrDefault((hit.BookId, hit.Chapter, hit.Verse)) ?? [];
            var sb = new StringBuilder();
            for (int i = 0; i < tokens.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                var tok = tokens[i];
                if (!string.IsNullOrEmpty(tok.PunctuationBefore))
                    sb.Append(tok.PunctuationBefore);
                if (tok.WordPosition == hit.WordPosition && tok.SurfaceForm == hit.SurfaceForm)
                    sb.Append("**").Append(tok.SurfaceForm).Append("**");
                else
                    sb.Append(tok.SurfaceForm);
                if (!string.IsNullOrEmpty(tok.PunctuationAfter))
                    sb.Append(tok.PunctuationAfter);
            }
            result.Add(new WordOccurrenceContext(hit, sb.ToString()));
        }

        return result;
    }

    public async Task<IReadOnlyList<ParsedVerseBlock>> ParseVerseRangeAsync(
        BibleReference reference,
        BibleEdition edition,
        bool includeVariants = true,
        CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        if (reference.Book.IsOldTestament)
            return await ParseOtVerseRangeAsync(conn, reference, edition, includeVariants);

        return await ParseNtVerseRangeAsync(conn, reference, edition, includeVariants);
    }

    private static async Task<IReadOnlyList<ParsedVerseBlock>> ParseOtVerseRangeAsync(
        NpgsqlConnection conn,
        BibleReference reference,
        BibleEdition edition,
        bool includeVariants)
    {
        var allTokens = (await conn.QueryAsync<VerseTokenView>(
            """
            SELECT
                book_id, chapter, verse, hebrew_ref, word_position,
                surface_form, segmented_form, surface_normalized, punctuation_after,
                transliteration, contextual_gloss,
                strong_code, base_strong_code,
                prefix_suffix_strongs AS secondary_strong_codes,
                lemma, morph_code, language, source_type, variant_note,
                in_mt AS in_tr, in_ketiv AS in_byz
            FROM ot_tokens
            WHERE book_id = @BookId AND chapter = @Chapter AND verse BETWEEN @StartVerse AND @EndVerse
            ORDER BY verse ASC, word_position ASC, sub_position ASC;
            """,
            new
            {
                BookId = reference.Book.BookId,
                Chapter = reference.Chapter,
                StartVerse = reference.StartVerse,
                EndVerse = reference.EndVerse
            })).ToList();

        var blocks = new List<ParsedVerseBlock>();

        for (short v = reference.StartVerse; v <= reference.EndVerse; v++)
        {
            var verseAll = allTokens.Where(t => t.Verse == v).ToList();
            if (verseAll.Count == 0)
                continue;

            var selected = edition switch
            {
                BibleEdition.Ketiv => verseAll.Where(t => t.InByz).ToList(), // mapped in_ketiv -> in_byz above
                BibleEdition.All => verseAll,
                _ => verseAll.Where(t => t.InTr).ToList() // mapped in_mt -> in_tr above
            };

            var hebrewRef = selected.FirstOrDefault(t => !string.IsNullOrEmpty(t.HebrewRef))?.HebrewRef
                            ?? verseAll.FirstOrDefault(t => !string.IsNullOrEmpty(t.HebrewRef))?.HebrewRef;

            var notes = new List<string>();
            if (includeVariants)
            {
                if (!string.IsNullOrEmpty(hebrewRef))
                    notes.Add($"**Versificação Hebraica (BHS/Leningrado):** `{hebrewRef}`");

                foreach (var tok in verseAll)
                {
                    if (!string.IsNullOrWhiteSpace(tok.VariantNote))
                    {
                        notes.Add($"Posição #{tok.WordPosition} (`{tok.SurfaceForm}`, fonte `{tok.SourceType}`): {tok.VariantNote}");
                    }
                }
            }

            var text = ReconstructHebrew(selected);
            blocks.Add(new ParsedVerseBlock(
                reference.Chapter, v, text, IsOldTestament: true, hebrewRef,
                selected, Comparison: null, notes));
        }

        return blocks;
    }

    private static async Task<IReadOnlyList<ParsedVerseBlock>> ParseNtVerseRangeAsync(
        NpgsqlConnection conn,
        BibleReference reference,
        BibleEdition edition,
        bool includeVariants)
    {
        var param = new
        {
            BookId = reference.Book.BookId,
            Chapter = reference.Chapter,
            StartVerse = reference.StartVerse,
            EndVerse = reference.EndVerse
        };

        var f35TokensAll = (await conn.QueryAsync<VerseTokenView>(
            """
            SELECT book_id, chapter, verse, word_position, surface_form, surface_normalized,
                   punctuation_before, punctuation_after, transliteration, contextual_gloss,
                   strong_code, base_strong_code, secondary_strong_codes, lemma, morph_code,
                   alignment_status, 'grc'::text AS language
            FROM f35_tokens
            WHERE book_id = @BookId AND chapter = @Chapter AND verse BETWEEN @StartVerse AND @EndVerse
            ORDER BY verse ASC, word_position ASC;
            """, param)).ToList();

        var f35VerseTexts = (await conn.QueryAsync<F35Verse>(
            """
            SELECT book_id, chapter, verse, verse_text
            FROM f35_verses
            WHERE book_id = @BookId AND chapter = @Chapter AND verse BETWEEN @StartVerse AND @EndVerse;
            """, param)).ToDictionary(v => v.Verse, v => v.VerseText);

        var tagntTokensAll = (await conn.QueryAsync<VerseTokenView>(
            """
            SELECT book_id, chapter, verse, word_position, surface_form, surface_normalized,
                   punctuation_before, punctuation_after, transliteration, contextual_gloss,
                   strong_code, base_strong_code, secondary_strong_codes, lemma, morph_code,
                   in_tr, in_byz, in_na28, in_sbl, editions_raw, 'grc'::text AS language
            FROM nt_tokens
            WHERE book_id = @BookId AND chapter = @Chapter AND verse BETWEEN @StartVerse AND @EndVerse
            ORDER BY verse ASC, word_position ASC, sub_position ASC;
            """, param)).ToList();

        var blocks = new List<ParsedVerseBlock>();

        for (short v = reference.StartVerse; v <= reference.EndVerse; v++)
        {
            var f35Tokens = f35TokensAll.Where(t => t.Verse == v).ToList();
            var tagntForVerse = tagntTokensAll.Where(t => t.Verse == v).ToList();
            var trTokens = tagntForVerse.Where(t => t.InTr).ToList();
            var byzTokens = tagntForVerse.Where(t => t.InByz).ToList();
            var na28Tokens = tagntForVerse.Where(t => t.InNa28).ToList();
            var sblTokens = tagntForVerse.Where(t => t.InSbl).ToList();

            var selectedTokens = edition switch
            {
                BibleEdition.F35 => f35Tokens,
                BibleEdition.Tr => trTokens,
                BibleEdition.Byz => byzTokens,
                BibleEdition.Na28 => na28Tokens,
                BibleEdition.Sbl => sblTokens,
                _ => tagntForVerse
            };

            if (selectedTokens.Count == 0 && f35Tokens.Count == 0 && tagntForVerse.Count == 0)
                continue;

            var f35Text = f35VerseTexts.GetValueOrDefault(v) ?? ReconstructGreek(f35Tokens);
            var trText = ReconstructGreek(trTokens);
            var byzText = ReconstructGreek(byzTokens);
            var na28Text = ReconstructGreek(na28Tokens);

            var selectedText = edition switch
            {
                BibleEdition.F35 => f35Text,
                BibleEdition.Tr => trText,
                BibleEdition.Byz => byzText,
                BibleEdition.Na28 => na28Text,
                BibleEdition.Sbl => ReconstructGreek(sblTokens),
                _ => ReconstructGreek(tagntForVerse)
            };

            VerseEditionComparison? comparison = null;
            if (includeVariants)
            {
                var f35Sig = Signature(f35Tokens);
                var trSig = Signature(trTokens);
                var byzSig = Signature(byzTokens);
                var na28Sig = Signature(na28Tokens);
                var primarySig = Signature(selectedTokens);

                comparison = new VerseEditionComparison(
                    Verse: v,
                    F35Text: string.IsNullOrWhiteSpace(f35Text) ? null : f35Text,
                    TrText: string.IsNullOrWhiteSpace(trText) ? null : trText,
                    ByzText: string.IsNullOrWhiteSpace(byzText) ? null : byzText,
                    Na28Text: string.IsNullOrWhiteSpace(na28Text) ? null : na28Text,
                    F35DiffersFromTr: !string.Equals(f35Sig, trSig, StringComparison.Ordinal),
                    TrDiffersFromByz: !string.Equals(trSig, byzSig, StringComparison.Ordinal),
                    PrimaryDiffersFromNa28: !string.Equals(primarySig, na28Sig, StringComparison.Ordinal));
            }

            blocks.Add(new ParsedVerseBlock(
                reference.Chapter, v, selectedText, IsOldTestament: false, HebrewRef: null,
                selectedTokens, comparison, MasoreticNotes: []));
        }

        return blocks;
    }

    private static string ReconstructGreek(IReadOnlyList<VerseTokenView> tokens)
    {
        if (tokens.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        for (int i = 0; i < tokens.Count; i++)
        {
            if (i > 0) sb.Append(' ');
            var t = tokens[i];
            if (!string.IsNullOrEmpty(t.PunctuationBefore))
                sb.Append(t.PunctuationBefore);
            sb.Append(t.SurfaceForm);
            if (!string.IsNullOrEmpty(t.PunctuationAfter))
                sb.Append(t.PunctuationAfter);
        }
        return sb.ToString();
    }

    private static string ReconstructHebrew(IReadOnlyList<VerseTokenView> tokens, short? highlightPosition = null)
    {
        if (tokens.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        bool prevEndedWithMaqqef = false;

        for (int i = 0; i < tokens.Count; i++)
        {
            if (i > 0 && !prevEndedWithMaqqef)
                sb.Append(' ');

            var t = tokens[i];
            if (highlightPosition.HasValue && t.WordPosition == highlightPosition.Value)
                sb.Append("**").Append(t.SurfaceForm).Append("**");
            else
                sb.Append(t.SurfaceForm);

            if (!string.IsNullOrEmpty(t.PunctuationAfter))
            {
                sb.Append(t.PunctuationAfter);
                prevEndedWithMaqqef = t.PunctuationAfter.EndsWith('\u05BE'); // maqqef ־ joins words without space
            }
            else
            {
                prevEndedWithMaqqef = false;
            }
        }
        return sb.ToString();
    }

    private static string Signature(IReadOnlyList<VerseTokenView> tokens) =>
        string.Join(' ', tokens.Select(t => t.SurfaceNormalized));
}
