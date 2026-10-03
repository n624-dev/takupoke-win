using System.Globalization;
using System.Text;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class PdfParsingTests
{
    private static PdfFont Font => new(new(1, new Dictionary<int, string> { [65] = "架", [66] = "空", [32] = " " }), new Dictionary<int, double> { [65] = 500, [66] = 500, [32] = 250 }, 0, 800, -200);
    [Fact]
    public void StrictDrawingGeometryUsesMetricsSpacingAndVerifiedText()
    {
        var text = new PdfTextEngine(); text.Operation("BT"); text.SetFont(Font, 10); text.Operation("Tm", 1, 0, 0, 1, 20, 100);
        text.Operation("Tw", 3); text.Show("A B"u8); text.Operation("ET");
        var glyphs = text.Finish("架 空");
        Assert.Equal(2, glyphs.Count); Assert.Equal(20, glyphs[0].X); Assert.Equal(30.5, glyphs[1].X); Assert.Equal(98, glyphs[0].Y); Assert.Equal(10, glyphs[0].Height);
    }
    [Fact]
    public void RejectsMissingUnicodeInvisibleTextUnfinishedTextAndMismatch()
    {
        var text = new PdfTextEngine(); text.Operation("BT"); text.SetFont(Font, 10);
        Assert.Throws<PdfParseException>(() => text.Show("C"u8));
        Assert.Throws<PdfParseException>(() => text.Operation("Tr", 3));
        text.Show("A"u8);
        Assert.Throws<PdfParseException>(() => text.Finish("架"));
        text.Operation("ET"); Assert.Throws<PdfParseException>(() => text.Finish("架空"));
    }
    [Fact]
    public void CompositeFontsUseDefaultWidthWhenIndividualWidthsAreMissing()
    {
        var text = new PdfTextEngine(); text.Operation("BT");
        text.SetFont(new(new(2, new Dictionary<int, string> { [65] = "架", [66] = "空" }), new Dictionary<int, double>(), 1000, 800, -200), 10);
        text.Show(new byte[] { 0, 65, 0, 66 }); text.Operation("ET");
        Assert.Equal(10, text.Finish("架空")[1].X);
    }
    [Fact]
    public void CmapDecodesBfcharAndSequentialRanges()
    {
        var map = PdfUnicodeMap.Read("1 begincodespacerange <00> <ff> endcodespacerange 1 beginbfchar <41> <67b6> endbfchar 1 beginbfrange <42> <43> <0042> endbfrange"u8.ToArray());
        Assert.Equal("架", map.Values[65]); Assert.Equal("B", map.Values[66]); Assert.Equal("C", map.Values[67]);
    }
    [Theory]
    [InlineData("usecmap")]
    [InlineData("/WMode 1")]
    [InlineData("1 beginbfchar <41> <d800> endbfchar")]
    [InlineData("2 beginbfchar <41> <0041> <41> <0042> endbfchar")]
    public void CmapRejectsUnsupportedOrAmbiguousMappings(string content)
    {
        Assert.Throws<PdfParseException>(() => PdfUnicodeMap.Read(Encoding.ASCII.GetBytes("1 begincodespacerange <00> <ff> endcodespacerange " + content)));
    }
    [Fact]
    public void ContentOrderSurvivesOverlappingCharacterBounds()
    {
        var glyphs = new[] { new PdfGlyph("架", 10, 10, 9, 10, 1, 0), new PdfGlyph("空", 12, 10, 4, 10, 1, 1) };
        var grid = new PdfGrid(new(100, 100, glyphs, [new(0, 0, 0, 100)]));
        Assert.Equal("架空", Assert.Single(grid.TimetableText(new(0, 0, 50, 50))));
    }
    [Fact]
    public void DisjointFragmentsMergeButContainedOrBackwardsFragmentsAreRejected()
    {
        PdfGrid Grid(double secondX, double secondWidth, int secondOrder) => new(new(100, 100,
            [new("架", 10, 10, 10, 10, 1, 3), new("空", secondX, 10, secondWidth, 10, 2, secondOrder)], [new(0, 0, 0, 100)]));
        Assert.Equal("架空", Assert.Single(Grid(22, 10, 4).TimetableText(new(0, 0, 50, 50))));
        Assert.Equal("P20", Assert.Throws<PdfParseException>(() => Grid(12, 4, 4).TimetableText(new(0, 0, 50, 50))).Stage);
        Assert.Equal("P20", Assert.Throws<PdfParseException>(() => Grid(18, 10, 2).TimetableText(new(0, 0, 50, 50))).Stage);
    }
    [Fact]
    public void TimetablePreservesParallelLessonsAndEmptyRoom()
    {
        var analysis = PdfScheduleParser.Timetable([TimetableLayout("科A・科B", "教A・教B", "室A・")]);
        Assert.Equal(2032, analysis.SchoolYear); Assert.Equal("前期", analysis.Term); Assert.Equal(2, analysis.Lessons.Count);
        Assert.Equal("科B", analysis.Lessons[1].Names.Subject); Assert.Equal("", analysis.Lessons[1].Names.Room);
        Assert.Equal("1_CN", analysis.Lessons[0].ClassName);
    }
    [Fact]
    public void TimetableCannotShiftRoomIntoMissingTeacher()
    {
        var page = TimetableLayout("架空科目", "架空教員", "架空教室");
        page = page with { Glyphs = page.Glyphs.Where(g => g.Text != "架空教員").ToArray() };
        Assert.Equal("P17", Assert.Throws<PdfParseException>(() => PdfScheduleParser.Timetable([page])).Stage);
    }
    [Fact]
    public void AnnotationInAnUnusedTableRowCannotTurnTeacherIntoSubject()
    {
        var glyphs = new List<PdfGlyph> { new("令和14年度前期時間割", 30, 20, 100, 10) };
        for (var index = 0; index < 40; index++) glyphs.Add(new((index % 8 + 1).ToString(CultureInfo.InvariantCulture), 42 + index * 10, 70, 5, 10));
        glyphs.AddRange([new("CN", 25, 210, 8, 10), new("1", 5, 210, 8, 10), new("教員だけ", 41, 215, 8, 4),
            new("注記一", 61, 165, 8, 4), new("注記二", 61, 175, 8, 4), new("注記三", 61, 185, 8, 4)]);
        var lines = new List<PdfRule>();
        foreach (var y in new[] { 60, 90, 140, 190, 240 }) lines.Add(new(0, y, 440, y));
        lines.Add(new(0, 60, 0, 240)); lines.Add(new(20, 60, 20, 240));
        for (var index = 0; index <= 40; index++) lines.Add(new(40 + index * 10, 60, 40 + index * 10, 240));
        Assert.Equal("P17", Assert.Throws<PdfParseException>(() => PdfScheduleParser.Timetable([new(500, 500, glyphs, lines)])).Stage);
    }
    [Fact]
    public void AWholeTableOutsideTheVisiblePageCannotBeAccepted()
    {
        var page = TimetableLayout("架空科目", "架空教員", "架空教室");
        var shifted = page with { Glyphs = page.Glyphs.Select(g => g with { X = g.X + 1000 }).ToArray(),
            Lines = page.Lines.Select(l => l with { X1 = l.X1 + 1000, X2 = l.X2 + 1000 }).ToArray() };
        Assert.Equal("P01", Assert.Throws<PdfParseException>(() => PdfScheduleParser.Timetable([shifted])).Stage);
        foreach (var kind in new[] { MaterialKind.Timetable, MaterialKind.Exam, MaterialKind.ExamReturn })
        {
            var capture = new RecoveryReadCapture();
            Assert.Equal("P01", Assert.Throws<PdfParseException>(() => PdfPigLayoutReader.Read(SyntheticPdf(prefix: "1 0 0 1 1000 0 cm "), kind, capture: capture)).Stage);
            Assert.False(capture.Complete); Assert.DoesNotContain(capture.Pages, p => p.State == RecoveryInputState.Complete);
        }
    }
    [Fact] public void ShortCellCalibrationPreservesTeacherHoleAndRejectsTeacherOnlyEvenWithOutsideAnnotation()
    {
        PdfGrid Grid(bool teacherOnly)
        {
            var glyphs = new List<PdfGlyph> { new("基準科目", 110, 5, 20, 4), new("基準教員", 110, 15, 20, 4), new("基準教室", 110, 25, 20, 4),
                new("注記一", 210, 15, 20, 4), new("注記二", 210, 25, 20, 4), new("注記三", 210, 35, 20, 4) };
            if (teacherOnly) glyphs.Add(new("教員だけ", 10, 15, 20, 4)); else { glyphs.Add(new("科目だけ", 10, 5, 20, 4)); glyphs.Add(new("教室あり", 10, 25, 20, 4)); }
            var grid = new PdfGrid(new(300, 100, glyphs, [new(0, 0, 300, 0), new(0, 100, 300, 100), new(0, 0, 0, 100), new(100, 0, 100, 100), new(200, 0, 200, 100), new(300, 0, 300, 100)]));
            grid.SetLessonArea(new(0, 0, 200, 100)); return grid;
        }
        Assert.Equal(new[] { "科目だけ", "", "教室あり" }, Grid(false).LessonFields(new(0, 0, 100, 100)));
        Assert.Equal("P17", Assert.Throws<PdfParseException>(() => Grid(true).LessonFields(new(0, 0, 100, 100))).Stage);
    }
    [Theory] [InlineData("担当教員")] [InlineData("教員名")] [InlineData("教師名")] [InlineData("授業名")] [InlineData("会場")] public void PartialRoleLabelInSecondParallelLessonCannotBypassRecoveryConfirmation(string alias)
    {
        Assert.Equal("P17", Assert.Throws<PdfParseException>(() => PdfScheduleParser.Timetable([TimetableLayout("科A・科B", "教A・" + alias + ":教B", "室A・室B")])).Stage);
    }
    [Fact]
    public void TimetableRejectsAmbiguousParallelPairing()
    {
        Assert.Equal("P18", Assert.Throws<PdfParseException>(() => PdfScheduleParser.Timetable([TimetableLayout("科A・科B", "教A・教B", "室A")])).Stage);
    }
    [Theory]
    [InlineData("ﾒﾃﾞﾞｨｱ", "ﾒﾃﾞｨｱ")]
    [InlineData("ﾞﾞ", "ﾞﾞ")]
    [InlineData("Aﾞﾞ", "Aﾞﾞ")]
    public void CollapsesOnlyRepeatedVoicingMarksFollowingHalfwidthKana(string source, string expected) => Assert.Equal(expected, PdfScheduleParser.CollapseRoomMarks(source));
    [Fact]
    public void PaintedThinRectanglesProvideRulesWithoutBroadHighlightEdges()
    {
        var paths = new PdfPathEngine(new(0, 0, 100, 100, 0));
        paths.Operation("re", [10, 10, 1, 50]); paths.Operation("f", []);
        paths.Operation("re", [20, 20, 50, 30]); paths.Operation("f", []);
        var rule = Assert.Single(paths.Finish()); Assert.True(rule.Vertical); Assert.Equal(10.5, rule.X1);
    }
    [Fact]
    public void ReaderVerifiesACompleteSyntheticPdfWithExplicitUnicodeAndFontMetrics()
    {
        var page = Assert.Single(PdfPigLayoutReader.Read(SyntheticPdf(), MaterialKind.Timetable));
        Assert.Equal("架空", string.Concat(page.Glyphs.Select(g => g.Text)));
        Assert.Equal(4, page.Lines.Count);
    }
    [Theory]
    [InlineData(MaterialKind.Timetable)]
    [InlineData(MaterialKind.Exam)]
    [InlineData(MaterialKind.ExamReturn)]
    public void ReaderAcceptsFullyOpaqueGraphicsStateForAllScheduleKinds(MaterialKind kind)
    {
        var page = Assert.Single(PdfPigLayoutReader.Read(SyntheticPdf(graphicsState: "/ca 1 /CA 1 /BM /Normal"), kind));
        Assert.Equal("架空", string.Concat(page.Glyphs.Select(g => g.Text)));
        Assert.Equal(4, page.Lines.Count);
    }
    [Theory]
    [InlineData("/ca 0")]
    [InlineData("/CA 0")]
    [InlineData("/SMask /None")]
    [InlineData("/TR /Identity")]
    [InlineData("/TR2 /Identity")]
    [InlineData("/BM /Multiply")]
    [InlineData("/BM /Screen")]
    [InlineData("/BM [/Normal]")]
    public void SpecialReaderRejectsInvisibleOrUnsupportedGraphicsState(string state)
    {
        foreach (var kind in new[] { MaterialKind.Timetable, MaterialKind.Exam, MaterialKind.ExamReturn })
            Assert.Equal("P01", Assert.Throws<PdfParseException>(() => PdfPigLayoutReader.Read(SyntheticPdf(graphicsState: state),kind)).Stage);
    }
    [Theory]
    [InlineData("0 0 1 1 re W n ")]
    [InlineData("0 0 1 1 re W* n ")]
    [InlineData("/Artifact BMC ")]
    [InlineData("/Artifact << >> BDC ")]
    public void SpecialReaderCannotTreatClippedOrMarkedTextAsProvenVisible(string prefix)
    {
        foreach (var kind in new[] { MaterialKind.Timetable, MaterialKind.Exam, MaterialKind.ExamReturn })
            Assert.Equal("P01", Assert.Throws<PdfParseException>(() => PdfPigLayoutReader.Read(SyntheticPdf(prefix: prefix),kind)).Stage);
    }
    [Theory]
    [InlineData("1 g ", "")]
    [InlineData("1 1 1 rg ", "")]
    [InlineData("0 0 0 0 k ", "")]
    [InlineData("0 g 0 0 200 200 re f ", "")]
    [InlineData("0 0 0 rg 0 0 200 200 re f ", "")]
    [InlineData("0 0 0 1 k 0 0 200 200 re f ", "")]
    [InlineData("20 100 m 40 100 l S ", "")]
    [InlineData("", " 20 100 m 40 100 l S")]
    [InlineData("", " 1 g 10 80 80 40 re f")]
    [InlineData("", " 1 G 20 w 10 100 m 100 100 l S")]
    public void ReaderDoesNotReuseWhiteOrOverpaintedText(string prefix, string suffix)
    {
        foreach (var kind in new[] { MaterialKind.Timetable, MaterialKind.Exam, MaterialKind.ExamReturn })
        {
            var capture = new RecoveryReadCapture();
            Assert.Equal("P01", Assert.Throws<PdfParseException>(() => PdfPigLayoutReader.Read(SyntheticPdf(prefix: prefix, suffix: suffix), kind, capture: capture)).Stage);
            Assert.False(capture.Complete);
            Assert.DoesNotContain(capture.Pages, p => p.State == RecoveryInputState.Complete);
        }
    }
    [Theory]
    [InlineData("0 g ")]
    [InlineData("0 0 0 rg ")]
    [InlineData("0 0 0 1 k ")]
    [InlineData("q 1 g 0 0 200 200 re f Q ")]
    [InlineData("1 g 0 0 200 200 re f 0 g ")]
    [InlineData("0 g 10 10 1 100 re f ")]
    public void ReaderStillReusesBlackTextAfterAnEarlierBackground(string prefix)
    {
        foreach (var kind in new[] { MaterialKind.Timetable, MaterialKind.Exam, MaterialKind.ExamReturn })
        {
            var capture = new RecoveryReadCapture();
            var page = Assert.Single(PdfPigLayoutReader.Read(SyntheticPdf(prefix: prefix), kind, capture: capture));
            Assert.Equal("架空", string.Concat(page.Glyphs.Select(g => g.Text)));
            Assert.True(capture.Complete);
        }
    }
    [Fact]
    public void GraphicsStateLineWidthCannotBypassTheOverpaintCheck()
    {
        foreach (var kind in new[] { MaterialKind.Timetable, MaterialKind.Exam, MaterialKind.ExamReturn })
            Assert.Equal("P01", Assert.Throws<PdfParseException>(() => PdfPigLayoutReader.Read(
                SyntheticPdf(graphicsState: "/LW 25", suffix: " 10 115 m 100 115 l S"), kind)).Stage);
    }
    [Theory]
    [InlineData("q 1 g 40 20 1 140 re f Q ", 4)]
    [InlineData("q 1 g 40 20 1 140 re B Q ", 8)]
    [InlineData("q 0 g 40 20 1 140 re B Q ", 9)]
    public void WhiteFillDoesNotCreateAnInvisibleRuleAndBlackStrokeRemainsVisible(string prefix, int ruleCount)
    {
        foreach (var kind in new[] { MaterialKind.Timetable, MaterialKind.Exam, MaterialKind.ExamReturn })
        {
            var capture = new RecoveryReadCapture();
            var page = Assert.Single(PdfPigLayoutReader.Read(SyntheticPdf(prefix: prefix), kind, capture: capture));
            Assert.Equal(ruleCount, page.Lines.Count);
            Assert.Equal("架空", string.Concat(page.Glyphs.Select(g => g.Text)));
            Assert.True(capture.Complete);
        }
    }
    [Theory]
    [InlineData("40 20 m 40 160 l S q 1 g 0 0 200 200 re f Q ")]
    [InlineData("25 w 40 20 1 140 re B ")]
    [InlineData("25 w 40 20 1 140 re B* ")]
    [InlineData("25 w 40 20 1 140 re b ")]
    [InlineData("25 w 40 20 1 140 re b* ")]
    [InlineData("1 G 40 20 1 140 re B ")]
    [InlineData("q 1 g 1 G 40 20 1 140 re B Q ")]
    [InlineData("20 90 m 50 120 l S ")]
    [InlineData("20 90 m 50 90 l 50 120 l s ")]
    [InlineData("1 Tr ")]
    [InlineData("2 Tr ")]
    [InlineData("25 w 1 Tr ")]
    [InlineData("25 w 2 Tr ")]
    public void UnsupportedPaintCannotBeCertifiedAsCompleteSource(string prefix)
    {
        foreach (var kind in new[] { MaterialKind.Timetable, MaterialKind.Exam, MaterialKind.ExamReturn })
        {
            var capture = new RecoveryReadCapture();
            Assert.Equal("P01", Assert.Throws<PdfParseException>(() => PdfPigLayoutReader.Read(
                SyntheticPdf(prefix: prefix), kind, capture: capture)).Stage);
            Assert.False(capture.Complete);
            Assert.DoesNotContain(capture.Pages, p => p.State == RecoveryInputState.Complete);
        }
    }
    [Fact]
    public void CroppedOutTextCannotBeReusedAsCompleteInput()
    {
        foreach (var kind in new[] { MaterialKind.Timetable, MaterialKind.Exam, MaterialKind.ExamReturn })
        {
            var capture = new RecoveryReadCapture();
            Assert.Equal("P01", Assert.Throws<PdfParseException>(() => PdfPigLayoutReader.Read(
                SyntheticPdf(cropBox: "/CropBox [0 150 200 200]"), kind, capture: capture)).Stage);
            Assert.False(capture.Complete); Assert.All(capture.Pages, p => Assert.Equal(RecoveryInputState.RasterOnly, p.State));
            Assert.Single(PdfPigLayoutReader.Read(SyntheticPdf(cropBox: "/CropBox [0 0 200 200]"), kind));
        }
    }
    [Fact]
    public void ReaderRejectsMissingMappingAndFormObjects()
    {
        Assert.Throws<PdfParseException>(() => PdfPigLayoutReader.Read(SyntheticPdf(removeMapping: true), MaterialKind.Timetable));
        Assert.Throws<PdfParseException>(() => PdfPigLayoutReader.Read(SyntheticPdf(formObject: true), MaterialKind.Timetable));
    }
    [Fact] public void ReaderCaptureKeepsCompleteLayoutAndResetsForAnotherAttempt()
    {
        var capture = new RecoveryReadCapture();
        var pages = PdfPigLayoutReader.Read(SyntheticPdf(), MaterialKind.Timetable, capture: capture);
        Assert.True(capture.Complete); Assert.Equal(pages[0].Glyphs, capture.Pages[0].Layout!.Glyphs);
        Assert.Equal(pages[0].Lines, capture.Pages[0].Layout!.Lines);
        Assert.Throws<PdfParseException>(() => PdfPigLayoutReader.Read("invalid"u8.ToArray(), MaterialKind.Timetable, capture: capture));
        Assert.False(capture.Complete); Assert.Empty(capture.Pages);
    }
    [Fact] public void ImageAndMissingRulesHaveRecoverableClassification()
    {
        Assert.Equal("raster", Assert.Throws<PdfParseException>(() => new PdfPageLayout(100, 100, [], []).Validate(1)).Stage);
        Assert.Equal("P08", Assert.Throws<PdfParseException>(() => new PdfPageLayout(100, 100, [new("架", 10, 10, 10, 10)], []).Validate(1)).Stage);
        Assert.Equal("limit", Assert.Throws<PdfParseException>(() => new PdfPageLayout(double.NaN, 100, [], []).Validate(1)).Stage);
    }
    private static PdfPageLayout TimetableLayout(string subject, string teacher, string room)
    {
        var glyphs = new List<PdfGlyph> { new("令和14年度前期時間割", 30, 20, 100, 10, 0, 100) };
        for (var index = 0; index < 40; index++) glyphs.Add(new((index % 8 + 1).ToString(CultureInfo.InvariantCulture), 42 + index * 10, 70, 5, 10, 1, index));
        glyphs.AddRange([new("CN", 25, 110, 8, 10, 2, 40), new("1", 5, 110, 8, 10, 3, 41), new(subject, 41, 96, 8, 8, 4, 42), new(teacher, 41, 108, 8, 8, 5, 43), new(room, 41, 120, 8, 8, 6, 44)]);
        var lines = new List<PdfRule> { new(0, 60, 440, 60), new(0, 90, 440, 90), new(0, 140, 440, 140), new(0, 60, 0, 140), new(20, 60, 20, 140) };
        for (var index = 0; index <= 40; index++) lines.Add(new(40 + index * 10, 60, 40 + index * 10, 140));
        return new(500, 500, glyphs, lines);
    }
    internal static byte[] SyntheticPdf(bool removeMapping = false, bool formObject = false, string? graphicsState = null, string? prefix = null, string? suffix = null, string? cropBox = null)
    {
        var cmap = "1 begincodespacerange <00> <ff> endcodespacerange 2 beginbfchar <41> <67b6> <42> <7a7a> endbfchar";
        var content = (prefix ?? "") + (graphicsState is null ? "" : "/Ghost gs ") + "BT /F1 10 Tf 1 0 0 1 20 100 Tm (AB) Tj ET 10 10 100 120 re S" + (suffix ?? "") + (formObject ? " /Fake Do" : "");
        string Stream(string value) => "<< /Length " + Encoding.ASCII.GetByteCount(value) + " >>\nstream\n" + value + "\nendstream";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] " + (cropBox ?? "") + " /Resources << /Font << /F1 4 0 R >> " + (graphicsState is null ? "" : "/ExtGState << /Ghost << /Type /ExtGState " + graphicsState + " >> >> ") + ">> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding /FirstChar 65 /LastChar 66 /Widths [500 500] /FontDescriptor 7 0 R" + (removeMapping ? "" : " /ToUnicode 6 0 R") + " >>",
            Stream(content), Stream(cmap),
            "<< /Type /FontDescriptor /FontName /Helvetica /Flags 32 /FontBBox [0 -200 1000 800] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 >>"
        };
        var pdf = new StringBuilder("%PDF-1.7\n"); var offsets = new List<int> { 0 };
        for (var index = 0; index < objects.Length; index++)
        { offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString())); pdf.Append(index + 1).Append(" 0 obj\n").Append(objects[index]).Append("\nendobj\n"); }
        var xref = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.Append("xref\n0 ").Append(objects.Length + 1).Append("\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) pdf.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        pdf.Append("trailer\n<< /Size ").Append(objects.Length + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}
