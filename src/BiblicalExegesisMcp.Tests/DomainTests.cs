using BiblicalExegesisMcp.Domain.Models;
using BiblicalExegesisMcp.Domain.Morphology;
using BiblicalExegesisMcp.Domain.Normalization;
using BiblicalExegesisMcp.Domain.References;
using Xunit;

namespace BiblicalExegesisMcp.Tests;

public sealed class DomainTests
{
    [Theory]
    [InlineData("λόγος", "λογοσ")]
    [InlineData("ΛΌΓΟΣ", "λογοσ")]
    [InlineData("λογος", "λογοσ")]
    [InlineData("ἀγάπη", "αγαπη")]
    [InlineData("ἀρχῇ", "αρχη")]
    [InlineData("ᾤχετο", "ωχετο")]
    [InlineData("Ἀβραάμ·", "αβρααμ")]
    public void GreekNormalizer_StripsDiacriticsAndUnifiesSigma(string input, string expected)
    {
        Assert.Equal(expected, GreekNormalizer.NormalizeGreek(input));
    }

    [Fact]
    public void GreekNormalizer_SplitsBoundaryPunctuation()
    {
        var (before, core, after) = GreekNormalizer.SplitPunctuation("«Ἀβραάμ·»");
        Assert.Equal("«", before);
        Assert.Equal("Ἀβραάμ", core);
        Assert.Equal("·»", after);
    }

    [Theory]
    [InlineData("3056", "G3056", "G3056", false)]
    [InlineData("G3056", "G3056", "G3056", false)]
    [InlineData("g26", "G0026", "G0026", false)]
    [InlineData("G0025a", "G0025A", "G0025", true)]
    [InlineData("G2264G", "G2264G", "G2264", true)]
    [InlineData("G6010", "G6010", "G6010", true)]
    public void StrongCodeNormalizer_NormalizesAllFormats(string raw, string expectedCode, string expectedBase, bool expectedExt)
    {
        var parts = StrongCodeNormalizer.TryNormalize(raw);
        Assert.NotNull(parts);
        Assert.Equal(expectedCode, parts!.StrongCode);
        Assert.Equal(expectedBase, parts.BaseStrongCode);
        Assert.Equal(expectedExt, parts.IsExtended);
    }

    [Fact]
    public void StrongCodeNormalizer_ParsesTagntCompoundsAndCrossMappedStrongs()
    {
        var (primary1, sec1, morph1) = StrongCodeNormalizer.ParseTagntGrammarField("H1732|G1138«G1138=N-GSM-P");
        Assert.Equal("G1138", primary1?.StrongCode);
        Assert.Equal("N-GSM-P", morph1);
        Assert.Empty(sec1);

        var (primary2, sec2, morph2) = StrongCodeNormalizer.ParseTagntGrammarField("G2532=CONJ + G1473=P-1NS");
        Assert.Equal("G2532", primary2?.StrongCode);
        Assert.Equal("CONJ", morph2);
        Assert.Contains("G1473", sec2);
    }

    [Theory]
    [InlineData("Jo 1:1", "Jhn", 1, 1, 1)]
    [InlineData("João 1.1-5", "Jhn", 1, 1, 5)]
    [InlineData("1Co 13:4-7", "1Co", 13, 4, 7)]
    [InlineData("1 Coríntios 13:4", "1Co", 13, 4, 4)]
    [InlineData("Ap 22:21", "Rev", 22, 21, 21)]
    [InlineData("Rom 8:28", "Rom", 8, 28, 28)]
    [InlineData("1Jo 5:7-8", "1Jn", 5, 7, 8)]
    public void ReferenceParser_ParsesPortugueseAndEnglishReferences(
        string input, string expectedOsis, short ch, short v1, short v2)
    {
        var (reference, error) = ReferenceParser.Parse(input);
        Assert.Null(error);
        Assert.NotNull(reference);
        Assert.Equal(expectedOsis, reference!.Book.OsisCode);
        Assert.Equal(ch, reference.Chapter);
        Assert.Equal(v1, reference.StartVerse);
        Assert.Equal(v2, reference.EndVerse);
    }

    [Fact]
    public void ReferenceParser_DefaultsToFamily35Edition()
    {
        Assert.Equal(BibleEdition.F35, ReferenceParser.ParseEdition(null));
        Assert.Equal(BibleEdition.F35, ReferenceParser.ParseEdition("f35"));
        Assert.Equal(BibleEdition.Tr, ReferenceParser.ParseEdition("tr"));
        Assert.Equal(BibleEdition.Byz, ReferenceParser.ParseEdition("byz"));
        Assert.Equal(BibleEdition.Na28, ReferenceParser.ParseEdition("na28"));
    }

    [Theory]
    [InlineData("V-AAI-3S", "Verbo, Aoristo Ativa Indicativo, 3ª Pessoa do Singular")]
    [InlineData("V-2AAP-NSM", "Verbo, 2º Aoristo Ativa Particípio, Nominativo Singular Masculino")]
    [InlineData("N-GSF", "Substantivo, Genitivo Singular Feminino")]
    [InlineData("T-NSM", "Artigo Definido, Nominativo Singular Masculino")]
    [InlineData("P-1GS", "Pronome Pessoal, 1ª Pessoa, Genitivo Singular")]
    [InlineData("CONJ", "Conjunção")]
    [InlineData("PREP", "Preposição")]
    public void RobinsonMorphDecoder_DecodesToAcademicPortuguese(string code, string expected)
    {
        Assert.Equal(expected, RobinsonMorphDecoder.Decode(code));
    }
}
