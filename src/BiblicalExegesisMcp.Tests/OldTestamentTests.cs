using System.Text;
using System.Xml.Linq;
using BiblicalExegesisMcp.Domain.Models;
using BiblicalExegesisMcp.Domain.Morphology;
using BiblicalExegesisMcp.Domain.Normalization;
using BiblicalExegesisMcp.Domain.References;
using BiblicalExegesisMcp.Etl.Parsers;
using Xunit;

namespace BiblicalExegesisMcp.Tests;

public sealed class OldTestamentTests
{
    [Theory]
    [InlineData("בְּרֵאשִׁ֖ית", "בראשית")]
    [InlineData("אֱלֹהִ֑ים", "אלהימ")]
    [InlineData("הָ/אָֽרֶץ\\׃", "הארצ")]
    [InlineData("חֶסֶד", "חסד")]
    public void HebrewNormalizer_StripsNiqqudTeAmimAndUnifiesSofit(string input, string expected)
    {
        Assert.True(HebrewNormalizer.ContainsHebrew(input));
        Assert.Equal(expected, HebrewNormalizer.NormalizeHebrew(input));
    }

    [Fact]
    public void HebrewNormalizer_CleansTahotSurfaceAndPunctuation()
    {
        var (surface, seg, punc) = HebrewNormalizer.CleanTahotSurface("הָ/אָֽרֶץ\\׃");
        Assert.Equal("הָאָֽרֶץ", surface);
        Assert.Equal("הָ/אָֽרֶץ", seg);
        Assert.Equal("׃", punc);
    }

    [Theory]
    [InlineData("H1", "H0001", "H0001", false)]
    [InlineData("h7225", "H7225", "H7225", false)]
    [InlineData("H7225G", "H7225G", "H7225", true)]
    [InlineData("H1254a", "H1254A", "H1254", true)]
    public void StrongCodeNormalizer_HandlesHebrewStrongs(string raw, string expectedCode, string expectedBase, bool expectedExt)
    {
        var parts = StrongCodeNormalizer.TryNormalize(raw);
        Assert.NotNull(parts);
        Assert.True(parts!.IsHebrew);
        Assert.Equal(expectedCode, parts.StrongCode);
        Assert.Equal(expectedBase, parts.BaseStrongCode);
        Assert.Equal(expectedExt, parts.IsExtended);
    }

    [Fact]
    public void StrongCodeNormalizer_ParsesTahotBracedStrongsAndExpandedLemma()
    {
        var (primary, affixes, lemma) = StrongCodeNormalizer.ParseTahotStrongField(
            "H9003/{H7225G}\\H9016",
            "H7225G",
            "H9003=ב=in/{H7225G=רֵאשִׁית=: beginning»first:1_beginning}\\H9016=׃=verseEnd");

        Assert.Equal("H7225G", primary?.StrongCode);
        Assert.Equal("H7225", primary?.BaseStrongCode);
        Assert.Contains("H9003", affixes);
        Assert.DoesNotContain("H9016", affixes);
        Assert.Equal("רֵאשִׁית", lemma);
    }

    [Theory]
    [InlineData("Gn 1:1-3", "Gen", true, 1, 1, 3)]
    [InlineData("Gênesis 1:1", "Gen", true, 1, 1, 1)]
    [InlineData("Sl 23:1-6", "Psa", true, 23, 1, 6)]
    [InlineData("Jó 19:25", "Job", true, 19, 25, 25)]
    [InlineData("Jo 1:1", "Jhn", false, 1, 1, 1)]
    [InlineData("1Sm 17:45", "1Sa", true, 17, 45, 45)]
    [InlineData("Is 53:5", "Isa", true, 53, 5, 5)]
    [InlineData("Dn 7:13", "Dan", true, 7, 13, 13)]
    [InlineData("Ml 4:1", "Mal", true, 4, 1, 1)]
    public void ReferenceParser_ResolvesAll66BooksAndDistinguishesJobVsJohn(
        string input, string expectedOsis, bool expectedOt, short ch, short v1, short v2)
    {
        var (reference, error) = ReferenceParser.Parse(input);
        Assert.Null(error);
        Assert.NotNull(reference);
        Assert.Equal(expectedOsis, reference!.Book.OsisCode);
        Assert.Equal(expectedOt, reference.Book.IsOldTestament);
        Assert.Equal(ch, reference.Chapter);
        Assert.Equal(v1, reference.StartVerse);
        Assert.Equal(v2, reference.EndVerse);

        var defaultEd = ReferenceParser.ParseEditionForBook(null, reference.Book);
        Assert.Equal(expectedOt ? BibleEdition.Mt : BibleEdition.F35, defaultEd);
    }

    [Theory]
    [InlineData("HR/Ncfsa", "Hebraico: Preposição + Substantivo, Comum, Feminino, Singular, Absoluto")]
    [InlineData("HVqp3ms", "Hebraico: Verbo, Qal, Perfeito (Qatal), 3ª Pessoa, Masculino, Singular")]
    [InlineData("Hc/Vqw3ms", "Hebraico: Conjunção (Waw Consecutivo) + Verbo, Qal, Imperfeito Sequencial (Wayyiqtol), 3ª Pessoa, Masculino, Singular")]
    [InlineData("HTd/Ncmpa", "Hebraico: Artigo Definido + Substantivo, Comum, Masculino, Plural, Absoluto")]
    [InlineData("AVqp3ms", "Aramaico: Verbo, Peal, Perfeito (Qatal), 3ª Pessoa, Masculino, Singular")]
    public void OshmMorphDecoder_DecodesHebrewAndAramaicToPortuguese(string code, string expected)
    {
        Assert.Equal(expected, OshmMorphDecoder.Decode(code));
    }

    [Fact]
    public void StrongsHebrewParser_And_BdbLexiconParser_ParseOpenScripturesXml()
    {
        const string strongXml = """
            <lexicon xmlns="http://openscriptures.github.com/morphhb/namespace">
              <entry id="H6">
                <w pos="v" pron="aw-bad'" xlit="ʼâbad" xml:lang="heb">אָבַד</w>
                <source>a primitive root;</source>
                <meaning>properly, to <def>wander</def> away</meaning>
                <usage>destroy, perish.</usage>
              </entry>
            </lexicon>
            """;

        var strongs = StrongsHebrewParser.ParseDocument(XDocument.Parse(strongXml));
        var s = Assert.Single(strongs);
        Assert.Equal("H0006", s.StrongCode);
        Assert.Equal("heb", s.Language);
        Assert.Equal("אבד", s.LemmaNormalized);
        Assert.Contains("**wander**", s.StrongDefinition);

        const string idxXml = """
            <index xmlns="http://openscriptures.github.com/morphhb/namespace">
              <part xml:lang="heb">
                <entry id="aaf">
                  <w xlit="ʾābad">אָבַד</w> <pos>V</pos> <def>perish</def>
                  <xref bdb="a.ac.aa" strong="6" twot="2"/>
                  <etym root="אבד" type="main">aag</etym>
                </entry>
              </part>
            </index>
            """;

        const string bdbXml = """
            <lexicon xmlns="http://openscriptures.github.com/morphhb/namespace">
              <part id="a" title="א" xml:lang="heb">
                <section id="a.ac">
                  <entry id="a.ac.aa" type="root">
                    <w>אָבַד</w> <pos>vb</pos>. <def>perish</def>
                    <sense><stem>Qal</stem> <sense n="1"><def>die</def> <ref r="Num.17.27">Nu 17:27</ref></sense></sense>
                    <status p="1">done</status>
                  </entry>
                </section>
              </part>
            </lexicon>
            """;

        var (byBdb, byStrong) = BdbLexiconParser.ParseLexicalIndexDocument(XDocument.Parse(idxXml));
        Assert.Equal("אבד", byStrong["H0006"].RootHebrew);
        Assert.Equal("2", byStrong["H0006"].TwotRef);

        var bdbs = BdbLexiconParser.ParseBdbDocument(XDocument.Parse(bdbXml), byBdb);
        var b = Assert.Single(bdbs);
        Assert.Equal("H0006", b.StrongCode);
        Assert.Contains("**[Qal]**", b.FullMarkdown);
        Assert.Contains("**die**", b.FullMarkdown);
        Assert.DoesNotContain("done", b.FullMarkdown);
    }

    [Fact]
    public void TahotParser_HandlesQereKetivAndBhsVersificationShift()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp,
                """
                Eng (Heb) Ref & Type	Hebrew	Transliteration	Translation	dStrongs	Grammar	Meaning Variants	Spelling Variants	Root dStrong+Instance	Alternative Strongs+Instance	Conjoin word	Expanded Strong tags
                Gen.1.1#01=L	בְּ/רֵאשִׁ֖ית	be./re.Shit	in/ beginning	H9003/{H7225G}	HR/Ncfsa			H7225G			H9003=ב=in/{H7225G=רֵאשִׁית=: beginning}
                Psa.51.1(51.3)#01=Q	חָנֵּ֣נִי	chon.Ne.ni	be gracious to me	{H2603A}	HVqv2ms/Sp1cs	Qere reading		H2603A			{H2603A=חָנַן=to be gracious}
                Psa.51.1(51.3)#01=K	חנני	ch.n.n.y	be gracious to me	{H2603A}	HVqv2ms/Sp1cs	Ketiv spelling		H2603A			{H2603A=חָנַן=to be gracious}
                """);

            var tokens = TahotParser.StreamFile(tmp).ToList();
            Assert.Equal(3, tokens.Count);

            Assert.True(tokens[0].InMt);
            Assert.True(tokens[0].InKetiv);
            Assert.Equal("H7225G", tokens[0].StrongCode);
            Assert.Equal("בְּרֵאשִׁ֖ית".Normalize(NormalizationForm.FormC), tokens[0].SurfaceForm);

            // Qere line
            Assert.Equal("Psa.51.3", tokens[1].HebrewRef);
            Assert.True(tokens[1].InMt);
            Assert.False(tokens[1].InKetiv);

            // Ketiv line
            Assert.Equal("Psa.51.3", tokens[2].HebrewRef);
            Assert.False(tokens[2].InMt);
            Assert.True(tokens[2].InKetiv);
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}
