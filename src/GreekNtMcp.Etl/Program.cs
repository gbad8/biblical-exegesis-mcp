using System.Diagnostics;
using Dapper;
using GreekNtMcp.Domain.Models;
using GreekNtMcp.Domain.Normalization;
using GreekNtMcp.Domain.References;
using GreekNtMcp.Etl.Download;
using GreekNtMcp.Etl.Parsers;
using GreekNtMcp.Infrastructure.Database;
using Npgsql;
using NpgsqlTypes;

var sw = Stopwatch.StartNew();
var forceDownload = args.Contains("--force-download", StringComparer.OrdinalIgnoreCase);

var dataDir = Environment.GetEnvironmentVariable("ETL_DATA_DIR") ?? Path.Combine(AppContext.BaseDirectory, "data", "raw");
var manifestPath = Path.Combine(AppContext.BaseDirectory, "datasets.json");
var schemaPath = Environment.GetEnvironmentVariable("ETL_SCHEMA_PATH") ?? "/app/schema/001_schema.sql";

Console.WriteLine("=== Greek NT Exegesis MCP — Pipeline ETL (.NET 10) ===");
Console.WriteLine($"[ETL] Diretório de cache: {dataDir}");

var manifest = DatasetDownloader.LoadManifest(manifestPath);
var downloader = new DatasetDownloader(dataDir);
var downloaded = await downloader.EnsureAllAsync(manifest, forceDownload);

await using var dataSource = PostgresConnectionFactory.CreateDataSource();
await using var conn = await dataSource.OpenConnectionAsync();

// 1. Ensure schema exists
if (File.Exists(schemaPath))
{
    Console.WriteLine($"[ETL] Aplicando schema idempotente de {schemaPath}...");
    var schemaSql = await File.ReadAllTextAsync(schemaPath);
    await conn.ExecuteAsync(schemaSql);
}

// 2. Parse all datasets in memory before writing (fail-fast before touching DB tables)
Console.WriteLine("[ETL] Parseando strongsgreek.xml...");
var strongEntries = StrongsGreekParser.ParseFile(downloaded["strongs_greek"].LocalPath);
Console.WriteLine($"[ETL]   -> {strongEntries.Count:N0} verbetes Strong.");

Console.WriteLine("[ETL] Parseando STEPBible TBESG...");
var tbesgRows = StepBibleLexiconParser.ParseTbesg(downloaded["stepbible_tbesg"].LocalPath);
Console.WriteLine($"[ETL]   -> {tbesgRows.Count:N0} entradas TBESG.");

Console.WriteLine("[ETL] Parseando Abbott-Smith TEI XML...");
var abbottEntries = AbbottSmithParser.ParseFile(downloaded["abbott_smith"].LocalPath);
Console.WriteLine($"[ETL]   -> {abbottEntries.Count:N0} verbetes Abbott-Smith.");

Console.WriteLine("[ETL] Parseando STEPBible TFLSJ (0-5624 + extra)...");
var lsjMain = StepBibleLexiconParser.ParseTflsj(downloaded["stepbible_tflsj_main"].LocalPath);
var lsjExtra = StepBibleLexiconParser.ParseTflsj(downloaded["stepbible_tflsj_extra"].LocalPath);
var allLsj = lsjMain.Concat(lsjExtra).ToList();
Console.WriteLine($"[ETL]   -> {allLsj.Count:N0} verbetes LSJ ({lsjMain.Count:N0} principais + {lsjExtra.Count:N0} extras).");

Console.WriteLine("[ETL] Parseando STEPBible TAGNT (Mat-Jhn + Act-Rev)...");
var tagntMatJhn = TagntParser.ParseFile(downloaded["stepbible_tagnt_mat_jhn"].LocalPath);
var tagntActRev = TagntParser.ParseFile(downloaded["stepbible_tagnt_act_rev"].LocalPath);
var allTagnt = tagntMatJhn.Concat(tagntActRev).ToList();
Console.WriteLine($"[ETL]   -> {allTagnt.Count:N0} tokens TAGNT (TR={allTagnt.Count(t => t.InTr):N0}, Byz={allTagnt.Count(t => t.InByz):N0}, NA28={allTagnt.Count(t => t.InNa28):N0}).");

Console.WriteLine("[ETL] Parseando e alinhando Família 35 (eBible grcf35_vpl.zip)...");
var (f35Verses, f35Tokens, f35Stats) = Family35ParserAndAligner.ParseAndAlignFromZip(
    downloaded["ebible_grcf35"].LocalPath, allTagnt);
var alignedPct = f35Stats.TotalTokens > 0
    ? 100.0 * (f35Stats.ExactVerseMatches + f35Stats.UnambiguousFormMatches) / f35Stats.TotalTokens
    : 0;
Console.WriteLine(
    $"[ETL]   -> {f35Stats.TotalVerses:N0} versículos F35, {f35Stats.TotalTokens:N0} tokens " +
    $"(exact={f35Stats.ExactVerseMatches:N0}, form={f35Stats.UnambiguousFormMatches:N0}, untagged={f35Stats.UntaggedVariants:N0} | alinhado={alignedPct:F2}%).");

// Merge Strong + TBESG into master lexicon dictionary keyed by StrongCode
var mergedLexicon = MergeLexicons(strongEntries, tbesgRows);
Console.WriteLine($"[ETL] Léxico unificado (Strong + TBESG): {mergedLexicon.Count:N0} entradas.");

// Sanity checks before truncating database
if (strongEntries.Count < 5500)
    throw new InvalidOperationException($"Sanity check falhou: apenas {strongEntries.Count} entradas Strong (esperado >= 5500).");
if (abbottEntries.Count < 4500)
    throw new InvalidOperationException($"Sanity check falhou: apenas {abbottEntries.Count} verbetes Abbott-Smith (esperado >= 4500).");
if (allTagnt.Count < 135000)
    throw new InvalidOperationException($"Sanity check falhou: apenas {allTagnt.Count} tokens TAGNT (esperado >= 135000).");
if (f35Tokens.Count < 130000)
    throw new InvalidOperationException($"Sanity check falhou: apenas {f35Tokens.Count} tokens Família 35 (esperado >= 130000).");

// 3. Load everything inside a transaction using fast PostgreSQL binary COPY
Console.WriteLine("[ETL] Gravando dados no PostgreSQL via binary COPY...");
await using var tx = await conn.BeginTransactionAsync();

await conn.ExecuteAsync(
    "TRUNCATE TABLE f35_tokens, f35_verses, nt_tokens, lexicon_lsj, lexicon_abbott_smith, lexicon_entries, bible_books, etl_metadata RESTART IDENTITY CASCADE;",
    transaction: tx);

// Seed bible_books
foreach (var b in BibleBookCatalog.NewTestamentBooks)
{
    await conn.ExecuteAsync(
        """
        INSERT INTO bible_books (book_id, osis_code, usfm_code, name_en, name_pt, aliases)
        VALUES (@BookId, @OsisCode, @UsfmCode, @NameEn, @NamePt, @Aliases);
        """,
        b, transaction: tx);
}

// COPY lexicon_entries
await using (var writer = await conn.BeginBinaryImportAsync(
    """
    COPY lexicon_entries (
        strong_code, base_strong_code, strong_number, is_extended,
        lemma, lemma_normalized, transliteration, transliteration_normalized,
        pos_code, short_gloss, tbesg_meaning,
        strong_derivation, strong_definition, strong_kjv_definition
    ) FROM STDIN (FORMAT BINARY)
    """))
{
    foreach (var e in mergedLexicon.Values)
    {
        await writer.StartRowAsync();
        await writer.WriteAsync(e.StrongCode, NpgsqlDbType.Varchar);
        await writer.WriteAsync(e.BaseStrongCode, NpgsqlDbType.Varchar);
        await writer.WriteAsync(e.StrongNumber, NpgsqlDbType.Integer);
        await writer.WriteAsync(e.IsExtended, NpgsqlDbType.Boolean);
        await writer.WriteAsync(e.Lemma, NpgsqlDbType.Text);
        await writer.WriteAsync(e.LemmaNormalized, NpgsqlDbType.Text);
        await writer.WriteAsync(e.Transliteration, NpgsqlDbType.Text);
        await writer.WriteAsync(e.TransliterationNormalized, NpgsqlDbType.Text);
        await writer.WriteAsync(e.PosCode, NpgsqlDbType.Varchar);
        await writer.WriteAsync(e.ShortGloss, NpgsqlDbType.Text);
        await writer.WriteAsync(e.TbesgMeaning, NpgsqlDbType.Text);
        await writer.WriteAsync(e.StrongDerivation, NpgsqlDbType.Text);
        await writer.WriteAsync(e.StrongDefinition, NpgsqlDbType.Text);
        await writer.WriteAsync(e.StrongKjvDefinition, NpgsqlDbType.Text);
    }
    await writer.CompleteAsync();
}

// COPY lexicon_abbott_smith
await using (var writer = await conn.BeginBinaryImportAsync(
    """
    COPY lexicon_abbott_smith (
        strong_code, headword, headword_normalized, grammatical_info,
        etymology, lxx_equivalents, synonyms, senses_json, full_markdown
    ) FROM STDIN (FORMAT BINARY)
    """))
{
    foreach (var a in abbottEntries)
    {
        await writer.StartRowAsync();
        await writer.WriteAsync(a.StrongCode, NpgsqlDbType.Varchar);
        await writer.WriteAsync(a.Headword, NpgsqlDbType.Text);
        await writer.WriteAsync(a.HeadwordNormalized, NpgsqlDbType.Text);
        await writer.WriteAsync(a.GrammaticalInfo, NpgsqlDbType.Text);
        await writer.WriteAsync(a.Etymology, NpgsqlDbType.Text);
        await writer.WriteAsync(a.LxxEquivalents, NpgsqlDbType.Array | NpgsqlDbType.Text);
        await writer.WriteAsync(a.Synonyms, NpgsqlDbType.Array | NpgsqlDbType.Text);
        if (a.SensesJson is not null)
            await writer.WriteAsync(a.SensesJson, NpgsqlDbType.Jsonb);
        else
            await writer.WriteNullAsync();
        await writer.WriteAsync(a.FullMarkdown, NpgsqlDbType.Text);
    }
    await writer.CompleteAsync();
}

// COPY lexicon_lsj
await using (var writer = await conn.BeginBinaryImportAsync(
    """
    COPY lexicon_lsj (
        strong_code, base_strong_code, headword, headword_normalized,
        summary_markdown, full_markdown, char_length
    ) FROM STDIN (FORMAT BINARY)
    """))
{
    foreach (var l in allLsj)
    {
        await writer.StartRowAsync();
        await writer.WriteAsync(l.StrongCode, NpgsqlDbType.Varchar);
        await writer.WriteAsync(l.BaseStrongCode, NpgsqlDbType.Varchar);
        await writer.WriteAsync(l.Headword, NpgsqlDbType.Text);
        await writer.WriteAsync(l.HeadwordNormalized, NpgsqlDbType.Text);
        await writer.WriteAsync(l.SummaryMarkdown, NpgsqlDbType.Text);
        await writer.WriteAsync(l.FullMarkdown, NpgsqlDbType.Text);
        await writer.WriteAsync(l.CharLength, NpgsqlDbType.Integer);
    }
    await writer.CompleteAsync();
}

// COPY nt_tokens
await using (var writer = await conn.BeginBinaryImportAsync(
    """
    COPY nt_tokens (
        book_id, chapter, verse, word_position, sub_position,
        surface_form, surface_normalized, punctuation_before, punctuation_after,
        transliteration, contextual_gloss,
        strong_code, base_strong_code, secondary_strong_codes,
        lemma, lemma_normalized, morph_code,
        edition_group, editions_raw,
        in_tr, in_byz, in_na28, in_sbl, variant_note
    ) FROM STDIN (FORMAT BINARY)
    """))
{
    foreach (var t in allTagnt)
    {
        await writer.StartRowAsync();
        await writer.WriteAsync(t.BookId, NpgsqlDbType.Smallint);
        await writer.WriteAsync(t.Chapter, NpgsqlDbType.Smallint);
        await writer.WriteAsync(t.Verse, NpgsqlDbType.Smallint);
        await writer.WriteAsync(t.WordPosition, NpgsqlDbType.Smallint);
        await writer.WriteAsync(t.SubPosition, NpgsqlDbType.Smallint);
        await writer.WriteAsync(t.SurfaceForm, NpgsqlDbType.Text);
        await writer.WriteAsync(t.SurfaceNormalized, NpgsqlDbType.Text);
        await writer.WriteAsync(t.PunctuationBefore, NpgsqlDbType.Text);
        await writer.WriteAsync(t.PunctuationAfter, NpgsqlDbType.Text);
        await writer.WriteAsync(t.Transliteration, NpgsqlDbType.Text);
        await writer.WriteAsync(t.ContextualGloss, NpgsqlDbType.Text);
        await writer.WriteAsync(t.StrongCode, NpgsqlDbType.Varchar);
        await writer.WriteAsync(t.BaseStrongCode, NpgsqlDbType.Varchar);
        await writer.WriteAsync(t.SecondaryStrongCodes, NpgsqlDbType.Array | NpgsqlDbType.Varchar);
        await writer.WriteAsync(t.Lemma, NpgsqlDbType.Text);
        await writer.WriteAsync(t.LemmaNormalized, NpgsqlDbType.Text);
        await writer.WriteAsync(t.MorphCode, NpgsqlDbType.Varchar);
        await writer.WriteAsync(t.EditionGroup, NpgsqlDbType.Varchar);
        await writer.WriteAsync(t.EditionsRaw, NpgsqlDbType.Text);
        await writer.WriteAsync(t.InTr, NpgsqlDbType.Boolean);
        await writer.WriteAsync(t.InByz, NpgsqlDbType.Boolean);
        await writer.WriteAsync(t.InNa28, NpgsqlDbType.Boolean);
        await writer.WriteAsync(t.InSbl, NpgsqlDbType.Boolean);
        await writer.WriteAsync(t.VariantNote, NpgsqlDbType.Text);
    }
    await writer.CompleteAsync();
}

// COPY f35_verses
await using (var writer = await conn.BeginBinaryImportAsync(
    "COPY f35_verses (book_id, chapter, verse, verse_text) FROM STDIN (FORMAT BINARY)"))
{
    foreach (var v in f35Verses)
    {
        await writer.StartRowAsync();
        await writer.WriteAsync(v.BookId, NpgsqlDbType.Smallint);
        await writer.WriteAsync(v.Chapter, NpgsqlDbType.Smallint);
        await writer.WriteAsync(v.Verse, NpgsqlDbType.Smallint);
        await writer.WriteAsync(v.VerseText, NpgsqlDbType.Text);
    }
    await writer.CompleteAsync();
}

// COPY f35_tokens
await using (var writer = await conn.BeginBinaryImportAsync(
    """
    COPY f35_tokens (
        book_id, chapter, verse, word_position,
        surface_form, surface_normalized, punctuation_before, punctuation_after,
        transliteration, contextual_gloss,
        strong_code, base_strong_code, secondary_strong_codes,
        lemma, lemma_normalized, morph_code, alignment_status
    ) FROM STDIN (FORMAT BINARY)
    """))
{
    foreach (var t in f35Tokens)
    {
        await writer.StartRowAsync();
        await writer.WriteAsync(t.BookId, NpgsqlDbType.Smallint);
        await writer.WriteAsync(t.Chapter, NpgsqlDbType.Smallint);
        await writer.WriteAsync(t.Verse, NpgsqlDbType.Smallint);
        await writer.WriteAsync(t.WordPosition, NpgsqlDbType.Smallint);
        await writer.WriteAsync(t.SurfaceForm, NpgsqlDbType.Text);
        await writer.WriteAsync(t.SurfaceNormalized, NpgsqlDbType.Text);
        await writer.WriteAsync(t.PunctuationBefore, NpgsqlDbType.Text);
        await writer.WriteAsync(t.PunctuationAfter, NpgsqlDbType.Text);
        await writer.WriteAsync(t.Transliteration, NpgsqlDbType.Text);
        await writer.WriteAsync(t.ContextualGloss, NpgsqlDbType.Text);
        await writer.WriteAsync(t.StrongCode, NpgsqlDbType.Varchar);
        await writer.WriteAsync(t.BaseStrongCode, NpgsqlDbType.Varchar);
        await writer.WriteAsync(t.SecondaryStrongCodes, NpgsqlDbType.Array | NpgsqlDbType.Varchar);
        await writer.WriteAsync(t.Lemma, NpgsqlDbType.Text);
        await writer.WriteAsync(t.LemmaNormalized, NpgsqlDbType.Text);
        await writer.WriteAsync(t.MorphCode, NpgsqlDbType.Varchar);
        await writer.WriteAsync(t.AlignmentStatus, NpgsqlDbType.Varchar);
    }
    await writer.CompleteAsync();
}

// 4. Populate FTS search_vector and occurrence counters per edition
Console.WriteLine("[ETL] Atualizando search_vector e contadores de ocorrências (F35, TR, Byz, NA28)...");
await conn.ExecuteAsync(
    """
    UPDATE lexicon_entries
    SET search_vector =
        setweight(to_tsvector('english', COALESCE(short_gloss, '')), 'A') ||
        setweight(to_tsvector('english', COALESCE(strong_kjv_definition, '')), 'B') ||
        setweight(to_tsvector('english', COALESCE(strong_definition, '')), 'C');

    UPDATE lexicon_entries l
    SET occurrences_f35 = sub.cnt
    FROM (
        SELECT base_strong_code, COUNT(*)::int AS cnt
        FROM f35_tokens
        WHERE base_strong_code IS NOT NULL
        GROUP BY base_strong_code
    ) sub
    WHERE l.strong_code = sub.base_strong_code;

    UPDATE lexicon_entries l
    SET occurrences_f35 = sub.cnt
    FROM (
        SELECT strong_code, COUNT(*)::int AS cnt
        FROM f35_tokens
        WHERE strong_code IS NOT NULL
        GROUP BY strong_code
    ) sub
    WHERE l.is_extended = TRUE AND l.strong_code = sub.strong_code;

    UPDATE lexicon_entries l
    SET occurrences_tr = sub.tr_cnt,
        occurrences_byz = sub.byz_cnt,
        occurrences_na28 = sub.na28_cnt
    FROM (
        SELECT
            base_strong_code,
            COUNT(*) FILTER (WHERE in_tr)::int   AS tr_cnt,
            COUNT(*) FILTER (WHERE in_byz)::int  AS byz_cnt,
            COUNT(*) FILTER (WHERE in_na28)::int AS na28_cnt
        FROM nt_tokens
        WHERE base_strong_code IS NOT NULL
        GROUP BY base_strong_code
    ) sub
    WHERE l.strong_code = sub.base_strong_code;

    UPDATE lexicon_entries l
    SET occurrences_tr = sub.tr_cnt,
        occurrences_byz = sub.byz_cnt,
        occurrences_na28 = sub.na28_cnt
    FROM (
        SELECT
            strong_code,
            COUNT(*) FILTER (WHERE in_tr)::int   AS tr_cnt,
            COUNT(*) FILTER (WHERE in_byz)::int  AS byz_cnt,
            COUNT(*) FILTER (WHERE in_na28)::int AS na28_cnt
        FROM nt_tokens
        WHERE strong_code IS NOT NULL
        GROUP BY strong_code
    ) sub
    WHERE l.is_extended = TRUE AND l.strong_code = sub.strong_code;
    """,
    transaction: tx);

// 5. Write audit metadata
var auditRows = new[]
{
    ("strongs_greek", strongEntries.Count, (string?)null),
    ("stepbible_tbesg", tbesgRows.Count, (string?)null),
    ("stepbible_tflsj_main", lsjMain.Count, (string?)null),
    ("stepbible_tflsj_extra", lsjExtra.Count, (string?)null),
    ("stepbible_tagnt_mat_jhn", tagntMatJhn.Count, (string?)null),
    ("stepbible_tagnt_act_rev", tagntActRev.Count, (string?)null),
    ("abbott_smith", abbottEntries.Count, (string?)null),
    ("ebible_grcf35", f35Tokens.Count,
        $"verses={f35Stats.TotalVerses};exact={f35Stats.ExactVerseMatches};form={f35Stats.UnambiguousFormMatches};untagged={f35Stats.UntaggedVariants}")
};

foreach (var (name, count, notes) in auditRows)
{
    var d = downloaded[name];
    await conn.ExecuteAsync(
        """
        INSERT INTO etl_metadata (source_name, source_url, version_ref, file_sha256, record_count, notes)
        VALUES (@Name, @Url, @VersionRef, @Sha256, @Count, @Notes);
        """,
        new { Name = name, d.Source.Url, d.Source.VersionRef, Sha256 = d.Sha256Hex, Count = count, Notes = notes },
        transaction: tx);
}

await tx.CommitAsync();

Console.WriteLine("[ETL] Executando ANALYZE nas tabelas...");
await conn.ExecuteAsync("ANALYZE lexicon_entries, lexicon_abbott_smith, lexicon_lsj, nt_tokens, f35_verses, f35_tokens;");

sw.Stop();
Console.WriteLine($"=== ETL concluído com sucesso em {sw.Elapsed.TotalSeconds:F1}s! ===");

static Dictionary<string, LexiconEntry> MergeLexicons(
    List<LexiconEntry> strongEntries,
    List<TbesgRow> tbesgRows)
{
    var map = new Dictionary<string, LexiconEntry>(StringComparer.OrdinalIgnoreCase);

    foreach (var s in strongEntries)
        map[s.StrongCode] = s;

    foreach (var t in tbesgRows)
    {
        if (map.TryGetValue(t.StrongCode, out var existing))
        {
            if (string.IsNullOrWhiteSpace(existing.Lemma) && !string.IsNullOrWhiteSpace(t.Lemma))
            {
                existing.Lemma = t.Lemma;
                existing.LemmaNormalized = t.LemmaNormalized;
            }
            if (!string.IsNullOrWhiteSpace(t.Transliteration))
            {
                existing.Transliteration = t.Transliteration;
                existing.TransliterationNormalized = t.TransliterationNormalized;
            }
            existing.PosCode = t.PosCode ?? existing.PosCode;
            existing.ShortGloss = t.ShortGloss ?? existing.ShortGloss;
            existing.TbesgMeaning = t.MeaningMarkdown ?? existing.TbesgMeaning;
        }
        else
        {
            // Inherit Strong's derivation/definition from base Strong if this is a STEPBible disambiguated sub-code (e.g. G2264G)
            map.TryGetValue(t.BaseStrongCode, out var baseEntry);

            map[t.StrongCode] = new LexiconEntry
            {
                StrongCode = t.StrongCode,
                BaseStrongCode = t.BaseStrongCode,
                StrongNumber = t.StrongNumber,
                IsExtended = t.IsExtended,
                Lemma = t.Lemma,
                LemmaNormalized = t.LemmaNormalized,
                Transliteration = t.Transliteration ?? baseEntry?.Transliteration,
                TransliterationNormalized = t.TransliterationNormalized ?? baseEntry?.TransliterationNormalized,
                PosCode = t.PosCode,
                ShortGloss = t.ShortGloss,
                TbesgMeaning = t.MeaningMarkdown,
                StrongDerivation = baseEntry?.StrongDerivation,
                StrongDefinition = baseEntry?.StrongDefinition,
                StrongKjvDefinition = baseEntry?.StrongKjvDefinition
            };
        }
    }

    // Backfill TBESG gloss/meaning onto base Strong if STEPBible split the word into G0001G/G0001H without a plain G0001 row
    foreach (var t in tbesgRows)
    {
        if (t.StrongCode != t.BaseStrongCode && map.TryGetValue(t.BaseStrongCode, out var baseEntry))
        {
            baseEntry.ShortGloss ??= t.ShortGloss;
            baseEntry.PosCode ??= t.PosCode;
            baseEntry.TbesgMeaning ??= t.MeaningMarkdown;
        }
    }

    return map;
}
