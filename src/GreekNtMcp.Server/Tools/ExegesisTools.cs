using System.ComponentModel;
using GreekNtMcp.Domain.Models;
using GreekNtMcp.Domain.References;
using GreekNtMcp.Infrastructure.Repositories;
using GreekNtMcp.Server.Formatting;
using ModelContextProtocol.Server;

namespace GreekNtMcp.Server.Tools;

[McpServerToolType]
public sealed class ExegesisTools
{
    private readonly ExegesisRepository _repository;

    public ExegesisTools(ExegesisRepository repository)
    {
        _repository = repository;
    }

    [McpServerTool(Name = "lookup_strong")]
    [Description("Consulta lexicográfica exata por código Strong (ex: 'G3056', '3056', 'G0025A'). Retorna lema politônico, transliteração, etimologia, resumo TBESG, definição Strong, verbete Abbott-Smith (acepções, sinônimos, LXX) e LSJ (TFLSJ) completo sob demanda.")]
    public async Task<string> LookupStrongAsync(
        [Description("Código Strong grego (ex: 'G3056', '3056', 'G0026', 'G0025A').")] string code,
        [Description("Nível de detalhe: 'brief' (TBESG + etimologia), 'standard' (padrão: + Strong + Abbott-Smith + resumo LSJ) ou 'full' (+ LSJ integral paginado).")] string detail_level = "standard",
        [Description("Offset em caracteres para paginação de verbetes LSJ extensos no modo 'full'.")] int lsj_offset = 0,
        [Description("Limite máximo de caracteres do LSJ retornados por chamada no modo 'full' (1000..40000).")] int lsj_max_chars = 12000,
        CancellationToken ct = default)
    {
        var result = await _repository.LookupStrongAsync(code, ct);
        if (result is null)
            return $"Código Strong não encontrado ou inválido: `{code}`. Use formatos como `G3056`, `3056` ou `search_lemma` para localizar o código correto.";

        var level = ReferenceParser.ParseDetailLevel(detail_level);
        return MarkdownFormatter.FormatLookupStrong(result, level, lsj_offset, lsj_max_chars);
    }

    [McpServerTool(Name = "search_lemma")]
    [Description("Busca lemas gregos no léxico unificado por caracteres gregos (com ou sem acentos/espíritos), transliteração ASCII (ex: 'agape', 'logos') ou glosa em inglês (ex: 'love', 'word').")]
    public async Task<string> SearchLemmaAsync(
        [Description("Termo de busca: grego politônico ou sem acentos (ex: 'λόγος', 'λογος'), transliteração ('logos') ou glosa em inglês ('word').")] string query,
        [Description("Se true (padrão), ativa busca aproximada (fuzzy) via pg_trgm para tolerar variações ortográficas.")] bool fuzzy = true,
        [Description("Máximo de resultados (1 a 50, padrão 15).")] int limit = 15,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "Informe um termo em `query` (grego, transliteração ou glosa em inglês).";

        var hits = await _repository.SearchLemmaAsync(query, fuzzy, limit, ct);
        return MarkdownFormatter.FormatSearchLemma(query, fuzzy, hits);
    }

    [McpServerTool(Name = "word_occurrences")]
    [Description("Concordância exata de um código Strong ou lema grego no Novo Testamento, retornando texto grego em contexto com a forma flexionada destacada e análise morfológica de Robinson decodificada em português.")]
    public async Task<string> WordOccurrencesAsync(
        [Description("Código Strong (ex: 'G3056', '3056') ou lema grego com ou sem acentos (ex: 'λόγος', 'αγαπη').")] string lemma_or_strong,
        [Description("Edição textual: 'f35' (Família 35 — padrão), 'tr' (Textus Receptus), 'byz' (Bizantino), 'na28', 'sbl' ou 'all'.")] string edition = "f35",
        [Description("Filtro opcional por livro do NT em português ou inglês (ex: 'Jo', 'João', 'Rom', '1Co', 'Ap').")] string? book = null,
        [Description("Quantidade de ocorrências por página (1 a 100, padrão 30).")] int limit = 30,
        [Description("Deslocamento para paginação (padrão 0).")] int offset = 0,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(lemma_or_strong))
            return "Informe um código Strong ou lema grego em `lemma_or_strong`.";

        try
        {
            var ed = ReferenceParser.ParseEdition(edition, BibleEdition.F35);
            var result = await _repository.GetWordOccurrencesAsync(lemma_or_strong, ed, book, limit, offset, ct);
            return MarkdownFormatter.FormatWordOccurrences(result, limit, offset);
        }
        catch (ArgumentException ex)
        {
            return $"Erro de parâmetro: {ex.Message}";
        }
    }

    [McpServerTool(Name = "parse_verse")]
    [Description("Análise palavra por palavra de um versículo ou faixa curta do NT (ex: 'Jo 1:1', 'João 1:1-5', '1Co 13:4'), retornando texto grego, lema, Strong, decodificação morfológica de Robinson em português e comparação de variantes entre Família 35, TR, Bizantino e NA28.")]
    public async Task<string> ParseVerseAsync(
        [Description("Referência bíblica em português ou inglês, versículo único ou faixa (ex: 'Jo 1:1', 'João 1:1-5', '1Co 13:4-7', 'Rom 8:28').")] string reference,
        [Description("Edição textual: 'f35' (Família 35 — padrão), 'tr' (Textus Receptus), 'byz' (Bizantino), 'na28' ou 'sbl'.")] string edition = "f35",
        [Description("Se true (padrão), inclui bloco de comparação textual entre Família 35, TR, Bizantino e NA28 quando houver variantes.")] bool include_variants = true,
        CancellationToken ct = default)
    {
        var (parsedRef, error) = ReferenceParser.Parse(reference);
        if (error is not null || parsedRef is null)
            return $"Referência inválida: {error}";

        var ed = ReferenceParser.ParseEdition(edition, BibleEdition.F35);
        var blocks = await _repository.ParseVerseRangeAsync(parsedRef, ed, include_variants, ct);
        return MarkdownFormatter.FormatParsedVerses(parsedRef, ed, include_variants, blocks);
    }
}
