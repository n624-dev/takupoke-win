using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class OcrNativeLineHeaderTests
{
    [Fact]
    public void RepeatedNativeTermHeadersFollowActualSourceOrderAcrossBothDiscoveryPaths()
    {
        using var fixture = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "ocr-repeated-header-order.json")));
        var pages = System.Text.Json.JsonSerializer.Deserialize<PdfPageLayout[]>(fixture.RootElement.GetProperty("Pages").GetRawText())!;
        var unchanged = System.Text.Json.JsonSerializer.Serialize(pages);
        var document = RecoveryDocumentBuilder.Build(new string('a', 64), Takupoke.Core.MaterialKind.Timetable,
            pages, (_, _) => true, Enumerable.Range(1, 5).ToHashSet(), allowStructureProposal: true);
        Assert.Equal("前期", document.Term);
        Assert.Equal(Enumerable.Range(1, 5).SelectMany(p => new[] { $"p{p}s6", $"p{p}s7" }), document.TermEvidence);
        Assert.Equal(Enumerable.Range(1, 5).SelectMany(p => Enumerable.Range(0, 6).Select(s => $"p{p}s{s}")), document.YearEvidence);
        Assert.Equal(pages.Sum(p => p.Glyphs.Count), document.Sources.Count);
        Assert.Equal(unchanged, System.Text.Json.JsonSerializer.Serialize(pages));
    }

    private const string MissingYear = "年度の独立した見出しがありません。";
    private static HashSet<int> OcrPages => Enumerable.Range(1, 5).ToHashSet();

    [Fact]
    public async Task CapturedNativeLineRetainsOriginalAtomsAndFailsDownstreamAfterOwnedHeaderSelection()
    {
        var (pages, rasters, hash) = CapturedOcrHeaderFixture.Load();
        var document = CapturedOcrHeaderFixture.Build(pages, rasters, hash, OcrPages);

        Assert.Equal(2027, document.SchoolYear);
        Assert.Equal("前期", document.Term);
        Assert.Equal(40, document.RequiredSlots.Count);
        var structure = await RecoveryStructure.ResolveAsync(document, "windows", 10, [], default);
        // Ownership filtering removed the pending candidate; this is not an
        // adoption certificate. Complete coverage/evidence still fails closed.
        Assert.Equal(RecoveryJobState.Running, structure.State);
        Assert.Same(document, structure.Document);
        Assert.Empty(structure.Errors);
        Assert.DoesNotContain(document.Cells, RecoveryStructure.Pending);
        // Verified blank CTC context now resolves day ownership; incomplete
        // fixture body evidence still prevents both adoption and manual preparation.
        var expectedErrors = new[] { "coverage", "periodEvidence", "unclassifiedSource", "unassignedCellText" };
        Assert.Equal(expectedErrors, RecoveryValidator.InputErrors(document));
        var run = await RecoveryEngine.RunAsync(document, "windows", 10, true, [], _ => null);
        Assert.Equal(RecoveryJobState.Failed, run.State);
        Assert.Equal(expectedErrors, run.Errors);
        Assert.Null(run.Result);
        Assert.Null(document.Capture);
        Assert.Null(RecoveryManualAssistance.Prepare(document));
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
    [InlineData("valid", true)]
    [InlineData("reverseOrder", false)]
    [InlineData("crossRow", false)]
    [InlineData("crossRule", false)]
    [InlineData("foreignOverlap", false)]
    [InlineData("notRoleLabel", false)]
    public void ShortCodeCropCannotMoveBeforeItsProvedOriginalJapaneseContinuation(string mutation, bool proved)
    {
        PdfGlyph[] row = [new("教室：",10,179,30,24.7,0,0),new("架空室",70,179,50,24.7,0,1),
            new("3",130,180,4,21,0,2),new("B",140,180,4,21,0,3)];
        if(mutation=="reverseOrder")row[2]=row[2] with{SourceOrder=9};
        if(mutation=="crossRow")row[2]=row[2] with{Y=210};
        if(mutation=="notRoleLabel")row[0]=row[0] with{Text="説明："};
        if(mutation=="foreignOverlap")row=[..row,new("X",90,180,4,21,1,4)];
        PdfRule[] rules=mutation=="crossRule" ? [new(125,170,125,230)] : [];
        var page=new PdfPageLayout(300,250,row,rules);
        var ids=Takupoke.Infrastructure.Recovery.RecoveryDocumentBuilder.OriginalOcrSourceOrder([page]);
        var observed=ids.Select(id=>row[int.Parse(id.AsSpan(id.IndexOf('s')+1))]).ToArray();
        if(proved)Assert.Equal("教室：架空室3B",string.Concat(observed.Select(g=>g.Text)));
        else Assert.Equal(row.OrderBy(g=>Math.Round(g.Cy/2)).ThenBy(g=>g.Cx),observed);
        Assert.Equal(row.Length,ids.Distinct().Count());
        Assert.All(row,g=>Assert.Contains(g,observed)); // Original geometry/metadata untouched.
    }

    [Fact]
    public void OriginalWhitespaceBetweenExplicitYearAndTermKeepsItsHeaderOwnership()
    {
        var (pages,rasters,hash)=CapturedOcrHeaderFixture.Load();
        var originalCount=pages.Sum(p=>p.Glyphs.Count);
        pages=EachPage(pages,InsertTitleGap);
        var document=CapturedOcrHeaderFixture.Build(pages,rasters,hash,OcrPages);
        var spaces=document.Sources.Where(s=>s.Text==" ").ToArray();
        Assert.Equal(5,spaces.Length);Assert.Equal(originalCount+5,document.Sources.Count);
        Assert.All(spaces,space=>{Assert.True(space.FromOcr);Assert.Contains(space.Id,document.YearEvidence);});
        Assert.Equal(2027,document.SchoolYear);Assert.Equal("前期",document.Term);
        Assert.DoesNotContain("yearEvidenceText",RecoveryValidator.InputErrors(document));
    }

    private static PdfPageLayout InsertTitleGap(PdfPageLayout page)
    {
        var line=YearLine(page);var next=line.First(g=>g.Text=="前");
        var previous=line.Last(g=>g.SourceOrder<next.SourceOrder);
        var left=previous.X+previous.Width;var gap=next.X-left;
        Assert.True(gap>0);
        var space=new PdfGlyph(" ",left+gap/4,next.Y,gap/2,next.Height,next.SourceLine,next.SourceOrder);
        var glyphs=new List<PdfGlyph>();
        foreach(var glyph in page.Glyphs)
        {
            if(ReferenceEquals(glyph,next))glyphs.Add(space);
            glyphs.Add(glyph.SourceOrder>=next.SourceOrder ? glyph with{SourceOrder=glyph.SourceOrder+1}:glyph);
        }
        return page with{Glyphs=glyphs};
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
