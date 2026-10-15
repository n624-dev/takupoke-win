using Takupoke.Core;
using Takupoke.Infrastructure.Parsing;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class PdfVisibleUnicodeRecoveryTests
{
    private const string Map="1 begincodespacerange <0000> <ffff> endcodespacerange 2 beginbfchar <0001> <0041> <0002> <0042> endbfchar";
    private static byte[] Pdf(string prefix="",string suffix="",bool known=true) =>
        PdfMissingUnicodeRecoveryTests.Pdf(PdfMissingUnicodeRecoveryTests.Font(),prefix:prefix,suffix:suffix,toUnicode:known?Map:null);

    [Theory]
    [InlineData(MaterialKind.Timetable)][InlineData(MaterialKind.Exam)][InlineData(MaterialKind.ExamReturn)]
    public void BoundedFillAndStrokeUsesOriginalExplicitUnicodeAndIndependentInk(MaterialKind kind)
    {
        var bytes=Pdf(".1 w 2 Tr ");
        Assert.Equal("P01",Assert.Throws<PdfParseException>(()=>PdfPigLayoutReader.Read(bytes,kind)).Stage);
        var capture=new RecoveryReadCapture();
        var page=Assert.Single(PdfPigLayoutReader.ReadWithBoundedVisibleUnicode(bytes,kind,default,capture));
        Assert.True(capture.Complete); Assert.Equal("AB",string.Concat(page.Glyphs.Select(g=>g.Text)));
        Assert.All(page.Glyphs,g=>Assert.Null(g.FontEvidence));
        Assert.Equal(new[]{0,1},page.Glyphs.Select(g=>g.SourceOrder!.Value));
        Assert.True(page.Glyphs[0].InkBounds!.Value.Left<20); // Stroke ink is retained beyond advance bounds.
    }

    [Fact]
    public void DisjointThinFilledRulesAfterTextDoNotInvalidateTheWholePage()
    {
        var bytes=Pdf(suffix:"0 g 60 30 1 100 re f");
        Assert.Throws<PdfParseException>(()=>PdfPigLayoutReader.Read(bytes,MaterialKind.Timetable));
        var capture=new RecoveryReadCapture();
        var page=Assert.Single(PdfPigLayoutReader.ReadWithBoundedVisibleUnicode(bytes,MaterialKind.Timetable,default,capture));
        Assert.True(capture.Complete); Assert.Equal("AB",string.Concat(page.Glyphs.Select(g=>g.Text)));
        Assert.Contains(page.Lines,l=>l.Vertical&&l.X1==60.5);
    }

    [Fact]
    public void ActualThinPaintExtentCanBeDisjointWhereTheConservativeLegacyMarginOverlaps()
    {
        var bytes=Pdf(suffix:"0 g 19.1 90 .2 30 re f");
        Assert.Throws<PdfParseException>(()=>PdfPigLayoutReader.Read(bytes,MaterialKind.Timetable));
        var capture=new RecoveryReadCapture();
        var page=Assert.Single(PdfPigLayoutReader.ReadWithBoundedVisibleUnicode(bytes,MaterialKind.Timetable,default,capture));
        Assert.True(capture.Complete);Assert.Equal("AB",string.Concat(page.Glyphs.Select(g=>g.Text)));
    }

    [Fact]
    public void OriginalRoundJoinHasItsOwnBoundedStrokeEnvelope()
    {
        var capture=new RecoveryReadCapture();
        var page=Assert.Single(PdfPigLayoutReader.ReadWithBoundedVisibleUnicode(Pdf(".1 w 1 j 2 Tr"),MaterialKind.Timetable,default,capture));
        Assert.True(capture.Complete);Assert.Equal(19.95,page.Glyphs[0].InkBounds!.Value.Left,6);
    }

    [Fact]
    public void EarlierGridAtGlyphEdgeDoesNotBecomeLaterOverpaint()
    {
        const string rule="20 106.9 m 40 106.9 l S ";
        var capture=new RecoveryReadCapture();
        var page=Assert.Single(PdfPigLayoutReader.ReadWithBoundedVisibleUnicode(Pdf(prefix:rule),MaterialKind.Timetable,default,capture));
        Assert.True(capture.Complete);Assert.Equal("AB",string.Concat(page.Glyphs.Select(g=>g.Text)));
        var later=Assert.Single(PdfPigLayoutReader.ReadWithBoundedVisibleUnicode(Pdf(suffix:rule),MaterialKind.Timetable,default,new()));
        Assert.Contains(later.Glyphs,g=>g.RequiresVisualReview);
    }

    [Fact]
    public void EarlierRuleCannotHideTheWholeOriginalFilledGlyph()
    {
        var bytes=PdfMissingUnicodeRecoveryTests.Pdf(PdfMissingUnicodeRecoveryTests.Font(),
            prefix:"2 w 14 100.35 m 40 100.35 l S ",options:"/F1 1 Tf",toUnicode:Map);
        var capture=new RecoveryReadCapture();
        Assert.Throws<PdfParseException>(()=>PdfPigLayoutReader.ReadWithBoundedVisibleUnicode(bytes,MaterialKind.Timetable,default,capture));
        Assert.False(capture.Complete);
    }

    [Theory]
    [InlineData("1 g 10 80 80 40 re f")]
    [InlineData("0 g 10 80 80 40 re f")]
    public void LaterPaintCrossingOriginalInkOrOpaqueBlocksStillRefuses(string suffix)
    {
        var capture=new RecoveryReadCapture();
        Assert.Throws<PdfParseException>(()=>PdfPigLayoutReader.ReadWithBoundedVisibleUnicode(Pdf(suffix:suffix),MaterialKind.Timetable,default,capture));
        Assert.False(capture.Complete);
    }

    [Fact]
    public void OnePartialLaterGridIntersectionRequiresOriginalPixelReview()
    {
        var capture=new RecoveryReadCapture();
        var page=Assert.Single(PdfPigLayoutReader.ReadWithBoundedVisibleUnicode(Pdf(suffix:"0 g 22 90 1 30 re f"),MaterialKind.Timetable,default,capture));
        Assert.True(capture.Complete);Assert.True(capture.Pages[0].NativeInkAccounted);
        Assert.Contains(page.Glyphs,g=>g.RequiresVisualReview);
        Assert.Equal("AB",string.Concat(page.Glyphs.Select(g=>g.Text)));
    }

    [Theory]
    [InlineData("q 0 0 200 200 re W n", "Q")]
    [InlineData("q 0 0 200 200 re W* n", "Q")]
    [InlineData("/Artifact BMC", "EMC")]
    [InlineData("1 g 10 80 80 40 re f 0 g", "")]
    public void FullyContainedNativeInkSupportsRectangleClipsAndPlainArtifactOrEarlierWhiteBackground(string prefix,string suffix)
    {
        var capture=new RecoveryReadCapture();
        var page=Assert.Single(PdfPigLayoutReader.ReadWithBoundedVisibleUnicode(Pdf(prefix,suffix),MaterialKind.Timetable,default,capture));
        Assert.True(capture.Complete);Assert.All(page.Glyphs,g=>Assert.False(g.RequiresVisualReview));
        Assert.Equal("AB",string.Concat(page.Glyphs.Select(g=>g.Text)));
    }

    [Theory]
    [InlineData("q 25 80 65 40 re W n", "Q")]
    [InlineData("/Artifact BMC", "")]
    [InlineData("", "EMC")]
    [InlineData("/OC BMC", "EMC")]
    [InlineData("10 80 m 90 80 l 20 120 l h W n", "")]
    [InlineData("q 0 0 200 200 re W n Q q 25 80 65 40 re W n", "Q")]
    public void ClippedOrUnbalancedNativeTextCannotBePromoted(string prefix,string suffix)
    {
        var capture=new RecoveryReadCapture();
        Assert.Throws<PdfParseException>(()=>PdfPigLayoutReader.ReadWithBoundedVisibleUnicode(PdfMissingUnicodeRecoveryTests.Pdf(PdfMissingUnicodeRecoveryTests.Font(),prefix:prefix,suffix:suffix=="Q"?"":suffix,toUnicode:Map,beforeRules:suffix=="Q"?"Q":""),MaterialKind.Timetable,default,capture));
        Assert.False(capture.Complete);
    }

    [Theory]
    [InlineData("q 10 80 80 40 re W n", "Q")]
    [InlineData("q 25 80 65 40 re W n Q", "")]
    [InlineData("/Part << /MCID 7 >> BDC", "EMC")]
    [InlineData(".7 g 10 80 80 40 re f 0 g", "")]
    [InlineData(".7 .8 .9 rg 10 80 80 40 re f 0 g", "")]
    public void ClipRestorationAndNonSemanticMetadataOrContrastingGrayBackgroundPreserveNativeText(string prefix,string beforeRules)
    {
        var bytes=PdfMissingUnicodeRecoveryTests.Pdf(PdfMissingUnicodeRecoveryTests.Font(),prefix:prefix,toUnicode:Map,beforeRules:beforeRules);
        var capture=new RecoveryReadCapture();
        var page=Assert.Single(PdfPigLayoutReader.ReadWithBoundedVisibleUnicode(bytes,MaterialKind.Timetable,default,capture));
        Assert.True(capture.Complete);Assert.All(page.Glyphs,g=>Assert.False(g.RequiresVisualReview));
    }

    [Theory]
    [InlineData(".7 g 10 80 80 40 re f .65 g", "")]
    [InlineData("0 g 10 80 80 40 re f 0 g", "")]
    [InlineData("", ".7 g 10 80 80 40 re f")]
    [InlineData("/Part << /MCID 7 /ActualText (forged) >> BDC", "EMC")]
    [InlineData("/Part << /MCID -1 >> BDC", "EMC")]
    [InlineData("/OC << /MCID 7 >> BDC", "EMC")]
    public void InsufficientBackgroundContrastAndSemanticReplacementAreStillRefused(string prefix,string suffix)
    {
        var capture=new RecoveryReadCapture();
        Assert.Throws<PdfParseException>(()=>PdfPigLayoutReader.ReadWithBoundedVisibleUnicode(Pdf(prefix,suffix),MaterialKind.Timetable,default,capture));
        Assert.False(capture.Complete);
    }

    [Fact]
    public void PartialOriginalRectangleClipRequiresReviewInsteadOfPretendingFullVisibility()
    {
        var bytes=PdfMissingUnicodeRecoveryTests.Pdf(PdfMissingUnicodeRecoveryTests.Font(),prefix:"q 22 80 68 40 re W n",toUnicode:Map,beforeRules:"Q");
        var capture=new RecoveryReadCapture();
        var page=Assert.Single(PdfPigLayoutReader.ReadWithBoundedVisibleUnicode(bytes,MaterialKind.Timetable,default,capture));
        Assert.True(capture.Complete);Assert.Contains(page.Glyphs,g=>g.RequiresVisualReview);
    }

    [Theory]
    [InlineData("1 g")][InlineData("0 g")]
    public void EntirelyCoveredOriginalGlyphIsNotExposedAsVisibleSource(string colour)
    {
        var capture=new RecoveryReadCapture();
        var page=Assert.Single(PdfPigLayoutReader.ReadWithBoundedVisibleUnicode(Pdf(suffix:colour+" 19 99 6 9 re f"),MaterialKind.Timetable,default,capture));
        Assert.True(capture.Complete);Assert.Equal("B",string.Concat(page.Glyphs.Select(g=>g.Text)));
        Assert.Equal(1,Assert.Single(page.Glyphs).SourceOrder);
    }

    [Fact]
    public void OrdinaryImportReusesOnlyCompletelyProvenVisibleNativeText()
    {
        var bytes=Pdf(".1 w 2 Tr");
        Assert.Throws<PdfParseException>(()=>PdfPigLayoutReader.Read(bytes,MaterialKind.Timetable));
        Assert.Equal("AB",string.Concat(Assert.Single(PdfPigLayoutReader.ReadForImport(bytes,MaterialKind.Timetable)).Glyphs.Select(g=>g.Text)));
        Assert.Throws<PdfParseException>(()=>PdfPigLayoutReader.ReadForImport(Pdf(suffix:"0 g 22 90 1 30 re f"),MaterialKind.Timetable));
    }

    [Theory]
    [InlineData("0 w 2 Tr")][InlineData("1 w 2 Tr")][InlineData(".1 w 1 Tr")]
    [InlineData(".1 w 3 Tr")][InlineData("1 g .1 w 2 Tr")][InlineData("1 G .1 w 2 Tr")]
    [InlineData("100 M .1 w 2 Tr")][InlineData("3 J .1 w 2 Tr")][InlineData("3 j .1 w 2 Tr")]
    [InlineData("10 10 40 40 re W n .1 w 2 Tr")]
    [InlineData("/Tag << /ActualText (forged) >> BDC")]
    public void BoundedRecoveryCannotOverrideUnprovedVisibility(string prefix)
    {
        var capture=new RecoveryReadCapture();
        Assert.Throws<PdfParseException>(()=>PdfPigLayoutReader.ReadWithBoundedVisibleUnicode(Pdf(prefix),MaterialKind.Timetable,default,capture));
        Assert.False(capture.Complete);
    }

    [Fact]
    public void MissingUnicodeIsNotFilledByThisExplicitUnicodeEntry()
    {
        var capture=new RecoveryReadCapture();
        Assert.Throws<PdfParseException>(()=>PdfPigLayoutReader.ReadWithBoundedVisibleUnicode(Pdf(".1 w 2 Tr",known:false),MaterialKind.Timetable,default,capture));
        Assert.False(capture.Complete);
    }
}
