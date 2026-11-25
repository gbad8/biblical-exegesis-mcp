using System.Text;
using GreekNtMcp.Domain.Models;
using GreekNtMcp.Domain.Morphology;
using GreekNtMcp.Domain.References;

namespace GreekNtMcp.Server.Formatting;

public static class MarkdownFormatter
{
    public static string FormatLookupStrong(
        LookupStrongResult result,
        DetailLevel detailLevel,
        int lsjOffset,
        int lsjMaxChars)
    {
        var e = result.PrimaryEntry;
        var sb = new StringBuilder();

        sb.AppendLine($"# {e.StrongCode} — {e.Lemma}");
        sb.AppendLine();
        sb.AppendLine($"- **Transliteração:** `{e.Transliteration ?? "—"}`");
        if (!string.IsNullOrWhiteSpace(e.PosCode))
            sb.AppendLine($"- **Classe (TBESG):** `{e.PosCode}`");
        if (!string.IsNullOrWhiteSpace(e.ShortGloss))
            sb.AppendLine($"- **Glosa:** {e.ShortGloss}");
        sb.AppendLine($"- **Ocorrências no NT:** Família 35 = **{e.OccurrencesF35}** | TR = **{e.OccurrencesTr}** | Bizantino = **{e.OccurrencesByz}** | NA28 = **{e.OccurrencesNa28}**");

        if (result.ExtendedSubEntries.Count > 0)
        {
            var subs = string.Join(", ", result.ExtendedSubEntries.Select(s => $"`{s.StrongCode}` ({s.ShortGloss ?? s.Lemma})"));
            sb.AppendLine($"- **Sub-códigos estendidos (STEPBible):** {subs}");
        }

        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(e.TbesgMeaning))
        {
            sb.AppendLine("## Resumo Léxico (TBESG / STEPBible)");
            sb.AppendLine(e.TbesgMeaning.Trim());
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(e.StrongDerivation))
        {
            sb.AppendLine("## Etimologia (Strong)");
            sb.AppendLine(e.StrongDerivation.Trim());
            sb.AppendLine();
        }

        if (detailLevel == DetailLevel.Brief)
        {
            sb.AppendLine("---");
            sb.AppendLine($"*Modo `brief`. Use `detail_level=\"standard\"` para Strong + Abbott-Smith completos, ou `detail_level=\"full\"` para incluir o LSJ integral.*");
            return sb.ToString();
        }

        if (!string.IsNullOrWhiteSpace(e.StrongDefinition) || !string.IsNullOrWhiteSpace(e.StrongKjvDefinition))
        {
            sb.AppendLine("## Definição Original (Strong)");
            if (!string.IsNullOrWhiteSpace(e.StrongDefinition))
                sb.AppendLine(e.StrongDefinition.Trim());
            if (!string.IsNullOrWhiteSpace(e.StrongKjvDefinition))
                sb.AppendLine($"\n**Traduções KJV (Strong):** {e.StrongKjvDefinition.Trim()}");
            sb.AppendLine();
        }

        sb.AppendLine("## Abbott-Smith Manual Greek Lexicon");
        if (result.AbbottSmithEntries.Count == 0)
        {
            sb.AppendLine("*Nenhum verbete correspondente encontrado no Abbott-Smith para este código.*");
        }
        else
        {
            foreach (var asm in result.AbbottSmithEntries)
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

        sb.AppendLine("## Liddell-Scott-Jones (TFLSJ / STEPBible)");
        if (result.LsjEntries.Count == 0)
        {
            sb.AppendLine("*Nenhum verbete correspondente encontrado no TFLSJ para este código.*");
        }
        else if (detailLevel == DetailLevel.Standard)
        {
            var totalChars = result.LsjEntries.Sum(l => l.CharLength);
            var first = result.LsjEntries[0];
            if (!string.IsNullOrWhiteSpace(first.SummaryMarkdown))
            {
                sb.AppendLine(first.SummaryMarkdown.Trim());
                sb.AppendLine();
            }
            sb.AppendLine($"*Verbete completo do LSJ disponível ({totalChars:N0} caracteres). Chame `lookup_strong(code=\"{e.StrongCode}\", detail_level=\"full\")` para leitura integral.*");
        }
        else
        {
            lsjOffset = Math.Max(0, lsjOffset);
            lsjMaxChars = Math.Clamp(lsjMaxChars, 1000, 40000);

            var combined = string.Join("\n\n---\n\n", result.LsjEntries.Select(l => l.FullMarkdown.Trim()));
            if (lsjOffset >= combined.Length)
            {
                sb.AppendLine($"*`lsj_offset={lsjOffset}` excede o tamanho total do verbete LSJ ({combined.Length:N0} caracteres).*");
            }
            else
            {
                var sliceLen = Math.Min(lsjMaxChars, combined.Length - lsjOffset);
                sb.AppendLine(combined.Substring(lsjOffset, sliceLen));
                if (lsjOffset + sliceLen < combined.Length)
                {
                    var nextOffset = lsjOffset + sliceLen;
                    sb.AppendLine();
                    sb.AppendLine($"*Exibindo caracteres {lsjOffset + 1}–{nextOffset} de {combined.Length:N0}. Para continuar, chame `lookup_strong(code=\"{e.StrongCode}\", detail_level=\"full\", lsj_offset={nextOffset})`.*");
                }
            }
        }

        return sb.ToString();
    }

    public static string FormatSearchLemma(string query, bool fuzzy, IReadOnlyList<SearchLemmaHit> hits)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Busca de Lema: `{query}`");
        sb.AppendLine($"- **Resultados:** {hits.Count} | **Fuzzy (`pg_trgm`):** {(fuzzy ? "ativado" : "desativado")}");
        sb.AppendLine();

        if (hits.Count == 0)
        {
            sb.AppendLine("Nenhum lema encontrado. Tente remover diacríticos, usar transliteração ASCII (ex.: `agape`, `logos`) ou glosa em inglês (ex.: `love`, `grace`).");
            return sb.ToString();
        }

        sb.AppendLine("| Strong | Lema | Transliteração | Classe | Glosa (TBESG) | Ocorr. (F35 / TR) | Match |");
        sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :---: | :--- |");

        foreach (var h in hits)
        {
            sb.AppendLine($"| `{h.StrongCode}` | {EscapePipe(h.Lemma)} | `{EscapePipe(h.Transliteration ?? "—")}` | `{EscapePipe(h.PosCode ?? "—")}` | {EscapePipe(h.ShortGloss ?? "—")} | {h.OccurrencesF35} / {h.OccurrencesTr} | `{h.MatchType}` |");
        }

        sb.AppendLine();
        sb.AppendLine("*Use `lookup_strong(code=\"...\")` para o verbete completo ou `word_occurrences(lemma_or_strong=\"...\")` para a concordância no NT.*");
        return sb.ToString();
    }

    public static string FormatWordOccurrences(WordOccurrencesResult result, int limit, int offset)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Concordância no NT — {result.ResolvedLabel}");
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
            var morph = RobinsonMorphDecoder.FormatWithCode(h.MorphCode);
            var gloss = string.IsNullOrWhiteSpace(h.ContextualGloss) ? "—" : h.ContextualGloss;
            sb.AppendLine($"### {idx}. {h.BookNamePt} {h.Chapter}:{h.Verse}");
            sb.AppendLine($"> {item.HighlightedGreekVerse}");
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
            sb.AppendLine($"## {reference.Book.NamePt} {block.Chapter}:{block.Verse}");
            sb.AppendLine($"> {block.GreekText}");
            sb.AppendLine();

            if (block.Tokens.Count == 0)
            {
                sb.AppendLine($"*Este versículo está ausente na edição `{edition.ToString().ToLowerInvariant()}` (veja Variantes Textuais abaixo).*");
                sb.AppendLine();
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
            }

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
                        sb.AppendLine($"- **Nota F35 vs. TR:** Há divergência lexical/ortográfica entre F35 e TR neste versículo.");

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

    private static string EscapePipe(string text) => text.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
}
