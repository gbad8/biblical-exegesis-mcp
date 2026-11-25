using System.Text;
using BiblicalExegesisMcp.Domain.Models;
using BiblicalExegesisMcp.Domain.Morphology;
using BiblicalExegesisMcp.Domain.References;

namespace BiblicalExegesisMcp.Server.Formatting;

public static class MarkdownFormatter
{
    public static string FormatLookupStrong(
        LookupStrongResult result,
        DetailLevel detailLevel,
        int fullOffset,
        int fullMaxChars)
    {
        var e = result.PrimaryEntry;
        bool isHebrew = e.Language is "heb" or "arc" || e.StrongCode.StartsWith('H');
        var sb = new StringBuilder();

        sb.AppendLine($"# {e.StrongCode} — {e.Lemma}");
        sb.AppendLine();
        sb.AppendLine($"- **Idioma:** {LanguageLabel(e.Language)}");
        sb.AppendLine($"- **Transliteração:** `{e.Transliteration ?? "—"}`");
        if (!string.IsNullOrWhiteSpace(e.PosCode))
            sb.AppendLine($"- **Classe Gramatical:** `{e.PosCode}`");
        if (!string.IsNullOrWhiteSpace(e.ShortGloss))
            sb.AppendLine($"- **Glosa:** {e.ShortGloss}");
        if (!string.IsNullOrWhiteSpace(e.RootHebrew))
            sb.AppendLine($"- **Raiz Semítica:** `{e.RootHebrew}`");
        if (!string.IsNullOrWhiteSpace(e.TwotRef))
            sb.AppendLine($"- **Referência TWOT:** `{e.TwotRef}`");

        if (isHebrew)
        {
            sb.AppendLine($"- **Ocorrências no AT (Texto Massorético):** **{e.OccurrencesMt}**");
        }
        else
        {
            sb.AppendLine($"- **Ocorrências no NT:** Família 35 = **{e.OccurrencesF35}** | TR = **{e.OccurrencesTr}** | Bizantino = **{e.OccurrencesByz}** | NA28 = **{e.OccurrencesNa28}**");
        }

        if (result.ExtendedSubEntries.Count > 0)
        {
            var subs = string.Join(", ", result.ExtendedSubEntries.Take(12).Select(s => $"`{s.StrongCode}` ({s.ShortGloss ?? s.Lemma})"));
            var extra = result.ExtendedSubEntries.Count > 12 ? $" *(+{result.ExtendedSubEntries.Count - 12} outros)*" : string.Empty;
            sb.AppendLine($"- **Sub-códigos estendidos (STEPBible):** {subs}{extra}");
        }

        sb.AppendLine();

        if (!isHebrew && !string.IsNullOrWhiteSpace(e.TbesgMeaning))
        {
            sb.AppendLine("## Resumo Léxico (TBESG / STEPBible)");
            sb.AppendLine(e.TbesgMeaning.Trim());
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(e.StrongDerivation))
        {
            sb.AppendLine("## Etimologia / Origem (Strong)");
            sb.AppendLine(e.StrongDerivation.Trim());
            sb.AppendLine();
        }

        if (detailLevel == DetailLevel.Brief)
        {
            sb.AppendLine("---");
            var fullLexName = isHebrew ? "BDB integral" : "LSJ integral";
            var stdLexName = isHebrew ? "Strong + resumo BDB" : "Strong + Abbott-Smith completos";
            sb.AppendLine($"*Modo `brief`. Use `detail_level=\"standard\"` para {stdLexName}, ou `detail_level=\"full\"` para incluir o {fullLexName}.*");
            return sb.ToString();
        }

        if (!string.IsNullOrWhiteSpace(e.StrongDefinition) || !string.IsNullOrWhiteSpace(e.StrongKjvDefinition))
        {
            sb.AppendLine("## Definição Original (Strong)");
            if (!string.IsNullOrWhiteSpace(e.StrongDefinition))
                sb.AppendLine(e.StrongDefinition.Trim());
            if (!string.IsNullOrWhiteSpace(e.StrongKjvDefinition))
                sb.AppendLine($"\n**Uso / Traduções KJV (Strong):** {e.StrongKjvDefinition.Trim()}");
            sb.AppendLine();
        }

        if (isHebrew)
        {
            AppendBdbSection(sb, e.StrongCode, result.BdbEntries, detailLevel, fullOffset, fullMaxChars);
        }
        else
        {
            AppendAbbottSmithSection(sb, result.AbbottSmithEntries);
            AppendLsjSection(sb, e.StrongCode, result.LsjEntries, detailLevel, fullOffset, fullMaxChars);
        }

        return sb.ToString();
    }

    private static void AppendBdbSection(
        StringBuilder sb,
        string strongCode,
        IReadOnlyList<BdbEntry> bdbEntries,
        DetailLevel detailLevel,
        int offset,
        int maxChars)
    {
        sb.AppendLine("## Brown-Driver-Briggs Hebrew & English Lexicon (BDB)");
        if (bdbEntries.Count == 0)
        {
            sb.AppendLine("*Nenhum verbete correspondente encontrado no BDB para este código.*");
            return;
        }

        if (detailLevel == DetailLevel.Standard)
        {
            var totalChars = bdbEntries.Sum(b => b.CharLength);
            foreach (var b in bdbEntries.Take(2))
            {
                if (!string.IsNullOrWhiteSpace(b.SummaryMarkdown))
                {
                    sb.AppendLine(b.SummaryMarkdown.Trim());
                    sb.AppendLine();
                }
            }
            sb.AppendLine($"*Verbete completo do BDB disponível ({totalChars:N0} caracteres, {bdbEntries.Count} entrada(s)). Chame `lookup_strong(code=\"{strongCode}\", detail_level=\"full\")` para leitura integral.*");
            return;
        }

        AppendPaginatedLexicon(
            sb,
            strongCode,
            "BDB",
            string.Join("\n\n---\n\n", bdbEntries.Select(b => $"### BDB `{b.BdbId}` — {b.Headword}\n\n{b.FullMarkdown.Trim()}")),
            offset,
            maxChars);
    }

    private static void AppendAbbottSmithSection(StringBuilder sb, IReadOnlyList<AbbottSmithEntry> entries)
    {
        sb.AppendLine("## Abbott-Smith Manual Greek Lexicon");
        if (entries.Count == 0)
        {
            sb.AppendLine("*Nenhum verbete correspondente encontrado no Abbott-Smith para este código.*");
            sb.AppendLine();
            return;
        }

        foreach (var asm in entries)
        {
            if (!string.IsNullOrWhiteSpace(asm.GrammaticalInfo))
                sb.AppendLine($"**Forma/Gramática:** {asm.GrammaticalInfo.Trim()}");
            if (asm.LxxEquivalents.Length > 0)
                sb.AppendLine($"**Equivalentes LXX:** {string.Join("; ", asm.LxxEquivalents)}");
            if (asm.Synonyms.Length > 0)
                sb.AppendLine($"**Sinônimos (SYN.):** {string.Join(", ", asm.Synonyms)}");
            sb.AppendLine();
            sb.AppendLine(asm.FullMarkdown.Trim());
            sb.AppendLine();
        }
    }

    private static void AppendLsjSection(
        StringBuilder sb,
        string strongCode,
        IReadOnlyList<LsjEntry> lsjEntries,
        DetailLevel detailLevel,
        int offset,
        int maxChars)
    {
        sb.AppendLine("## Liddell-Scott-Jones (TFLSJ / STEPBible)");
        if (lsjEntries.Count == 0)
        {
            sb.AppendLine("*Nenhum verbete correspondente encontrado no TFLSJ para este código.*");
            return;
        }

        if (detailLevel == DetailLevel.Standard)
        {
            var totalChars = lsjEntries.Sum(l => l.CharLength);
            var first = lsjEntries[0];
            if (!string.IsNullOrWhiteSpace(first.SummaryMarkdown))
            {
                sb.AppendLine(first.SummaryMarkdown.Trim());
                sb.AppendLine();
            }
            sb.AppendLine($"*Verbete completo do LSJ disponível ({totalChars:N0} caracteres). Chame `lookup_strong(code=\"{strongCode}\", detail_level=\"full\")` para leitura integral.*");
            return;
        }

        AppendPaginatedLexicon(
            sb,
            strongCode,
            "LSJ",
            string.Join("\n\n---\n\n", lsjEntries.Select(l => l.FullMarkdown.Trim())),
            offset,
            maxChars);
    }

    private static void AppendPaginatedLexicon(
        StringBuilder sb,
        string strongCode,
        string lexiconName,
        string combined,
        int offset,
        int maxChars)
    {
        offset = Math.Max(0, offset);
        maxChars = Math.Clamp(maxChars, 1000, 40000);

        if (offset >= combined.Length)
        {
            sb.AppendLine($"*`lsj_offset={offset}` excede o tamanho total do verbete {lexiconName} ({combined.Length:N0} caracteres).*");
            return;
        }

        var sliceLen = Math.Min(maxChars, combined.Length - offset);
        sb.AppendLine(combined.Substring(offset, sliceLen));
        if (offset + sliceLen < combined.Length)
        {
            var nextOffset = offset + sliceLen;
            sb.AppendLine();
            sb.AppendLine($"*Exibindo caracteres {offset + 1}–{nextOffset} de {combined.Length:N0} ({lexiconName}). Para continuar, chame `lookup_strong(code=\"{strongCode}\", detail_level=\"full\", lsj_offset={nextOffset})`.*");
        }
    }

    public static string FormatSearchLemma(string query, bool fuzzy, string testament, IReadOnlyList<SearchLemmaHit> hits)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Busca de Lema: `{query}`");
        sb.AppendLine($"- **Resultados:** {hits.Count} | **Filtro:** `{testament}` | **Fuzzy (`pg_trgm`):** {(fuzzy ? "ativado" : "desativado")}");
        sb.AppendLine();

        if (hits.Count == 0)
        {
            sb.AppendLine("Nenhum lema encontrado. Tente remover acentos/vogais (niqqud), usar transliteração ASCII (ex.: `agape`, `logos`, `chesed`, `dabar`) ou glosa em inglês.");
            return sb.ToString();
        }

        sb.AppendLine("| Strong | Idioma | Lema | Transliteração | Classe | Glosa | Ocorrências | Match |");
        sb.AppendLine("| :--- | :---: | :--- | :--- | :--- | :--- | :---: | :--- |");

        foreach (var h in hits)
        {
            var occ = h.Language is "heb" or "arc"
                ? $"TM: {h.OccurrencesMt}"
                : $"F35: {h.OccurrencesF35} / TR: {h.OccurrencesTr}";

            sb.AppendLine(
                $"| `{h.StrongCode}` | {LanguageShort(h.Language)} | {EscapePipe(h.Lemma)} | `{EscapePipe(h.Transliteration ?? "—")}` | `{EscapePipe(h.PosCode ?? "—")}` | {EscapePipe(h.ShortGloss ?? "—")} | {occ} | `{h.MatchType}` |");
        }

        sb.AppendLine();
        sb.AppendLine("*Use `lookup_strong(code=\"...\")` para o verbete completo ou `word_occurrences(lemma_or_strong=\"...\")` para a concordância bíblica.*");
        return sb.ToString();
    }

    public static string FormatWordOccurrences(WordOccurrencesResult result, int limit, int offset)
    {
        var sb = new StringBuilder();
        var scope = result.IsOldTestament ? "AT" : "NT";
        sb.AppendLine($"# Concordância no {scope} — {result.ResolvedLabel}");
        sb.AppendLine($"- **Edição:** {ReferenceParser.EditionDisplayName(result.Edition)}");
        sb.AppendLine($"- **Total de ocorrências encontradas:** **{result.TotalOccurrences}**");
        if (result.TotalOccurrences > 0)
        {
            var start = offset + 1;
            var end = Math.Min(offset + result.Items.Count, result.TotalOccurrences);
            sb.AppendLine($"- **Exibindo:** {start}–{end} (use `offset={offset + limit}` para próxima página)");
        }

        if (result.BookDistribution.Count > 0)
        {
            var dist = string.Join(", ", result.BookDistribution.Select(b => $"{b.BookNamePt} ({b.Count})"));
            sb.AppendLine($"- **Distribuição por livro:** {dist}");
        }

        if (result.Edition == BibleEdition.F35 && result.UntaggedF35Count > 0)
        {
            sb.AppendLine($"- *Nota de precisão (F35):* Tokens alinhados deterministicamente contra o TAGNT; variantes exclusivas da F35 sem equivalência unívoca no corpus ({result.UntaggedF35Count} no NT inteiro) não recebem etiqueta inferida.");
        }

        sb.AppendLine();

        if (result.Items.Count == 0)
        {
            sb.AppendLine("Nenhuma ocorrência encontrada para os critérios informados.");
            return sb.ToString();
        }

        int idx = offset + 1;
        foreach (var item in result.Items)
        {
            var h = item.Hit;
            var morph = h.Language is "heb" or "arc"
                ? OshmMorphDecoder.FormatWithCode(h.MorphCode)
                : RobinsonMorphDecoder.FormatWithCode(h.MorphCode);
            var gloss = string.IsNullOrWhiteSpace(h.ContextualGloss) ? "—" : h.ContextualGloss;
            var hebSuffix = !string.IsNullOrEmpty(h.HebrewRef) ? $" *(BHS: {h.HebrewRef})*" : string.Empty;

            sb.AppendLine($"### {idx}. {h.BookNamePt} {h.Chapter}:{h.Verse}{hebSuffix}");
            sb.AppendLine($"> {item.HighlightedVerse}");
            sb.AppendLine($"- **Forma:** `{h.SurfaceForm}` | **Strong:** `{h.StrongCode ?? "—"}` | **Morfologia:** {morph} | **Glosa:** {gloss}");
            sb.AppendLine();
            idx++;
        }

        return sb.ToString();
    }

    public static string FormatParsedVerses(
        BibleReference reference,
        BibleEdition edition,
        bool includeVariants,
        IReadOnlyList<ParsedVerseBlock> blocks)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Análise Morfológica — {reference.DisplayPt} ({reference.DisplayEn})");
        sb.AppendLine($"- **Edição Base:** {ReferenceParser.EditionDisplayName(edition)}");
        sb.AppendLine();

        if (blocks.Count == 0)
        {
            sb.AppendLine("Nenhum versículo encontrado para esta referência no banco de dados.");
            return sb.ToString();
        }

        foreach (var block in blocks)
        {
            var bhsTag = !string.IsNullOrEmpty(block.HebrewRef) ? $" *(BHS: `{block.HebrewRef}`)*" : string.Empty;
            sb.AppendLine($"## {reference.Book.NamePt} {block.Chapter}:{block.Verse}{bhsTag}");
            sb.AppendLine($"> {block.OriginalText}");
            sb.AppendLine();

            if (block.Tokens.Count == 0)
            {
                sb.AppendLine($"*Este versículo está ausente na edição `{edition.ToString().ToLowerInvariant()}`.*");
                sb.AppendLine();
            }
            else if (block.IsOldTestament)
            {
                sb.AppendLine("| # | Forma (Segmentada) | Translit. | Lema | Strong | Morfologia (OSHM + PT) | Glosa |");
                sb.AppendLine("| :---: | :--- | :--- | :--- | :--- | :--- | :--- |");

                foreach (var t in block.Tokens)
                {
                    var formCell = !string.IsNullOrWhiteSpace(t.SegmentedForm) && t.SegmentedForm != t.SurfaceForm
                        ? $"**{EscapePipe(t.SurfaceForm)}** (`{EscapePipe(t.SegmentedForm)}`)"
                        : $"**{EscapePipe(t.SurfaceForm)}**";
                    var strongDisplay = FormatStrongCell(t);
                    var morphDisplay = OshmMorphDecoder.FormatWithCode(t.MorphCode);

                    sb.AppendLine(
                        $"| {t.WordPosition} | {formCell} | `{EscapePipe(t.Transliteration ?? "—")}` | {EscapePipe(t.Lemma ?? "—")} | {strongDisplay} | {EscapePipe(morphDisplay)} | {EscapePipe(t.ContextualGloss ?? "—")} |");
                }

                sb.AppendLine();

                if (includeVariants && block.MasoreticNotes.Count > 0)
                {
                    sb.AppendLine("### Notas Massoréticas e Variantes (Qere/Ketiv, Aleppo, DSS, Ben Chaim)");
                    foreach (var note in block.MasoreticNotes)
                        sb.AppendLine($"- {note}");
                    sb.AppendLine();
                }
            }
            else
            {
                sb.AppendLine("| # | Forma | Translit. | Lema | Strong | Morfologia (Robinson + PT) | Glosa |");
                sb.AppendLine("| :---: | :--- | :--- | :--- | :--- | :--- | :--- |");

                foreach (var t in block.Tokens)
                {
                    var strongDisplay = FormatStrongCell(t);
                    var morphDisplay = t.AlignmentStatus == "untagged_f35_variant"
                        ? "*Variante exclusiva F35 (sem etiqueta automática)*"
                        : RobinsonMorphDecoder.FormatWithCode(t.MorphCode);

                    sb.AppendLine(
                        $"| {t.WordPosition} | **{EscapePipe(t.SurfaceForm)}** | `{EscapePipe(t.Transliteration ?? "—")}` | {EscapePipe(t.Lemma ?? "—")} | {strongDisplay} | {EscapePipe(morphDisplay)} | {EscapePipe(t.ContextualGloss ?? "—")} |");
                }

                sb.AppendLine();

                if (includeVariants && block.Comparison is { } cmp)
                {
                    var hasAnyDiff = cmp.F35DiffersFromTr || cmp.TrDiffersFromByz || cmp.PrimaryDiffersFromNa28;
                    sb.AppendLine("### Comparação Textual (F35 / TR / Bizantino / NA28)");
                    if (!hasAnyDiff)
                    {
                        sb.AppendLine("- *Sem divergências textuais entre Família 35, Textus Receptus, Bizantino e NA28 neste versículo.*");
                    }
                    else
                    {
                        if (edition != BibleEdition.F35 && cmp.F35Text is not null)
                            sb.AppendLine($"- **Família 35 (F35):** {cmp.F35Text}");
                        if (edition != BibleEdition.Tr && cmp.TrText is not null)
                            sb.AppendLine($"- **Textus Receptus (TR):** {cmp.TrText}{(cmp.F35DiffersFromTr ? " *(difere da F35)*" : " *(idêntico à F35)*")}");
                        else if (edition == BibleEdition.Tr && cmp.F35Text is not null && cmp.F35DiffersFromTr)
                            sb.AppendLine("- **Nota F35 vs. TR:** Há divergência lexical/ortográfica entre F35 e TR neste versículo.");

                        if (cmp.TrDiffersFromByz && edition != BibleEdition.Byz && cmp.ByzText is not null)
                            sb.AppendLine($"- **Texto Bizantino (Byz):** {cmp.ByzText}");

                        if (cmp.PrimaryDiffersFromNa28 && edition != BibleEdition.Na28)
                        {
                            var naText = string.IsNullOrWhiteSpace(cmp.Na28Text) ? "*(verso omitido no NA28)*" : cmp.Na28Text;
                            sb.AppendLine($"- **Nestle-Aland 28 (NA28):** {naText}");
                        }
                    }
                    sb.AppendLine();
                }
            }
        }

        return sb.ToString();
    }

    private static string FormatStrongCell(VerseTokenView t)
    {
        if (string.IsNullOrWhiteSpace(t.StrongCode))
            return "—";
        if (t.SecondaryStrongCodes.Length == 0)
            return $"`{t.StrongCode}`";
        return $"`{t.StrongCode}` + " + string.Join(" + ", t.SecondaryStrongCodes.Select(c => $"`{c}`"));
    }

    private static string LanguageLabel(string lang) => lang switch
    {
        "heb" => "Hebraico Bíblico",
        "arc" => "Aramaico Bíblico",
        _ => "Grego Koiné"
    };

    private static string LanguageShort(string lang) => lang switch
    {
        "heb" => "Heb",
        "arc" => "Aram",
        _ => "Grc"
    };

    private static string EscapePipe(string text) => text.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
}
