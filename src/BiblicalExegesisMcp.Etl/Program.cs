using System.Diagnostics;
using Dapper;
using BiblicalExegesisMcp.Domain.Models;
using BiblicalExegesisMcp.Domain.References;
using BiblicalExegesisMcp.Etl.Download;
using BiblicalExegesisMcp.Etl.Parsers;
using BiblicalExegesisMcp.Infrastructure.Database;
using Npgsql;
using NpgsqlTypes;

var sw = Stopwatch.StartNew();
var forceDownload = args.Contains("--force-download", StringComparer.OrdinalIgnoreCase);

var dataDir = Environment.GetEnvironmentVariable("ETL_DATA_DIR") ?? Path.Combine(AppContext.BaseDirectory, "data", "raw");
var manifestPath = Path.Combine(AppContext.BaseDirectory, "datasets.json");
var schemaDir = Environment.GetEnvironmentVariable("ETL_SCHEMA_DIR") ?? "/app/schema";

Console.WriteLine("=== Biblical Exegesis MCP — Pipeline ETL Completo (AT TM + NT Grego | .NET 10) ===");
Console.WriteLine($"[ETL] Diretório de cache: {dataDir}");

var manifest = DatasetDownloader.LoadManifest(manifestPath);
var downloader = new DatasetDownloader(dataDir);
var downloaded = await downloader.EnsureAllAsync(manifest, forceDownload);

await using var dataSource = PostgresConnectionFactory.CreateDataSource();
await using var conn = await dataSource.OpenConnectionAsync();

// 1. Apply all idempotent SQL migrations in order (001_schema.sql, 002_ot_schema.sql, ...)
if (Directory.Exists(schemaDir))
{
    foreach (var sqlFile in Directory.GetFiles(schemaDir, "*.sql").OrderBy(f => f, StringComparer.Ordinal))
    {
        Console.WriteLine($"[ETL] Aplicando migration idempotente: {Path.GetFileName(sqlFile)}...");
        var schemaSql = await File.ReadAllTextAsync(sqlFile);
        await conn.ExecuteAsync(schemaSql);
    }
}

// 2. Parse Greek + Hebrew/Aramaic lexicons
Console.WriteLine("[ETL] Parseando strongsgreek.xml...");
var strongGreek = StrongsGreekParser.ParseFile(downloaded["strongs_greek"].LocalPath);
Console.WriteLine($"[ETL]   -> {strongGreek.Count:N0} verbetes Strong Grego.");

Console.WriteLine("[ETL] Parseando STEPBible TBESG...");
var tbesgRows = StepBibleLexiconParser.ParseTbesg(downloaded["stepbible_tbesg"].LocalPath);
Console.WriteLine($"[ETL]   -> {tbesgRows.Count:N0} entradas TBESG.");

Console.WriteLine("[ETL] Parseando OpenScriptures HebrewStrong.xml...");
var strongHebrew = StrongsHebrewParser.ParseFile(downloaded["openscriptures_hebrew_strong"].LocalPath);
Console.WriteLine($"[ETL]   -> {strongHebrew.Count:N0} verbetes Strong Hebraico/Aramaico.");

Console.WriteLine("[ETL] Parseando OpenScriptures LexicalIndex.xml...");
var (indexByBdbId, indexByStrong) = BdbLexiconParser.ParseLexicalIndex(downloaded["openscriptures_lexical_index"].LocalPath);
Console.WriteLine($"[ETL]   -> {indexByStrong.Count:N0} códigos Strong mapeados no LexicalIndex.");

Console.WriteLine("[ETL] Parseando STEPBible TBESH (campos CC BY 4.0)...");
var tbeshRows = TbeshParser.ParseFile(downloaded["stepbible_tbesh"].LocalPath);
Console.WriteLine($"[ETL]   -> {tbeshRows.Count:N0} entradas TBESH.");

var mergedLexicon = MergeAllLexicons(strongGreek, tbesgRows, strongHebrew, indexByStrong, tbeshRows);
Console.WriteLine($"[ETL] Léxico mestre unificado (Grego + Hebraico/Aramaico): {mergedLexicon.Count:N0} entradas.");

// Fail-fast sanity checks on core lexicons before touching DB
if (strongGreek.Count < 5500)
    throw new InvalidOperationException($"Sanity check falhou: apenas {strongGreek.Count} Strong Grego.");
if (strongHebrew.Count < 8500)
    throw new InvalidOperationException($"Sanity check falhou: apenas {strongHebrew.Count} Strong Hebraico.");

// 3. Begin transactional full reload using binary COPY (streaming per dataset to stay light on 1 GB RAM OCI VM)
await using var tx = await conn.BeginTransactionAsync();

Console.WriteLine("[ETL] Limpando tabelas para recarga limpa e transacional...");
await conn.ExecuteAsync(
    """
    TRUNCATE TABLE
        ot_tokens,
        f35_tokens,
        f35_verses,
        nt_tokens,
        lexicon_bdb,
        lexicon_lsj,
        lexicon_abbott_smith,
        lexicon_entries,
        bible_books,
        etl_metadata
    RESTART IDENTITY CASCADE;
    """,
    transaction: tx);

// Seed all 66 Bible books (OT + NT)
foreach (var b in BibleBookCatalog.AllBooks)
{
    await conn.ExecuteAsync(
        """
        INSERT INTO bible_books (book_id, osis_code, usfm_code, name_en, name_pt, aliases, testament)
        VALUES (@BookId, @OsisCode, @UsfmCode, @NameEn, @NamePt, @Aliases, @Testament);
        """,
        b, transaction: tx);
}

// COPY lexicon_entries
Console.WriteLine("[ETL] Carregando lexicon_entries via COPY...");
await using (var writer = await conn.BeginBinaryImportAsync(
    """
    COPY lexicon_entries (
        strong_code, base_strong_code, strong_number, is_extended, language,
        lemma, lemma_normalized, root_hebrew, twot_ref,
        transliteration, transliteration_normalized,
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
        await writer.WriteAsync(e.Language, NpgsqlDbType.Varchar);
        await writer.WriteAsync(e.Lemma, NpgsqlDbType.Text);
        await writer.WriteAsync(e.LemmaNormalized, NpgsqlDbType.Text);
        await writer.WriteAsync(e.RootHebrew, NpgsqlDbType.Text);
        await writer.WriteAsync(e.TwotRef, NpgsqlDbType.Varchar);
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

int strongGreekCount = strongGreek.Count;
int tbesgCount = tbesgRows.Count;
int strongHebrewCount = strongHebrew.Count;
int lexicalIndexCount = indexByStrong.Count;
int tbeshCount = tbeshRows.Count;
mergedLexicon.Clear();
strongGreek.Clear();
tbesgRows.Clear();
strongHebrew.Clear();
tbeshRows.Clear();
GC.Collect();

// Parse & COPY Abbott-Smith
Console.WriteLine("[ETL] Parseando e carregando Abbott-Smith TEI XML...");
var abbottEntries = AbbottSmithParser.ParseFile(downloaded["abbott_smith"].LocalPath);
if (abbottEntries.Count < 4500)
    throw new InvalidOperationException($"Sanity check falhou: apenas {abbottEntries.Count} verbetes Abbott-Smith.");
int abbottCount = abbottEntries.Count;
Console.WriteLine($"[ETL]   -> {abbottCount:N0} verbetes Abbott-Smith.");

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
abbottEntries.Clear();
GC.Collect();

// Parse & COPY TFLSJ (LSJ)
Console.WriteLine("[ETL] Parseando e carregando STEPBible TFLSJ (0-5624 + extra)...");
var lsjMain = StepBibleLexiconParser.ParseTflsj(downloaded["stepbible_tflsj_main"].LocalPath);
var lsjExtra = StepBibleLexiconParser.ParseTflsj(downloaded["stepbible_tflsj_extra"].LocalPath);
int lsjMainCount = lsjMain.Count;
int lsjExtraCount = lsjExtra.Count;
Console.WriteLine($"[ETL]   -> {lsjMainCount + lsjExtraCount:N0} verbetes LSJ.");

await using (var writer = await conn.BeginBinaryImportAsync(
    """
    COPY lexicon_lsj (
        strong_code, base_strong_code, headword, headword_normalized,
        summary_markdown, full_markdown, char_length
    ) FROM STDIN (FORMAT BINARY)
    """))
{
    foreach (var l in lsjMain.Concat(lsjExtra))
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
lsjMain.Clear();
lsjExtra.Clear();
GC.Collect();

// Parse & COPY OpenScriptures Brown-Driver-Briggs (BDB)
Console.WriteLine("[ETL] Parseando e carregando OpenScriptures Brown-Driver-Briggs (BDB)...");
var bdbEntries = BdbLexiconParser.ParseBdbFile(downloaded["openscriptures_bdb"].LocalPath, indexByBdbId);
if (bdbEntries.Count < 8000)
    throw new InvalidOperationException($"Sanity check falhou: apenas {bdbEntries.Count} verbetes BDB.");
int bdbCount = bdbEntries.Count;
Console.WriteLine($"[ETL]   -> {bdbCount:N0} verbetes BDB mapeados.");

await using (var writer = await conn.BeginBinaryImportAsync(
    """
    COPY lexicon_bdb (
        bdb_id, strong_code, base_strong_code, headword, headword_normalized,
        entry_type, twot_ref, summary_markdown, full_markdown, char_length
    ) FROM STDIN (FORMAT BINARY)
    """))
{
    foreach (var b in bdbEntries)
    {
        await writer.StartRowAsync();
        await writer.WriteAsync(b.BdbId, NpgsqlDbType.Varchar);
        await writer.WriteAsync(b.StrongCode, NpgsqlDbType.Varchar);
        await writer.WriteAsync(b.BaseStrongCode, NpgsqlDbType.Varchar);
        await writer.WriteAsync(b.Headword, NpgsqlDbType.Text);
        await writer.WriteAsync(b.HeadwordNormalized, NpgsqlDbType.Text);
        await writer.WriteAsync(b.EntryType, NpgsqlDbType.Varchar);
        await writer.WriteAsync(b.TwotRef, NpgsqlDbType.Varchar);
        await writer.WriteAsync(b.SummaryMarkdown, NpgsqlDbType.Text);
        await writer.WriteAsync(b.FullMarkdown, NpgsqlDbType.Text);
        await writer.WriteAsync(b.CharLength, NpgsqlDbType.Integer);
    }
    await writer.CompleteAsync();
}
bdbEntries.Clear();
indexByBdbId.Clear();
indexByStrong.Clear();
GC.Collect();

// Parse & COPY TAGNT + Family 35 (NT)
Console.WriteLine("[ETL] Parseando STEPBible TAGNT (Mat-Jhn + Act-Rev)...");
var tagntMatJhn = TagntParser.ParseFile(downloaded["stepbible_tagnt_mat_jhn"].LocalPath);
var tagntActRev = TagntParser.ParseFile(downloaded["stepbible_tagnt_act_rev"].LocalPath);
int tagntMatJhnCount = tagntMatJhn.Count;
int tagntActRevCount = tagntActRev.Count;
var allTagnt = tagntMatJhn.Concat(tagntActRev).ToList();
if (allTagnt.Count < 135000)
    throw new InvalidOperationException($"Sanity check falhou: apenas {allTagnt.Count} tokens TAGNT.");
Console.WriteLine($"[ETL]   -> {allTagnt.Count:N0} tokens TAGNT.");

Console.WriteLine("[ETL] Parseando e alinhando Família 35 (eBible grcf35_vpl.zip)...");
var (f35Verses, f35Tokens, f35Stats) = Family35ParserAndAligner.ParseAndAlignFromZip(
    downloaded["ebible_grcf35"].LocalPath, allTagnt);
if (f35Tokens.Count < 130000)
    throw new InvalidOperationException($"Sanity check falhou: apenas {f35Tokens.Count} tokens F35.");
var alignedPct = 100.0 * (f35Stats.ExactVerseMatches + f35Stats.UnambiguousFormMatches) / f35Stats.TotalTokens;
Console.WriteLine($"[ETL]   -> {f35Stats.TotalVerses:N0} versos F35, {f35Stats.TotalTokens:N0} tokens (alinhado={alignedPct:F2}%).");

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

// Release NT token memory before streaming OT TAHOT files (1 GB RAM friendly)
tagntMatJhn.Clear();
tagntActRev.Clear();
allTagnt.Clear();
f35Verses.Clear();
f35Tokens.Clear();
GC.Collect();

// Stream & COPY all 4 STEPBible TAHOT files directly into ot_tokens
Console.WriteLine("[ETL] Streaming STEPBible TAHOT (4 arquivos do AT Massorético) para ot_tokens...");
var tahotSources = new[]
{
    "stepbible_tahot_gen_deu",
    "stepbible_tahot_jos_est",
    "stepbible_tahot_job_sng",
    "stepbible_tahot_isa_mal"
};
var tahotCounts = new Dictionary<string, int>();
int totalOtTokens = 0;

await using (var writer = await conn.BeginBinaryImportAsync(
    """
    COPY ot_tokens (
        book_id, chapter, verse, hebrew_ref, word_position, sub_position,
        surface_form, segmented_form, surface_normalized, punctuation_after,
        transliteration, contextual_gloss,
        strong_code, base_strong_code, prefix_suffix_strongs,
        lemma, lemma_normalized, morph_code, language,
        source_type, in_mt, in_ketiv, variant_note
    ) FROM STDIN (FORMAT BINARY)
    """))
{
    foreach (var srcName in tahotSources)
    {
        int count = 0;
        foreach (var t in TahotParser.StreamFile(downloaded[srcName].LocalPath))
        {
            await writer.StartRowAsync();
            await writer.WriteAsync(t.BookId, NpgsqlDbType.Smallint);
            await writer.WriteAsync(t.Chapter, NpgsqlDbType.Smallint);
            await writer.WriteAsync(t.Verse, NpgsqlDbType.Smallint);
            await writer.WriteAsync(t.HebrewRef, NpgsqlDbType.Varchar);
            await writer.WriteAsync(t.WordPosition, NpgsqlDbType.Smallint);
            await writer.WriteAsync(t.SubPosition, NpgsqlDbType.Smallint);
            await writer.WriteAsync(t.SurfaceForm, NpgsqlDbType.Text);
            await writer.WriteAsync(t.SegmentedForm, NpgsqlDbType.Text);
            await writer.WriteAsync(t.SurfaceNormalized, NpgsqlDbType.Text);
            await writer.WriteAsync(t.PunctuationAfter, NpgsqlDbType.Text);
            await writer.WriteAsync(t.Transliteration, NpgsqlDbType.Text);
            await writer.WriteAsync(t.ContextualGloss, NpgsqlDbType.Text);
            await writer.WriteAsync(t.StrongCode, NpgsqlDbType.Varchar);
            await writer.WriteAsync(t.BaseStrongCode, NpgsqlDbType.Varchar);
            await writer.WriteAsync(t.PrefixSuffixStrongs, NpgsqlDbType.Array | NpgsqlDbType.Varchar);
            await writer.WriteAsync(t.Lemma, NpgsqlDbType.Text);
            await writer.WriteAsync(t.LemmaNormalized, NpgsqlDbType.Text);
            await writer.WriteAsync(t.MorphCode, NpgsqlDbType.Varchar);
            await writer.WriteAsync(t.Language, NpgsqlDbType.Varchar);
            await writer.WriteAsync(t.SourceType, NpgsqlDbType.Varchar);
            await writer.WriteAsync(t.InMt, NpgsqlDbType.Boolean);
            await writer.WriteAsync(t.InKetiv, NpgsqlDbType.Boolean);
            await writer.WriteAsync(t.VariantNote, NpgsqlDbType.Text);
            count++;
        }
        tahotCounts[srcName] = count;
        totalOtTokens += count;
        Console.WriteLine($"[ETL]   -> {srcName}: {count:N0} tokens.");
    }
    await writer.CompleteAsync();
}

if (totalOtTokens < 300000)
    throw new InvalidOperationException($"Sanity check falhou: apenas {totalOtTokens:N0} tokens TAHOT (esperado >= 300.000 palavras massoréticas).");
Console.WriteLine($"[ETL]   -> Total TAHOT: {totalOtTokens:N0} tokens.");

// 4. Populate FTS search_vector and occurrence counters (NT + OT)
Console.WriteLine("[ETL] Atualizando search_vector e contadores de ocorrências (F35, TR, Byz, NA28, MT)...");
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

    UPDATE lexicon_entries l
    SET occurrences_mt = sub.cnt
    FROM (
        SELECT base_strong_code, COUNT(*)::int AS cnt
        FROM ot_tokens
        WHERE in_mt = TRUE AND base_strong_code IS NOT NULL
        GROUP BY base_strong_code
    ) sub
    WHERE l.strong_code = sub.base_strong_code;

    UPDATE lexicon_entries l
    SET occurrences_mt = sub.cnt
    FROM (
        SELECT strong_code, COUNT(*)::int AS cnt
        FROM ot_tokens
        WHERE in_mt = TRUE AND strong_code IS NOT NULL
        GROUP BY strong_code
    ) sub
    WHERE l.is_extended = TRUE AND l.strong_code = sub.strong_code;
    """,
    transaction: tx, commandTimeout: 180);

// 5. Write audit metadata for all 16 sources
var auditRows = new List<(string Name, int Count, string? Notes)>
{
    ("strongs_greek", strongGreekCount, null),
    ("stepbible_tbesg", tbesgCount, null),
    ("stepbible_tflsj_main", lsjMainCount, null),
    ("stepbible_tflsj_extra", lsjExtraCount, null),
    ("stepbible_tagnt_mat_jhn", tagntMatJhnCount, null),
    ("stepbible_tagnt_act_rev", tagntActRevCount, null),
    ("abbott_smith", abbottCount, null),
    ("ebible_grcf35", f35Stats.TotalTokens,
        $"verses={f35Stats.TotalVerses};exact={f35Stats.ExactVerseMatches};form={f35Stats.UnambiguousFormMatches};untagged={f35Stats.UntaggedVariants}"),
    ("openscriptures_hebrew_strong", strongHebrewCount, null),
    ("openscriptures_lexical_index", lexicalIndexCount, null),
    ("openscriptures_bdb", bdbCount, null),
    ("stepbible_tbesh", tbeshCount, "CC BY 4.0 metadata only (dStrong/lemma/translit/morph/gloss)")
};
foreach (var (name, cnt) in tahotCounts)
    auditRows.Add((name, cnt, null));

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
await conn.ExecuteAsync(
    "ANALYZE lexicon_entries, lexicon_abbott_smith, lexicon_lsj, lexicon_bdb, nt_tokens, f35_verses, f35_tokens, ot_tokens;",
    commandTimeout: 120);

sw.Stop();
Console.WriteLine($"=== ETL Completo (AT + NT) concluído com sucesso em {sw.Elapsed.TotalSeconds:F1}s! ===");

static Dictionary<string, LexiconEntry> MergeAllLexicons(
    List<LexiconEntry> strongGreek,
    List<TbesgRow> tbesgRows,
    List<LexiconEntry> strongHebrew,
    Dictionary<string, LexicalIndexMetadata> lexicalIndex,
    List<TbeshRow> tbeshRows)
{
    var map = new Dictionary<string, LexiconEntry>(StringComparer.OrdinalIgnoreCase);

    // 1. Greek Strong + TBESG
    foreach (var s in strongGreek)
    {
        s.Language = "grc";
        map[s.StrongCode] = s;
    }

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
            map.TryGetValue(t.BaseStrongCode, out var baseEntry);
            map[t.StrongCode] = new LexiconEntry
            {
                StrongCode = t.StrongCode,
                BaseStrongCode = t.BaseStrongCode,
                StrongNumber = t.StrongNumber,
                IsExtended = t.IsExtended,
                Language = "grc",
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

    foreach (var t in tbesgRows)
    {
        if (t.StrongCode != t.BaseStrongCode && map.TryGetValue(t.BaseStrongCode, out var baseEntry))
        {
            baseEntry.ShortGloss ??= t.ShortGloss;
            baseEntry.PosCode ??= t.PosCode;
            baseEntry.TbesgMeaning ??= t.MeaningMarkdown;
        }
    }

    // 2. Hebrew/Aramaic Strong + LexicalIndex (root/TWOT) + TBESH (CC BY 4.0 metadata only)
    foreach (var s in strongHebrew)
    {
        if (lexicalIndex.TryGetValue(s.BaseStrongCode, out var meta))
        {
            s.RootHebrew = meta.RootHebrew;
            s.TwotRef = meta.TwotRef;
            s.ShortGloss ??= meta.ShortDef;
        }
        map[s.StrongCode] = s;
    }

    foreach (var h in tbeshRows)
    {
        if (map.TryGetValue(h.StrongCode, out var existing))
        {
            if (string.IsNullOrWhiteSpace(existing.Lemma) && !string.IsNullOrWhiteSpace(h.Lemma))
            {
                existing.Lemma = h.Lemma;
                existing.LemmaNormalized = h.LemmaNormalized;
            }
            if (!string.IsNullOrWhiteSpace(h.Transliteration))
            {
                existing.Transliteration = h.Transliteration;
                existing.TransliterationNormalized = h.TransliterationNormalized;
            }
            existing.PosCode = h.PosCode ?? existing.PosCode;
            existing.ShortGloss = h.ShortGloss ?? existing.ShortGloss;
            if (h.Language == "arc")
                existing.Language = "arc";
        }
        else
        {
            map.TryGetValue(h.BaseStrongCode, out var baseEntry);
            map[h.StrongCode] = new LexiconEntry
            {
                StrongCode = h.StrongCode,
                BaseStrongCode = h.BaseStrongCode,
                StrongNumber = h.StrongNumber,
                IsExtended = h.IsExtended,
                Language = h.Language,
                Lemma = h.Lemma,
                LemmaNormalized = h.LemmaNormalized,
                RootHebrew = baseEntry?.RootHebrew,
                TwotRef = baseEntry?.TwotRef,
                Transliteration = h.Transliteration ?? baseEntry?.Transliteration,
                TransliterationNormalized = h.TransliterationNormalized ?? baseEntry?.TransliterationNormalized,
                PosCode = h.PosCode ?? baseEntry?.PosCode,
                ShortGloss = h.ShortGloss ?? baseEntry?.ShortGloss,
                StrongDerivation = baseEntry?.StrongDerivation,
                StrongDefinition = baseEntry?.StrongDefinition,
                StrongKjvDefinition = baseEntry?.StrongKjvDefinition
            };
        }
    }

    foreach (var h in tbeshRows)
    {
        if (h.StrongCode != h.BaseStrongCode && map.TryGetValue(h.BaseStrongCode, out var baseEntry))
        {
            baseEntry.ShortGloss ??= h.ShortGloss;
            baseEntry.PosCode ??= h.PosCode;
        }
    }

    return map;
}
