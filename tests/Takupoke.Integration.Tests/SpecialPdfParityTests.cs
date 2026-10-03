using Takupoke.Core;
using Takupoke.Infrastructure.Parsing;
using Xunit;

namespace Takupoke.Integration.Tests;

/// <summary>Fictional ruled tables with intact role baselines, empty fields and merged lessons.</summary>
public sealed class SpecialPdfParityTests
{
    [Fact]
    public void ExamMatchesIosCoveredClassesDatesTimesAndMergedLessons()
    {
        var result = PdfScheduleParser.Special(Enumerable.Range(1, 6).Select(i => ExamPage(i, merged: i == 1)).ToArray(), MaterialKind.Exam);
        Assert.Equal(86, result.Lessons.Count); Assert.Equal(5, result.CoveredDates.Count); Assert.Equal(17, result.CoveredClasses.Count);
        Assert.Contains("AI_1", result.CoveredClasses); Assert.Contains("AI_2", result.CoveredClasses);
        Assert.Equal(new TimeRange("09:50", "10:35"), result.PeriodTimes[2]);
        var merged = result.Lessons.Where(l => l.Date == "2026-04-01" && l.ClassName == "1_1" && l.Period is 1 or 2).ToArray();
        Assert.Equal(2, merged.Length); Assert.All(merged, l => { Assert.Equal(2, l.SpanEnd); Assert.Equal(new TimeRange("08:50", "10:20"), l.RecordedTime); });
    }
    [Fact]
    public void ExamSeparatesSubjectTeacherAndRoom()
    {
        var result = PdfScheduleParser.Special(Enumerable.Range(1, 6).Select(i => ExamPage(i, metadata: i == 1)).ToArray(), MaterialKind.Exam);
        var lesson = result.Lessons.First(l => l.Date == "2026-04-01" && l.ClassName == "1_1" && l.Period == 1);
        Assert.Equal(new LessonNames("架空科目A", "架空教員A", "架空教室A"), lesson.Names);
    }
    [Fact]
    public void ReturnMatchesIosSplitCellsAndDifferentTimesOnSubsequentDays()
    {
        var result = PdfScheduleParser.Special([ReturnPage()], MaterialKind.ExamReturn);
        var lessons = result.Lessons.Where(l => l.Date == "2026-04-01" && l.ClassName == "3_IT" && l.Period == 3).ToArray();
        Assert.Equal(2, lessons.Length); Assert.Equal(new[] { "架空科目A", "架空科目B" }, lessons.Select(l => l.Names.Subject));
        Assert.All(lessons, l => Assert.Empty(l.Names.Room));
        Assert.Equal(new TimeRange("11:10", "11:50"), result.PeriodTime(new(2026, 4, 1), 6));
        Assert.Equal(new TimeRange("09:35", "10:20"), result.PeriodTime(new(2026, 4, 2), 2));
        Assert.Equal(new TimeRange("13:35", "14:20"), result.PeriodTime(new(2026, 4, 2), 6));
        foreach (var date in new[] { "2026-04-01", "2026-04-02" })
        {
            var pair = result.Lessons.Where(l => l.ClassName == "1_1" && l.Date == date && l.Period is 5 or 6).ToArray();
            Assert.Equal(2, pair.Length);
            Assert.All(pair, l => Assert.Equal(date == "2026-04-01" ? new("10:30", "11:50") : new TimeRange("12:50", "14:20"), result.TimeFor(l)));
        }
    }
    [Fact]
    public void MissingSpecialTimeCannotBeReplacedByOrdinaryTime()
    {
        Assert.Throws<PdfParseException>(() => PdfScheduleParser.Special(Enumerable.Range(1, 6).Select(i => ExamPage(i, omitLastTime: i == 1)).ToArray(), MaterialKind.Exam));
    }
    [Theory]
    [InlineData("9:20~10:35")]
    [InlineData("8:40~9:30")]
    public void RepeatedExamChartsCannotAcceptOverlappingOrReversedPeriodOrder(string second)
    {
        var times = new[] { "8:50~9:35", second, "10:50~11:35", "11:50~12:35", "13:20~14:05", "14:20~15:05" };
        Assert.Equal("P05", Assert.Throws<PdfParseException>(() => PdfScheduleParser.Special(Enumerable.Range(1,6).Select(i=>ExamPage(i,timingOverride:times)).ToArray(),MaterialKind.Exam)).Stage);
    }
    [Fact]
    public void ReturnChartCannotAcceptOverlappingFirstDayPeriods()
    {
        var times = new[] { "7:00~7:40", "7:50~8:30", "8:40~9:20", "9:30~10:10", "10:30~11:10", "11:00~11:50", "12:00~12:40", "12:40~13:20" };
        Assert.Equal("P05", Assert.Throws<PdfParseException>(()=>PdfScheduleParser.Special([ReturnPage(timingOverride:times)],MaterialKind.ExamReturn)).Stage);
    }
    [Fact]
    public void AdjacentExamPeriodsMayShareAnEndpoint()
    {
        var times = new[] { "8:50~9:35", "9:35~10:35", "10:50~11:35", "11:50~12:35", "13:20~14:05", "14:20~15:05" };
        var result=PdfScheduleParser.Special(Enumerable.Range(1,6).Select(i=>ExamPage(i,timingOverride:times)).ToArray(),MaterialKind.Exam);
        Assert.Equal(new TimeRange("09:35","10:35"),result.PeriodTimes[2]);
    }
    [Theory]
    [InlineData("3-XX")]
    [InlineData("3-1")]
    public void SeventeenExamClassesMustMatchTheRequiredSet(string replacement)
    {
        var pages = Enumerable.Range(1, 6).Select(i => ExamPage(i, firstLabel: i == 3 ? replacement : null)).ToArray();
        Assert.Throws<PdfParseException>(() => PdfScheduleParser.Special(pages, MaterialKind.Exam));
    }
    [Fact]
    public void SeventeenReturnClassesCannotReplaceAiTwoWithAiThree()
    {
        Assert.Throws<PdfParseException>(() => PdfScheduleParser.Special([ReturnPage(invalidAiClass: true)], MaterialKind.ExamReturn));
    }
    private sealed class Builder(bool ordered, double height)
    {
        public readonly List<PdfGlyph> Glyphs = []; public readonly List<PdfRule> Lines = [];
        private int _line, _order;
        public void Write(string text, double x, double y, double step = 4)
        {
            for (var i = 0; i < text.Length; i++) Glyphs.Add(new(text[i].ToString(), x + i * step, y, step, height, ordered ? _line : null, ordered ? _order++ : null));
            _line++;
        }
        public void Line(double x1, double y1, double x2, double y2) => Lines.Add(new(x1, y1, x2, y2));
        public PdfPageLayout Page(double width, double pageHeight) => new(width, pageHeight, Glyphs, Lines);
    }
    private static PdfPageLayout ExamPage(int number, bool merged = false, bool metadata = false, bool omitLastTime = false, string? firstLabel = null, string[]? timingOverride = null)
    {
        var b = new Builder(true, 8); var columns = number == 6 ? 2 : 3;
        string[] labels = number switch { 1 => ["1-1", "1-2", "1-3"], 6 => ["1年", "2年"], _ => [$"{number}-CN", $"{number}-ES", $"{number}-IT"] };
        if (firstLabel is not null) labels[0] = firstLabel;
        b.Write("令和8年度 試験時間割", 20, 20);
        for (var column = 0; column < columns; column++)
        {
            b.Write(labels[column], 200 + column * 240, 70);
            for (var period = 0; period < 6; period++) b.Write((period + 1).ToString(), 118 + (column * 6 + period) * 40, 100);
        }
        for (var index = 0; index <= columns * 6; index++) b.Line(100 + index * 40, merged && index == 1 ? 150 : 110, 100 + index * 40, 350);
        b.Line(0, 110, 0, 350);
        for (var index = 0; index <= 6; index++) b.Line(0, 110 + index * 40, 100 + columns * 6 * 40, 110 + index * 40);
        for (var index = 0; index < 5; index++)
        {
            b.Write($"4月{index + 1}日", 16, 126 + index * 40);
            for (var column = 0; column < columns; column++) { b.Write("架空科目A", 106 + column * 240, 126 + index * 40, 6); b.Write("架空教員A", 106 + column * 240, 136 + index * 40, 5); b.Write("架空教室A", 106 + column * 240, 142 + index * 40, 5); }
        }
        var times = timingOverride ?? new[] { "8:50~9:35", "9:50~10:35", "10:50~11:35", "11:50~12:35", "13:20~14:05", "14:20~15:05" };
        for (var index = 0; index < times.Length; index++) if (!omitLastTime || index != 5) b.Write($"{index + 1}時限目{times[index]}", 20, 470 + index * 15);
        b.Write("1・2時限連続8:50~10:20", 300, 470);
        return b.Page(850, 600);
    }
    private static PdfPageLayout ReturnPage(bool invalidAiClass = false, string[]? timingOverride = null)
    {
        var b = new Builder(false, 4); b.Write("令和8年度 試験返却時間割", 20, 20);
        for (var day = 0; day < 5; day++)
        {
            b.Write($"4/{day + 1}", 150 + day * 8 * 40, 70);
            for (var period = 0; period < 8; period++) b.Write((period + 1).ToString(), 150 + (day * 8 + period) * 40, 100);
        }
        var groups = new[] { ("1", new[] { "1", "2", "3" }), ("2", new[] { "CN", "ES", "IT" }), ("3", new[] { "CN", "ES", "IT" }),
            ("4", new[] { "CN", "ES", "IT" }), ("5", new[] { "CN", "ES", "IT" }), ("AI", new[] { "1", invalidAiClass ? "3" : "2" }) };
        var row = 0;
        foreach (var (grade, classes) in groups)
        { b.Write(grade, 30, 132 + (row + classes.Length / 2) * 25); foreach (var cls in classes) { b.Write(cls, 124, 132 + row * 25); row++; } }
        for (var index = 0; index <= 40; index++) b.Line(140 + index * 40, index is 5 or 13 ? 145 : 110, 140 + index * 40, 545);
        b.Line(110, 110, 110, 545);
        for (var index = 0; index <= 17; index++) b.Line(0, 120 + index * 25, 1740, 120 + index * 25);
        b.Line(220, 332.5, 260, 332.5);
        b.Write("架空科目A", 222, 321, 3); b.Write("架空教員A", 222, 324, 3); b.Write("架空科目B", 222, 333.5, 3); b.Write("架空教員B", 222, 336.5, 3);
        b.Line(260, 332.5, 300, 332.5); b.Write("架空位置基準", 262, 321, 3); b.Write("架空教員基準", 262, 324, 3); b.Write("架空教室基準", 262, 327, 3);
        b.Write("架空科目C", 502, 322, 3); b.Write("架空教員C", 502, 327, 3); b.Write("架空教室C", 502, 332, 3);
        b.Write("架空科目D", 302, 122, 3); b.Write("架空教員D", 302, 127, 3); b.Write("架空教室D", 302, 132, 3);
        b.Write("架空科目E", 622, 122, 3); b.Write("架空教員E", 622, 127, 3); b.Write("架空教室E", 622, 132, 3);
        b.Write("4月1日の時間割は以下のとおりです。", 1300, 650); b.Write("4月2日~5日は通常の授業日どおりの授業時間です。", 1300, 670);
        var times = timingOverride ?? new[] { "7:00~7:40", "7:50~8:30", "8:40~9:20", "9:30~10:10", "10:30~11:10", "11:10~11:50", "12:00~12:40", "12:40~13:20" };
        for (var index = 0; index < times.Length; index++) b.Write($"{index + 1}時限目{times[index]}", 20, 760 + (index + 1) * 15);
        return b.Page(1800, 1000);
    }
}
