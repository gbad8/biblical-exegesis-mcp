using System.ComponentModel;
using BiblicalExegesisMcp.Domain.References;
using BiblicalExegesisMcp.Infrastructure.Repositories;
using BiblicalExegesisMcp.Server.Formatting;
using ModelContextProtocol.Server;

namespace BiblicalExegesisMcp.Server.Tools;

[McpServerToolType]
public sealed class ExegesisTools
{
    private readonly ExegesisRepository _repository;

    public ExegesisTools(ExegesisRepository repository)
    {
        _repository = repository;
    }

    [McpServerTool(Name = "lookup_strong")]
    [Description("Consulta lexicográfica exata por código Strong Grego (ex: 'G3056', '3056', 'G0025A') ou Hebraico/Aramaico (ex: 'H7225', 'H2617', 'H0430'). Para Grego entrega TBESG, Strong, Abbott-Smith (sinônimos e LXX) e LSJ; para Hebraico/Aramaico entrega glosa TBESH, raiz semítica, TWOT, Strong Hebraico e Brown-Driver-Briggs (BDB) completo sob demanda.")]
    public async Task<string> LookupStrongAsync(
        [Description("Código Strong grego ('G3056', '3056') ou hebraico/aramaico ('H7225', 'H2617', 'H1254A').")] string code,
        [Description("Nível de detalhe: 'brief' (resumo + etimologia), 'standard' (padrão: + Strong + Abbott-Smith/resumo BDB + resumo LSJ) ou 'full' (+ LSJ ou BDB integral paginado).")] string detail_level = "standard",
        [Description("Offset em caracteres para paginação de verbetes extensos (LSJ ou BDB) no modo 'full'.")] int lsj_offset = 0,
        [Description("Limite máximo de caracteres do LSJ/BDB retornados por chamada no modo 'full' (1000..40000).")] int lsj_max_chars = 12000,
        CancellationToken ct = default)
    {
        var result = await _repository.LookupStrongAsync(code, ct);
        if (result is null)
            return $"Código Strong não encontrado ou inválido: `{code}`. Use formatos como `G3056` (Grego) ou `H2617` (Hebraico), ou chame `search_lemma` para localizar o código.";

        var level = ReferenceParser.ParseDetailLevel(detail_level);
        return MarkdownFormatter.FormatLookupStrong(result, level, lsj_offset, lsj_max_chars);
    }

    [McpServerTool(Name = "search_lemma")]
    [Description("Busca lemas bíblicos (Grego do NT e Hebraico/Aramaico do AT) por escrita original (com ou sem acentos gregos ou vogais/niqqud hebraicos), transliteração ASCII ('agape', 'logos', 'chesed', 'dabar') ou glosa em inglês ('love', 'covenant').")]
    public async Task<string> SearchLemmaAsync(
        [Description("Termo de busca: grego ('λόγος'/'λογος'), hebraico ('בְּרֵאשִׁית'/'בראשית'), transliteração ('chesed', 'logos') ou glosa em inglês ('grace', 'covenant').")] string query,
        [Description("Se true (padrão), ativa busca aproximada (fuzzy) via pg_trgm.")] bool fuzzy = true,
        [Description("Máximo de resultados (1 a 50, padrão 15).")] int limit = 15,
        [Description("Filtro opcional de testamento: 'all' (padrão), 'ot' (AT Hebraico/Aramaico) ou 'nt' (NT Grego).")] string testament = "all",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "Informe um termo em `query` (grego, hebraico, transliteração ou glosa em inglês).";

        var hits = await _repository.SearchLemmaAsync(query, fuzzy, limit, testament, ct);
        return MarkdownFormatter.FormatSearchLemma(query, fuzzy, testament, hits);
    }

    [McpServerTool(Name = "word_occurrences")]
    [Description("Concordância exata de um código Strong (Gxxxx ou Hxxxx) ou lema grego/hebraico em toda a Bíblia (AT Massorético ou NT Grego), com texto original em contexto (forma em negrito) e morfologia (Robinson ou OSHM) decodificada em português.")]
    public async Task<string> WordOccurrencesAsync(
        [Description("Código Strong ('G3056', 'H2617', 'H1254') ou lema em grego/hebraico com ou sem acentos/vogais ('λόγος', 'ברא', 'חסד').")] string lemma_or_strong,
        [Description("Edição textual (opcional — padrão automático: 'f35' para NT, 'mt' para AT). NT: 'f35', 'tr', 'byz', 'na28', 'sbl', 'all'. AT: 'mt' (Leningrado/Qere), 'ketiv', 'all'.")] string? edition = null,
        [Description("Filtro opcional por livro bíblico em português ou inglês (ex: 'Gn', 'Sl', 'Is', 'Dn', 'Jo', 'Rom', '1Co').")] string? book = null,
        [Description("Quantidade de ocorrências por página (1 a 100, padrão 30).")] int limit = 30,
        [Description("Deslocamento para paginação (padrão 0).")] int offset = 0,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(lemma_or_strong))
            return "Informe um código Strong (`Gxxxx` ou `Hxxxx`) ou lema grego/hebraico em `lemma_or_strong`.";

        try
        {
            var result = await _repository.GetWordOccurrencesAsync(lemma_or_strong, edition, book, limit, offset, ct);
            return MarkdownFormatter.FormatWordOccurrences(result, limit, offset);
        }
        catch (ArgumentException ex)
        {
            return $"Erro de parâmetro: {ex.Message}";
        }
    }

    [McpServerTool(Name = "parse_verse")]
    [Description("Análise palavra por palavra de qualquer versículo ou faixa curta dos 66 livros da Bíblia (ex: 'Gn 1:1-3', 'Sl 23:1', 'Is 53:5', 'Dn 7:13', 'Jo 1:1', '1Jo 5:7-8'). Entrega texto original (TM Hebraico/Aramaico no AT; Família 35/TR no NT), lema, Strong, morfologia (OSHM ou Robinson) decodificada em português e notas de variantes textuais.")]
    public async Task<string> ParseVerseAsync(
        [Description("Referência bíblica em português ou inglês, versículo único ou faixa (até 30 versículos). Ex: 'Gn 1:1-3', 'Sl 23:1', 'Jo 1:1-5', '1Co 13:4'.")] string reference,
        [Description("Edição textual (opcional — padrão automático: 'mt' para AT, 'f35' para NT). AT: 'mt' (WLC/Qere), 'ketiv', 'all'. NT: 'f35', 'tr', 'byz', 'na28', 'sbl'.")] string? edition = null,
        [Description("Se true (padrão), inclui Notas Massoréticas/Qere-Ketiv/variantes no AT ou comparação F35/TR/Byz/NA28 no NT.")] bool include_variants = true,
        CancellationToken ct = default)
    {
        var (parsedRef, error) = ReferenceParser.Parse(reference);
        if (error is not null || parsedRef is null)
            return $"Referência inválida: {error}";

        var resolvedEdition = ReferenceParser.ParseEditionForBook(edition, parsedRef.Book);
        var blocks = await _repository.ParseVerseRangeAsync(parsedRef, resolvedEdition, include_variants, ct);
        return MarkdownFormatter.FormatParsedVerses(parsedRef, resolvedEdition, include_variants, blocks);
    }
}
