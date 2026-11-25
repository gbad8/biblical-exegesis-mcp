using System.Text;
using Dapper;
using GreekNtMcp.Domain.Models;
using GreekNtMcp.Domain.Normalization;
using GreekNtMcp.Domain.References;
using Npgsql;

namespace GreekNtMcp.Infrastructure.Repositories;

public sealed record HealthStatus(
    bool DatabaseConnected,
    int LexiconCount,
    int AbbottSmithCount,
    int LsjCount,
    int TagntTokenCount,
    int F35TokenCount,
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
    public int Nt { get; set; }
    public int F35 { get; set; }
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
                (SELECT COUNT(*)::int FROM nt_tokens)            AS nt,
                (SELECT COUNT(*)::int FROM f35_tokens)           AS f35;
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
            TagntTokenCount: counts.Nt,
            F35TokenCount: counts.F35,
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

        var abbottSmith = (await conn.QueryAsync<AbbottSmithEntry>(
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

        var lsj = (await conn.QueryAsync<LsjEntry>(
            """
            SELECT *
            FROM lexicon_lsj
            WHERE strong_code = @Exact
               OR base_strong_code = @Base
            ORDER BY (strong_code = @Exact) DESC, id ASC;
            """,
            new { Exact = primary.StrongCode, Base = primary.BaseStrongCode })).ToList();

        return new LookupStrongResult(primary, subEntries, abbottSmith, lsj);
    }

    public async Task<IReadOnlyList<SearchLemmaHit>> SearchLemmaAsync(
        string query,
        bool fuzzy = true,
        int limit = 15,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        limit = Math.Clamp(limit, 1, 50);
        var trimmed = query.Trim();
        var isGreek = GreekNormalizer.ContainsGreek(trimmed);
        var greekNorm = GreekNormalizer.NormalizeGreek(trimmed);
        var latinNorm = GreekNormalizer.NormalizeLatin(trimmed);
        var strongParts = StrongCodeNormalizer.TryNormalize(trimmed);

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        const string sql = """
            WITH ranked AS (
                SELECT
                    strong_code,
                    base_strong_code,
                    lemma,
                    transliteration,
                    pos_code,
                    short_gloss,
                    occurrences_f35,
                    occurrences_tr,
                    CASE
                        WHEN @StrongExact IS NOT NULL AND (strong_code = @StrongExact OR base_strong_code = @StrongBase) THEN 'exact_strong'
                        WHEN @IsGreek AND lemma_normalized = @GreekNorm THEN 'exact_greek'
                        WHEN NOT @IsGreek AND transliteration_normalized = @LatinNorm THEN 'exact_translit'
                        WHEN @IsGreek AND lemma_normalized LIKE @GreekPrefix THEN 'prefix_greek'
                        WHEN NOT @IsGreek AND transliteration_normalized LIKE @LatinPrefix THEN 'prefix_translit'
                        WHEN NOT @IsGreek AND short_gloss ILIKE @GlossExact THEN 'exact_gloss'
                        WHEN NOT @IsGreek AND search_vector @@ websearch_to_tsquery('english', @RawQuery) THEN 'fts_gloss'
                        WHEN @Fuzzy AND @IsGreek AND similarity(lemma_normalized, @GreekNorm) >= 0.30 THEN 'fuzzy_greek'
                        WHEN @Fuzzy AND NOT @IsGreek AND similarity(COALESCE(transliteration_normalized, ''), @LatinNorm) >= 0.35 THEN 'fuzzy_translit'
                        WHEN @Fuzzy AND NOT @IsGreek AND similarity(COALESCE(short_gloss, ''), @RawQuery) >= 0.30 THEN 'fuzzy_gloss'
                        ELSE NULL
                    END AS match_type,
                    CASE
                        WHEN @StrongExact IS NOT NULL AND strong_code = @StrongExact THEN 1.00
                        WHEN @StrongExact IS NOT NULL AND base_strong_code = @StrongBase THEN 0.98
                        WHEN @IsGreek AND lemma_normalized = @GreekNorm THEN 0.99
                        WHEN NOT @IsGreek AND transliteration_normalized = @LatinNorm THEN 0.95
                        WHEN @IsGreek AND lemma_normalized LIKE @GreekPrefix THEN 0.85
                        WHEN NOT @IsGreek AND transliteration_normalized LIKE @LatinPrefix THEN 0.82
                        WHEN NOT @IsGreek AND short_gloss ILIKE @GlossExact THEN 0.80
                        WHEN NOT @IsGreek AND search_vector @@ websearch_to_tsquery('english', @RawQuery)
                            THEN 0.65 + LEAST(0.14, ts_rank(search_vector, websearch_to_tsquery('english', @RawQuery)))
                        WHEN @Fuzzy AND @IsGreek AND similarity(lemma_normalized, @GreekNorm) >= 0.30
                            THEN 0.55 * similarity(lemma_normalized, @GreekNorm)
                        WHEN @Fuzzy AND NOT @IsGreek AND similarity(COALESCE(transliteration_normalized, ''), @LatinNorm) >= 0.35
                            THEN 0.55 * similarity(COALESCE(transliteration_normalized, ''), @LatinNorm)
                        WHEN @Fuzzy AND NOT @IsGreek AND similarity(COALESCE(short_gloss, ''), @RawQuery) >= 0.30
                            THEN 0.50 * similarity(COALESCE(short_gloss, ''), @RawQuery)
                        ELSE 0.0
                    END AS score
                FROM lexicon_entries
                WHERE
                    (@StrongExact IS NOT NULL AND (strong_code = @StrongExact OR base_strong_code = @StrongBase))
                    OR (@IsGreek AND lemma_normalized LIKE @GreekPrefix)
                    OR (NOT @IsGreek AND transliteration_normalized LIKE @LatinPrefix)
                    OR (NOT @IsGreek AND short_gloss ILIKE @GlossContains)
                    OR (NOT @IsGreek AND search_vector @@ websearch_to_tsquery('english', @RawQuery))
                    OR (@Fuzzy AND @IsGreek AND lemma_normalized % @GreekNorm)
                    OR (@Fuzzy AND NOT @IsGreek AND transliteration_normalized % @LatinNorm)
                    OR (@Fuzzy AND NOT @IsGreek AND short_gloss % @RawQuery)
            )
            SELECT
                strong_code,
                base_strong_code,
                lemma,
                transliteration,
                pos_code,
                short_gloss,
                occurrences_f35,
                occurrences_tr,
                match_type,
                score
            FROM ranked
            WHERE match_type IS NOT NULL
            ORDER BY score DESC, (occurrences_f35 + occurrences_tr) DESC, strong_code ASC
            LIMIT @Limit;
            """;

        // Tune pg_trgm similarity threshold for the session if fuzzy is enabled
        if (fuzzy)
            await conn.ExecuteAsync("SET pg_trgm.similarity_threshold = 0.30;");

        var rows = await conn.QueryAsync<SearchLemmaHit>(sql, new
        {
            RawQuery = trimmed,
            IsGreek = isGreek,
            GreekNorm = greekNorm,
            GreekPrefix = greekNorm + "%",
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
        BibleEdition edition = BibleEdition.F35,
        string? bookAlias = null,
        int limit = 30,
        int offset = 0,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 100);
        offset = Math.Max(0, offset);

        short? bookIdFilter = null;
        if (!string.IsNullOrWhiteSpace(bookAlias))
        {
            var book = BibleBookCatalog.FindByAlias(bookAlias);
            if (book is null)
                throw new ArgumentException($"Livro do NT não reconhecido: '{bookAlias}'.");
            bookIdFilter = book.BookId;
        }

        var strongParts = StrongCodeNormalizer.TryNormalize(lemmaOrStrong);
        string? resolvedStrong = strongParts?.StrongCode;
        string? resolvedBaseStrong = strongParts?.BaseStrongCode;
        string? resolvedLemma = null;
        string? lemmaNorm = null;

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
        }
        else
        {
            lemmaNorm = GreekNormalizer.NormalizeGreek(lemmaOrStrong);
            var entry = await conn.QueryFirstOrDefaultAsync<LexiconEntry>(
                """
                SELECT * FROM lexicon_entries
                WHERE lemma_normalized = @LemmaNorm
                   OR transliteration_normalized = @LatinNorm
                ORDER BY is_extended ASC, (occurrences_f35 + occurrences_tr) DESC
                LIMIT 1;
                """,
                new { LemmaNorm = lemmaNorm, LatinNorm = GreekNormalizer.NormalizeLatin(lemmaOrStrong) });

            if (entry is not null)
            {
                resolvedStrong = entry.BaseStrongCode;
                resolvedBaseStrong = entry.BaseStrongCode;
                resolvedLemma = entry.Lemma;
                lemmaNorm = entry.LemmaNormalized;
            }
        }

        var label = resolvedStrong is not null && resolvedLemma is not null
            ? $"{resolvedLemma} ({resolvedStrong})"
            : resolvedStrong ?? lemmaOrStrong.Trim();

        if (edition == BibleEdition.F35)
        {
            return await QueryF35OccurrencesAsync(
                conn, label, resolvedStrong, resolvedBaseStrong, resolvedLemma, lemmaNorm,
                strongParts?.IsExtended == true, bookIdFilter, limit, offset);
        }

        return await QueryTagntOccurrencesAsync(
            conn, label, resolvedStrong, resolvedBaseStrong, resolvedLemma, lemmaNorm,
            strongParts?.IsExtended == true, edition, bookIdFilter, limit, offset);
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
        var whereClause = BuildMatchClause("t", exactExtendedStrong);
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
                t.word_position,
                t.surface_form,
                t.strong_code,
                t.morph_code,
                t.contextual_gloss,
                t.alignment_status
            FROM f35_tokens t
            JOIN bible_books b ON b.book_id = t.book_id
            WHERE {whereClause}{bookClause}
            ORDER BY t.book_id ASC, t.chapter ASC, t.verse ASC, t.word_position ASC
            LIMIT @Limit OFFSET @Offset;
            """, param)).ToList();

        var contexts = await BuildHighlightedContextsAsync(conn, hits, isF35: true, BibleEdition.F35);

        return new WordOccurrencesResult(
            label, resolvedStrong, resolvedLemma, BibleEdition.F35,
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
        var whereClause = BuildMatchClause("t", exactExtendedStrong);
        var edClause = EditionFilterSql("t", edition);
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
                t.word_position,
                t.surface_form,
                t.strong_code,
                t.morph_code,
                t.contextual_gloss,
                NULL::text AS alignment_status
            FROM nt_tokens t
            JOIN bible_books b ON b.book_id = t.book_id
            WHERE ({whereClause}){edClause}{bookClause}
            ORDER BY t.book_id ASC, t.chapter ASC, t.verse ASC, t.word_position ASC, t.sub_position ASC
            LIMIT @Limit OFFSET @Offset;
            """, param)).ToList();

        var contexts = await BuildHighlightedContextsAsync(conn, hits, isF35: false, edition);

        return new WordOccurrencesResult(
            label, resolvedStrong, resolvedLemma, edition,
            total, 0, distribution, contexts);
    }

    private static string BuildMatchClause(string alias, bool exactExtendedStrong)
    {
        if (exactExtendedStrong)
        {
            return $"({alias}.strong_code = @ExactStrong OR @ExactStrong = ANY({alias}.secondary_strong_codes))";
        }

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

    private static string EditionFilterSql(string alias, BibleEdition edition) => edition switch
    {
        BibleEdition.Tr => $" AND {alias}.in_tr = TRUE",
        BibleEdition.Byz => $" AND {alias}.in_byz = TRUE",
        BibleEdition.Na28 => $" AND {alias}.in_na28 = TRUE",
        BibleEdition.Sbl => $" AND {alias}.in_sbl = TRUE",
        _ => string.Empty
    };

    private static async Task<IReadOnlyList<WordOccurrenceContext>> BuildHighlightedContextsAsync(
        NpgsqlConnection conn,
        List<WordOccurrenceRow> hits,
        bool isF35,
        BibleEdition edition)
    {
        if (hits.Count == 0)
            return [];

        // Batch fetch tokens for all distinct verses referenced in this page
        var distinctVerses = hits
            .Select(h => (h.BookId, h.Chapter, h.Verse))
            .Distinct()
            .ToList();

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
                   WHERE t.book_id = @BookId AND t.chapter = @Chapter AND t.verse = @Verse {EditionFilterSql("t", edition)}
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
        BibleEdition edition = BibleEdition.F35,
        bool includeVariants = true,
        CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);

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
                   alignment_status
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
                   in_tr, in_byz, in_na28, in_sbl, editions_raw
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

            blocks.Add(new ParsedVerseBlock(reference.Chapter, v, selectedText, selectedTokens, comparison));
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

    private static string Signature(IReadOnlyList<VerseTokenView> tokens) =>
        string.Join(' ', tokens.Select(t => t.SurfaceNormalized));
}
