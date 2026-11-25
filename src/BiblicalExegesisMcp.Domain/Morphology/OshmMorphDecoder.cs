namespace BiblicalExegesisMcp.Domain.Morphology;

/// <summary>
/// Deterministic decoder for OSHM / STEPBible TEHMC Hebrew and Aramaic morphology codes into academic Portuguese.
/// Handles compound slash-separated segments (prefixes + main stem + pronominal/paragogic suffixes)
/// such as "HR/Ncfsa", "HVqp3ms", "Hc/Vqw3ms", "HR/Ncmsc/Sp3ms", and Aramaic "AVqp3ms".
/// </summary>
public static class OshmMorphDecoder
{
    private static readonly Dictionary<char, string> HebrewStems = new()
    {
        ['q'] = "Qal",
        ['N'] = "Niphal",
        ['p'] = "Piel",
        ['P'] = "Pual",
        ['h'] = "Hiphil",
        ['H'] = "Hophal",
        ['t'] = "Hithpael",
        ['o'] = "Polel",
        ['O'] = "Polal",
        ['r'] = "Hithpolel",
        ['m'] = "Poel",
        ['M'] = "Poal",
        ['k'] = "Palel",
        ['K'] = "Pulal",
        ['Q'] = "Qal Passivo",
        ['l'] = "Pilpel",
        ['L'] = "Polpal",
        ['f'] = "Hithpalpel",
        ['D'] = "Nithpael",
        ['j'] = "Pealal",
        ['i'] = "Pilel",
        ['u'] = "Hothpaal",
        ['c'] = "Tiphil",
        ['v'] = "Hishtaphel",
        ['w'] = "Hithpaal",
        ['y'] = "Nithpael",
        ['z'] = "Hithpoel"
    };

    private static readonly Dictionary<char, string> AramaicStems = new()
    {
        ['q'] = "Peal",
        ['Q'] = "Peil",
        ['u'] = "Hithpeel",
        ['p'] = "Pael",
        ['P'] = "Ithpaal",
        ['M'] = "Hithpaal",
        ['a'] = "Aphel",
        ['h'] = "Haphel",
        ['s'] = "Saphel",
        ['e'] = "Shaphel",
        ['H'] = "Hophal",
        ['i'] = "Ithpeel",
        ['t'] = "Ithpaal",
        ['v'] = "Ishtaphal",
        ['w'] = "Ittaphal",
        ['o'] = "Polel",
        ['z'] = "Ithpoel",
        ['r'] = "Hithpolel",
        ['f'] = "Hithpalpel",
        ['b'] = "Pealal",
        ['c'] = "Palpel",
        ['m'] = "Poel",
        ['l'] = "Palel",
        ['L'] = "Ithpalel",
        ['O'] = "Ithpolal",
        ['G'] = "Ittaphal",
        ['K'] = "Palel",
        ['N'] = "Niphal"
    };

    private static readonly Dictionary<char, string> VerbConjugations = new()
    {
        ['p'] = "Perfeito (Qatal)",
        ['q'] = "Perfeito Sequencial (Weqatal)",
        ['i'] = "Imperfeito (Yiqtol)",
        ['w'] = "Imperfeito Sequencial (Wayyiqtol)",
        ['h'] = "Coortativo",
        ['j'] = "Jussivo",
        ['v'] = "Imperativo",
        ['r'] = "Particípio Ativo",
        ['s'] = "Particípio Passivo",
        ['a'] = "Infinitivo Absoluto",
        ['c'] = "Infinitivo Construto"
    };

    private static readonly Dictionary<char, string> NounTypes = new()
    {
        ['c'] = "Comum",
        ['g'] = "Gentílico",
        ['p'] = "Próprio"
    };

    private static readonly Dictionary<char, string> AdjectiveTypes = new()
    {
        ['a'] = "Adjetivo",
        ['c'] = "Numeral Cardinal",
        ['g'] = "Gentílico",
        ['o'] = "Numeral Ordinal"
    };

    private static readonly Dictionary<char, string> PronounTypes = new()
    {
        ['d'] = "Demonstrativo",
        ['f'] = "Indefinido",
        ['i'] = "Interrogativo",
        ['p'] = "Pessoal",
        ['r'] = "Relativo"
    };

    private static readonly Dictionary<char, string> ParticleTypes = new()
    {
        ['a'] = "Afirmativa",
        ['d'] = "Artigo Definido",
        ['e'] = "Exortativa",
        ['i'] = "Interrogativa",
        ['j'] = "Interjeição",
        ['m'] = "Demonstrativa",
        ['n'] = "Negativa",
        ['o'] = "Marcador de Objeto Direto (ʾēt)",
        ['r'] = "Relativa"
    };

    private static readonly Dictionary<char, string> SuffixTypes = new()
    {
        ['d'] = "He Direcional",
        ['h'] = "He Paragógico",
        ['n'] = "Nun Paragógico",
        ['p'] = "Sufixo Pronominal"
    };

    private static readonly Dictionary<char, string> Genders = new()
    {
        ['m'] = "Masculino",
        ['f'] = "Feminino",
        ['b'] = "Ambos os Gêneros",
        ['c'] = "Comum"
    };

    private static readonly Dictionary<char, string> Numbers = new()
    {
        ['s'] = "Singular",
        ['p'] = "Plural",
        ['d'] = "Dual"
    };

    private static readonly Dictionary<char, string> States = new()
    {
        ['a'] = "Absoluto",
        ['c'] = "Construto",
        ['d'] = "Determinado (Enfático)"
    };

    public static bool IsAramaic(string? morphCode) =>
        !string.IsNullOrWhiteSpace(morphCode) && morphCode.TrimStart().StartsWith('A');

    public static string Decode(string? morphCode)
    {
        if (string.IsNullOrWhiteSpace(morphCode))
            return "—";

        var trimmed = morphCode.Trim();
        bool isAramaic = trimmed[0] == 'A';
        bool hasLangPrefix = trimmed[0] is 'H' or 'A';
        var body = hasLangPrefix ? trimmed[1..] : trimmed;

        var segments = body.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return morphCode;

        var decodedSegments = segments.Select(seg => DecodeSegment(seg, isAramaic)).ToList();
        var langLabel = isAramaic ? "Aramaico" : "Hebraico";
        return $"{langLabel}: {string.Join(" + ", decodedSegments)}";
    }

    public static string FormatWithCode(string? morphCode)
    {
        if (string.IsNullOrWhiteSpace(morphCode))
            return "—";

        return $"`{morphCode}` ({Decode(morphCode)})";
    }

    private static string DecodeSegment(string seg, bool isAramaic)
    {
        if (string.IsNullOrEmpty(seg))
            return seg;

        // STEPBible lowercase 'c' denotes Waw consecutive conjunction (e.g. Hc/Vqw3ms)
        if (seg == "c")
            return "Conjunção (Waw Consecutivo)";
        if (seg == "C")
            return "Conjunção";
        if (seg == "D")
            return "Advérbio";

        var pos = seg[0];
        var rest = seg.Length > 1 ? seg[1..] : string.Empty;

        return pos switch
        {
            'V' => DecodeVerb(rest, isAramaic),
            'N' => DecodeNoun(rest),
            'A' => DecodeAdjective(rest),
            'P' => DecodePronoun(rest),
            'R' => DecodePreposition(rest),
            'T' => DecodeParticle(rest),
            'S' => DecodeSuffix(rest),
            'C' => "Conjunção",
            'D' => "Advérbio",
            _ => seg
        };
    }

    private static string DecodeVerb(string rest, bool isAramaic)
    {
        var parts = new List<string> { "Verbo" };
        if (rest.Length == 0)
            return "Verbo";

        var stemMap = isAramaic ? AramaicStems : HebrewStems;
        if (stemMap.TryGetValue(rest[0], out var stem))
            parts.Add(stem);

        if (rest.Length >= 2 && VerbConjugations.TryGetValue(rest[1], out var conj))
            parts.Add(conj);

        if (rest.Length >= 3)
            AppendPgns(rest[2..], parts);

        return string.Join(", ", parts);
    }

    private static string DecodeNoun(string rest)
    {
        var parts = new List<string> { "Substantivo" };
        if (rest.Length == 0)
            return "Substantivo";

        if (NounTypes.TryGetValue(rest[0], out var nType))
            parts.Add(nType);

        if (rest.Length >= 2)
            AppendPgns(rest[1..], parts);

        return string.Join(", ", parts);
    }

    private static string DecodeAdjective(string rest)
    {
        if (rest.Length == 0)
            return "Adjetivo";

        var label = AdjectiveTypes.TryGetValue(rest[0], out var aType) ? aType : "Adjetivo";
        var parts = new List<string> { label };
        if (rest.Length >= 2)
            AppendPgns(rest[1..], parts);

        return string.Join(", ", parts);
    }

    private static string DecodePronoun(string rest)
    {
        var parts = new List<string> { "Pronome" };
        if (rest.Length == 0)
            return "Pronome";

        if (PronounTypes.TryGetValue(rest[0], out var pType))
            parts.Add(pType);

        if (rest.Length >= 2)
            AppendPgns(rest[1..], parts);

        return string.Join(", ", parts);
    }

    private static string DecodePreposition(string rest)
    {
        if (rest == "d")
            return "Preposição + Artigo Definido";
        return "Preposição";
    }

    private static string DecodeParticle(string rest)
    {
        if (rest.Length == 0)
            return "Partícula";

        if (ParticleTypes.TryGetValue(rest[0], out var tLabel))
        {
            // 'Td' is standalone "Artigo Definido" for clarity
            return rest[0] == 'd' ? tLabel : $"Partícula {tLabel}";
        }

        return "Partícula";
    }

    private static string DecodeSuffix(string rest)
    {
        if (rest.Length == 0)
            return "Sufixo";

        var label = SuffixTypes.TryGetValue(rest[0], out var sLabel) ? sLabel : "Sufixo";
        var parts = new List<string> { label };
        if (rest.Length >= 2)
            AppendPgns(rest[1..], parts);

        return string.Join(", ", parts);
    }

    private static void AppendPgns(string tail, List<string> parts)
    {
        int idx = 0;

        // Person: 1, 2, 3
        if (idx < tail.Length && tail[idx] is '1' or '2' or '3')
        {
            parts.Add($"{tail[idx]}ª Pessoa");
            idx++;
        }

        // Gender: m, f, b, c
        if (idx < tail.Length && Genders.TryGetValue(tail[idx], out var gender))
        {
            parts.Add(gender);
            idx++;
        }

        // Number: s, p, d
        if (idx < tail.Length && Numbers.TryGetValue(tail[idx], out var number))
        {
            parts.Add(number);
            idx++;
        }

        // State: a, c, d
        if (idx < tail.Length && States.TryGetValue(tail[idx], out var state))
        {
            parts.Add(state);
        }
    }
}
