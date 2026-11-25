using System.Xml.Linq;
using GreekNtMcp.Domain.Models;
using GreekNtMcp.Etl.Parsers;
using Xunit;

namespace GreekNtMcp.Tests;

public sealed class ParserTests
{
    [Fact]
    public void StrongsGreekParser_ParsesRealXmlEntry()
    {
        const string xml = """
            <strongsdictionary>
              <entries>
                <entry strongs="03056">
                  <strongs>3056</strongs>
                  <greek BETA="LO/GOS" unicode="λόγος" translit="lógos"/>
                  <pronunciation strongs="log'-os"/>
                  <strongs_derivation>from <strongsref language="GREEK" strongs="3004"/>;</strongs_derivation>
                  <strongs_def>something said (including the thought)</strongs_def>
                  <kjv_def>:--account, word.</kjv_def>
                </entry>
              </entries>
            </strongsdictionary>
            """;

        var entries = StrongsGreekParser.ParseDocument(XDocument.Parse(xml));
        var e = Assert.Single(entries);
        Assert.Equal("G3056", e.StrongCode);
        Assert.Equal("λόγος", e.Lemma);
        Assert.Equal("λογοσ", e.LemmaNormalized);
        Assert.Contains("[G3004]", e.StrongDerivation);
        Assert.Equal("account, word.", e.StrongKjvDefinition);
    }

    [Fact]
    public void AbbottSmithParser_ParsesCrossWireNamespaceAndExtractsLxxAndSenses()
    {
        const string xml = """
            <TEI xmlns="http://www.crosswire.org/2013/TEIOSIS/namespace">
              <text><body>
                <entry n="Ἀβαδδών|G3">
                  <note type="occurrencesNT">1</note>
                  <form><orth>Ἀβαδδών</orth> (Heb. <foreign xml:lang="heb" n="H11">אֲבַדֹּון</foreign>, <emph>destruction</emph>), indecl.;</form>
                  <sense n="1.">in NT, <gloss>Abaddon</gloss>: <ref osisRef="Rev.9.11">Re 9:11</ref>.</sense>
                </entry>
              </body></text>
            </TEI>
            """;

        var entries = AbbottSmithParser.ParseDocument(XDocument.Parse(xml));
        var e = Assert.Single(entries);
        Assert.Equal("G0003", e.StrongCode);
        Assert.Equal("Ἀβαδδών", e.Headword);
        Assert.Contains("אֲבַדֹּון (H11)", e.LxxEquivalents);
        Assert.Contains("**Abaddon**", e.FullMarkdown);
    }

    [Fact]
    public void HtmlToMarkdownConverter_PreservesLsjCitationsCleanly()
    {
        const string html = """<b> ἀγάπ-η</b>, ἡ, <br /> <b>love,</b> [<a href="javascript:void(0)" title=" LXX.Jer.2.2">LXX</a>]""";
        var full = HtmlToMarkdownConverter.Convert(html, includeAnchorTitles: true);
        var brief = HtmlToMarkdownConverter.Convert(html, includeAnchorTitles: false);

        Assert.Contains("**love,**", full);
        Assert.Contains("LXX.Jer.2.2", full);
        Assert.DoesNotContain("javascript:void(0)", full);
        Assert.DoesNotContain("LXX.Jer.2.2", brief);
    }

    [Fact]
    public void Family35Aligner_AlignsExactAndMovableNuAndRefusesToHallucinateUnknownVariant()
    {
        var tagntTokens = new List<NtToken>
        {
            new()
            {
                BookId = 40, Chapter = 1, Verse = 2, WordPosition = 1,
                SurfaceForm = "Ἀβραὰμ", SurfaceNormalized = "αβρααμ",
                StrongCode = "G0011", BaseStrongCode = "G0011", Lemma = "Ἀβραάμ", LemmaNormalized = "αβρααμ",
                MorphCode = "N-NSM-P", ContextualGloss = "Abraham", InByz = true, InTr = true
            },
            new()
            {
                BookId = 40, Chapter = 1, Verse = 2, WordPosition = 2,
                SurfaceForm = "ἐγέννησε", SurfaceNormalized = "εγεννησε", // without movable-nu
                StrongCode = "G1080", BaseStrongCode = "G1080", Lemma = "γεννάω", LemmaNormalized = "γενναω",
                MorphCode = "V-AAI-3S", ContextualGloss = "begat", InByz = true, InTr = true
            }
        };

        // F35 has movable-nu "ἐγέννησεν" + one synthetic unique word "ἀγνώστουτινος" not in TAGNT
        const string f35Xml = """
            <verseFile>
              <v b="MAT" c="1" v="2">Ἁβραὰμ ἐγέννησεν ἀγνώστουτινος.</v>
            </verseFile>
            """;

        var (verses, tokens, stats) = Family35ParserAndAligner.ParseAndAlignDocument(XDocument.Parse(f35Xml), tagntTokens);

        Assert.Single(verses);
        Assert.Equal(3, tokens.Count);

        // Token 1: Ἁβραὰμ matches Ἀβραὰμ (breathing mark normalized)
        Assert.Equal("exact_verse_match", tokens[0].AlignmentStatus);
        Assert.Equal("G0011", tokens[0].StrongCode);

        // Token 2: ἐγέννησεν matches ἐγέννησε via movable-nu equivalence
        Assert.Equal("exact_verse_match", tokens[1].AlignmentStatus);
        Assert.Equal("V-AAI-3S", tokens[1].MorphCode);

        // Token 3: unknown F35 variant MUST remain untagged (zero hallucination!)
        Assert.Equal("untagged_f35_variant", tokens[2].AlignmentStatus);
        Assert.Null(tokens[2].StrongCode);
        Assert.Null(tokens[2].MorphCode);
        Assert.Equal(1, stats.UntaggedVariants);
    }
}
