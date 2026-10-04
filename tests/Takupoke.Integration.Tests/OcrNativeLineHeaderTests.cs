using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class OcrNativeLineHeaderTests
{
    private const string MissingYear = "年度の独立した見出しがありません。";
    private static HashSet<int> OcrPages => Enumerable.Range(1, 5).ToHashSet();

    [Fact]
    public async Task CapturedNativeLineRetainsOriginalAtomsAndExistingStructureFailures()
    {
        var (pages, rasters, hash) = CapturedOcrHeaderFixture.Load();
        var document = CapturedOcrHeaderFixture.Build(pages, rasters, hash, OcrPages);

        Assert.Equal(2027, document.SchoolYear);
        Assert.Equal("前期", document.Term);
        Assert.Equal(40, document.RequiredSlots.Count);
        var structure = await RecoveryStructure.ResolveAsync(document, "windows", 10, [], default);
        Assert.Equal(RecoveryJobState.Failed, structure.State);
        Assert.Null(structure.Document);
        Assert.Equal(new[] { "unassignedCellText", "dayBinding", "periodBinding" }, structure.Errors);
        Assert.Equal(pages.Sum(page => page.Glyphs.Count), document.Sources.Count);
        var sources = document.Sources.ToDictionary(source => source.Id);
        foreach (var (page, index) in pages.Select((page, index) => (page, index)))
        foreach (var (glyph, atom) in page.Glyphs.Select((glyph, atom) => (glyph, atom)))
        {
            var source = sources[$"p{index + 1}s{atom}"];
            Assert.Equal(glyph.Text, source.Text);
            Assert.Equal(Box(glyph), source.Box);
            Assert.Equal(glyph.SourceLine, source.SourceLine);
            Assert.Equal(glyph.SourceOrder, source.SourceOrder);
            Assert.True(source.FromOcr);
        }
        Assert.Equal(Enumerable.Range(1, 5).SelectMany(page => Enumerable.Range(3, 6).Select(atom => $"p{page}s{atom}")), document.YearEvidence);
        Assert.Equal(Enumerable.Range(1, 5).SelectMany(page => new[] { $"p{page}s9", $"p{page}s10" }), document.TermEvidence);
        Assert.Equal(document.YearEvidence.Count, document.YearEvidence.Distinct().Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeMetadataCannotEnableSupplementalHeadersForVectorPages(bool noPageSet)
    {
        var (pages, rasters, hash) = CapturedOcrHeaderFixture.Load();
        var error = Assert.Throws<InvalidDataException>(() =>
            CapturedOcrHeaderFixture.Build(pages, rasters, hash, noPageSet ? null : new HashSet<int>()));
        Assert.Equal(MissingYear, error.Message);
    }

    [Fact]
    public void YearSuffixOnAnotherNativeLineIsNotJoined()
        => RejectYear(pages => EachPage(pages, page => MapYear(page,
            glyph => glyph.Text == "度" ? glyph with { SourceLine = 999 } : glyph)));

    [Fact]
    public void NativeLineIdentityReusedOnDifferentRowsIsRejected()
        => RejectYear(pages => EachPage(pages, page =>
        {
            var lower = page.Glyphs.First(glyph => glyph.Y > 35);
            var line = YearLine(page);
            return page with { Glyphs = page.Glyphs.Select(glyph => ReferenceEquals(glyph, lower)
                ? glyph with { SourceLine = line[0].SourceLine, SourceOrder = line[^1].SourceOrder + 1 } : glyph).ToArray() };
        }));

    [Theory]
    [InlineData("duplicate")]
    [InlineData("reversed")]
    [InlineData("missing")]
    [InlineData("gap")]
    public void InvalidNativeOrderIsNotSortedOrRepaired(string mutation)
        => RejectYear(pages => EachPage(pages, page => MapYear(page, glyph =>
            glyph.Text != "年" ? glyph : glyph with { SourceOrder = mutation switch
            {
                "duplicate" => 6,
                "reversed" => 2,
                "missing" => null,
                _ => 99
            } })));

    [Fact]
    public void PhysicalRuleCrossingNativeHeaderRejectsSupplementalYear()
        => RejectYear(pages => EachPage(pages, page => page with
        { Lines = page.Lines.Append(new PdfRule(60, 0, 60, 30)).ToArray() }));

    [Fact]
    public void IndependentLegacyHeadersRemainAvailableWhenNativeLineCrossesRule()
    {
        var (pages, rasters, hash) = CapturedOcrHeaderFixture.Load();
        pages = EachPage(pages, page => AddLegacyHeaders(page) with
        { Lines = page.Lines.Append(new PdfRule(60, 0, 60, 30)).ToArray() });
        var document = CapturedOcrHeaderFixture.Build(pages, rasters, hash, OcrPages);
        Assert.Equal(2027, document.SchoolYear);
        Assert.Equal("前期", document.Term);
        var sources = document.Sources.ToDictionary(source => source.Id);
        Assert.All(document.YearEvidence, id => Assert.Equal("2027年度", sources[id].Text));
        Assert.Equal(5, document.YearEvidence.Count);
    }

    [Fact]
    public void OtherNativeLineOverlappingHeaderIsNotIgnored()
        => RejectYear(pages => EachPage(pages, page =>
        {
            var first = YearLine(page)[0];
            return page with { Glyphs = page.Glyphs.Append(first with
                { Text = "X", SourceLine = 999, SourceOrder = 999 }).ToArray() };
        }));

    [Fact]
    public void RulesOnlyTouchingNativeHeaderBoundaryKeepOriginalYear()
    {
        var (pages, rasters, hash) = CapturedOcrHeaderFixture.Load();
        pages = EachPage(pages, page =>
        {
            var line = YearLine(page);
            var left = line.Min(glyph => glyph.X); var top = line.Min(glyph => glyph.Y);
            return page with { Lines = page.Lines.Concat(new[]
            { new PdfRule(left, 0, left, 30), new PdfRule(0, top, 120, top) }).ToArray() };
        });
        var document = CapturedOcrHeaderFixture.Build(pages, rasters, hash, OcrPages);
        Assert.Equal(2027, document.SchoolYear);
        Assert.Equal("前期", document.Term);
        Assert.Equal(30, document.YearEvidence.Count);
    }

    [Fact]
    public void WholeAtomicYearAndTermCannotBeSplitIntoInventedAtoms()
        => RejectYear(pages => EachPage(pages, page =>
        {
            var line = YearLine(page);
            var first = line[0];
            var whole = first with { Text = "2027年度前期", Width = line.Max(glyph => glyph.X + glyph.Width) - first.X };
            return page with { Glyphs = page.Glyphs.Where(glyph => glyph.SourceLine != first.SourceLine).Append(whole).ToArray() };
        }));

    [Fact]
    public void ObservedWrongYearStaysWrongWithoutFixtureGoldRepair()
    {
        var (pages, rasters, hash) = CapturedOcrHeaderFixture.Load();
        pages = EachPage(pages, page =>
        {
            var first = YearLine(page)[0];
            return page with { Glyphs = page.Glyphs.Select(glyph => ReferenceEquals(glyph, first)
                ? glyph with { Text = "9" } : glyph).ToArray() };
        });
        var document = CapturedOcrHeaderFixture.Build(pages, rasters, hash, OcrPages);
        Assert.Equal(9027, document.SchoolYear);
        var sources = document.Sources.ToDictionary(source => source.Id);
        Assert.Equal("9027年度", string.Concat(document.YearEvidence.Where(id => sources[id].Page == 1).Select(id => sources[id].Text)));
    }

    [Fact]
    public void DifferentObservedYearsRemainAmbiguous()
        => RejectYear(pages => pages.Select((page, index) => index != 1 ? page : MapYear(page,
            glyph => glyph.Text == "7" ? glyph with { Text = "8" } : glyph)).ToArray());

    [Fact]
    public void LegacyAndSupplementalHeadersDoNotDuplicateEvidenceAtoms()
    {
        var (pages, rasters, hash) = CapturedOcrHeaderFixture.Load();
        pages = EachPage(pages, AddLegacyHeaders);
        var document = CapturedOcrHeaderFixture.Build(pages, rasters, hash, OcrPages);
        Assert.Equal(35, document.YearEvidence.Count);
        Assert.Equal(document.YearEvidence.Count, document.YearEvidence.Distinct().Count());
        Assert.Equal(15, document.TermEvidence.Count);
        Assert.Equal(document.TermEvidence.Count, document.TermEvidence.Distinct().Count());
    }

    private static void RejectYear(Func<PdfPageLayout[], PdfPageLayout[]> mutate)
    {
        var (pages, rasters, hash) = CapturedOcrHeaderFixture.Load();
        var error = Assert.Throws<InvalidDataException>(() =>
            CapturedOcrHeaderFixture.Build(mutate(pages), rasters, hash, OcrPages));
        Assert.Equal(MissingYear, error.Message);
    }

    private static PdfPageLayout[] EachPage(PdfPageLayout[] pages, Func<PdfPageLayout, PdfPageLayout> mutate)
        => pages.Select(mutate).ToArray();

    private static PdfGlyph[] YearLine(PdfPageLayout page)
    {
        var line = page.Glyphs.Single(glyph => glyph.Y < 35 && glyph.Text == "度").SourceLine;
        var glyphs = page.Glyphs.Where(glyph => glyph.SourceLine == line).ToArray();
        Assert.Equal("2027年度前期", string.Concat(glyphs.Select(glyph => glyph.Text)));
        return glyphs;
    }

    private static PdfPageLayout MapYear(PdfPageLayout page, Func<PdfGlyph, PdfGlyph> mutate)
    {
        var line = YearLine(page)[0].SourceLine;
        return page with { Glyphs = page.Glyphs.Select(glyph => glyph.SourceLine == line ? mutate(glyph) : glyph).ToArray() };
    }

    private static PdfPageLayout AddLegacyHeaders(PdfPageLayout page)
        => page with { Glyphs = page.Glyphs.Concat(new[]
        { new PdfGlyph("2027年度", 140, 5, 50, 8, 998, 0), new PdfGlyph("前期", 195, 5, 20, 8, 999, 0) }).ToArray() };

    private static RecoveryBox Box(PdfGlyph glyph) => new(glyph.X, glyph.Y, glyph.Width, glyph.Height);
}
