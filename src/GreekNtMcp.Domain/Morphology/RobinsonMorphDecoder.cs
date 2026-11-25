namespace GreekNtMcp.Domain.Morphology;

/// <summary>
/// Deterministic decoder for Robinson / Tauber / STEPBible Greek NT morphology codes into academic Portuguese.
/// Never guesses or hallucinates; preserves the exact original code alongside its grammatical expansion.
/// </summary>
public static class RobinsonMorphDecoder
{
    private static readonly Dictionary<string, string> PosLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["N"] = "Substantivo",
        ["V"] = "Verbo",
        ["A"] = "Adjetivo",
        ["T"] = "Artigo Definido",
        ["P"] = "Pronome Pessoal",
        ["R"] = "Pronome Relativo",
        ["C"] = "Pronome Recíproco",
        ["D"] = "Pronome Demonstrativo",
        ["X"] = "Pronome Indefinido",
        ["I"] = "Pronome Interrogativo",
        ["F"] = "Pronome Reflexivo",
        ["S"] = "Pronome Possessivo",
        ["K"] = "Pronome Correlativo",
        ["Q"] = "Pronome Correlativo/Interrogativo",
        ["ADV"] = "Advérbio",
        ["CONJ"] = "Conjunção",
        ["COND"] = "Partícula Condicional",
        ["PRT"] = "Partícula",
        ["PREP"] = "Preposição",
        ["INJ"] = "Interjeição",
        ["ARAM"] = "Termo Aramaico (transliterado)",
        ["HEB"] = "Termo Hebraico (transliterado)",
        ["N-PRI"] = "Nome Próprio Indeclinável",
        ["N-LI"] = "Letra Indeclinável",
        ["N-OI"] = "Substantivo Indeclinável",
        ["A-NUI"] = "Numeral Indeclinável"
    };

    private static readonly Dictionary<char, string> Cases = new()
    {
        ['N'] = "Nominativo",
        ['G'] = "Genitivo",
        ['D'] = "Dativo",
        ['A'] = "Acusativo",
        ['V'] = "Vocativo"
    };

    private static readonly Dictionary<char, string> Numbers = new()
    {
        ['S'] = "Singular",
        ['P'] = "Plural"
    };

    private static readonly Dictionary<char, string> Genders = new()
    {
        ['M'] = "Masculino",
        ['F'] = "Feminino",
        ['N'] = "Neutro"
    };

    private static readonly Dictionary<string, string> Tenses = new(StringComparer.OrdinalIgnoreCase)
    {
        ["P"] = "Presente",
        ["I"] = "Imperfeito",
        ["F"] = "Futuro",
        ["2F"] = "2º Futuro",
        ["A"] = "Aoristo",
        ["2A"] = "2º Aoristo",
        ["X"] = "Perfeito",
        ["2X"] = "2º Perfeito",
        ["Y"] = "Mais-que-perfeito",
        ["2Y"] = "2º Mais-que-perfeito"
    };

    private static readonly Dictionary<char, string> Voices = new()
    {
        ['A'] = "Ativa",
        ['M'] = "Média",
        ['P'] = "Passiva",
        ['E'] = "Média ou Passiva",
        ['D'] = "Média Depoente",
        ['O'] = "Passiva Depoente",
        ['N'] = "Média ou Passiva Depoente",
        ['Q'] = "Impessoal Ativa",
        ['X'] = "Sem Voz Declarada"
    };

    private static readonly Dictionary<char, string> Moods = new()
    {
        ['I'] = "Indicativo",
        ['S'] = "Subjuntivo",
        ['O'] = "Optativo",
        ['M'] = "Imperativo",
        ['N'] = "Infinitivo",
        ['P'] = "Particípio",
        ['R'] = "Imperativo (Particípio)"
    };

    private static readonly Dictionary<string, string> ExtraSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["C"] = "Comparativo",
        ["S"] = "Superlativo",
        ["N"] = "Negativo",
        ["I"] = "Interrogativo",
        ["K"] = "Crase",
        ["ATT"] = "Forma Ática",
        ["ABB"] = "Abreviado",
        ["P"] = "Partícula Anexada",
        ["PRI"] = "Nome Próprio Indeclinável",
        ["LI"] = "Letra Indeclinável",
        ["OI"] = "Indeclinável",
        ["NUI"] = "Numeral Indeclinável",
        ["LG"] = "Nome de Lugar",
        ["PG"] = "Nome de Pessoa",
        ["T"] = "Título"
    };

    public static string Decode(string? morphCode)
    {
        if (string.IsNullOrWhiteSpace(morphCode))
            return "—";

        var code = morphCode.Trim().ToUpperInvariant();
        if (PosLabels.TryGetValue(code, out var exact))
            return exact;

        var parts = code.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return morphCode;

        var pos = parts[0];
        var result = new List<string>
        {
            PosLabels.TryGetValue(pos, out var posName) ? posName : pos
        };

        if (pos == "V" && parts.Length >= 2)
        {
            DecodeVerb(parts, result);
            return string.Join(", ", result);
        }

        // Indeclinable compound suffixes (e.g. N-PRI, N-LI, ADV-N, CONJ-N, ADV-C)
        for (int i = 1; i < parts.Length; i++)
        {
            var seg = parts[i];
            if (ExtraSuffixes.TryGetValue(seg, out var suffixLabel))
            {
                result.Add(suffixLabel);
                continue;
            }

            DecodeNominalBlock(seg, result);
        }

        return string.Join(", ", result);
    }

    public static string FormatWithCode(string? morphCode)
    {
        if (string.IsNullOrWhiteSpace(morphCode))
            return "—";

        var decoded = Decode(morphCode);
        return $"`{morphCode}` ({decoded})";
    }

    private static void DecodeVerb(string[] parts, List<string> result)
    {
        var tvm = parts[1];
        int idx = 0;

        // Parse tense (may start with '2' for 2nd Aorist / 2nd Future / 2nd Perfect / 2nd Pluperfect)
        string tenseKey;
        if (tvm.Length >= 2 && tvm[0] == '2')
        {
            tenseKey = tvm[..2];
            idx = 2;
        }
        else if (tvm.Length >= 1)
        {
            tenseKey = tvm[..1];
            idx = 1;
        }
        else
        {
            tenseKey = string.Empty;
        }

        var tvmDesc = new List<string>();
        if (Tenses.TryGetValue(tenseKey, out var tense))
            tvmDesc.Add(tense);

        if (idx < tvm.Length && Voices.TryGetValue(tvm[idx], out var voice))
        {
            tvmDesc.Add(voice);
            idx++;
        }

        if (idx < tvm.Length && Moods.TryGetValue(tvm[idx], out var mood))
        {
            tvmDesc.Add(mood);
        }

        if (tvmDesc.Count > 0)
            result.Add(string.Join(' ', tvmDesc));

        // Remaining segments: person+number (e.g. 3S, 1P) or participle case+number+gender (e.g. NSM, GPF) or suffix (ATT)
        for (int i = 2; i < parts.Length; i++)
        {
            var seg = parts[i];
            if (ExtraSuffixes.TryGetValue(seg, out var suffix))
            {
                result.Add(suffix);
                continue;
            }

            if (seg.Length == 2 && seg[0] is '1' or '2' or '3' && Numbers.TryGetValue(seg[1], out var num))
            {
                result.Add($"{seg[0]}ª Pessoa do {num}");
                continue;
            }

            DecodeNominalBlock(seg, result);
        }
    }

    private static void DecodeNominalBlock(string seg, List<string> result)
    {
        var idx = 0;

        // Optional person prefix on personal/reflexive/possessive pronouns (e.g. 1GS, 2DP, 3ASM, 1NS, 2PP)
        if (idx < seg.Length && seg[idx] is '1' or '2' or '3')
        {
            result.Add($"{seg[idx]}ª Pessoa");
            idx++;
        }

        // Possessive pronouns may carry possessor number before case (e.g. S-1PNSN = 1st pl possessor, Nom Sg Neut)
        if (idx + 3 < seg.Length && Numbers.TryGetValue(seg[idx], out var possNum) && Cases.ContainsKey(seg[idx + 1]))
        {
            result.Add($"Possuidor {possNum}");
            idx++;
        }

        var cng = new List<string>();
        if (idx < seg.Length && Cases.TryGetValue(seg[idx], out var cas))
        {
            cng.Add(cas);
            idx++;
        }
        if (idx < seg.Length && Numbers.TryGetValue(seg[idx], out var num))
        {
            cng.Add(num);
            idx++;
        }
        if (idx < seg.Length && Genders.TryGetValue(seg[idx], out var gen))
        {
            cng.Add(gen);
            idx++;
        }

        if (cng.Count > 0)
            result.Add(string.Join(' ', cng));

        if (idx < seg.Length)
        {
            var rem = seg[idx..];
            if (ExtraSuffixes.TryGetValue(rem, out var extra))
                result.Add(extra);
        }
    }
}
