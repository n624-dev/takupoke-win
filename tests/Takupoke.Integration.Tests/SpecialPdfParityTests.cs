using Takupoke.Core;
using Takupoke.Infrastructure.Parsing;
using Xunit;

namespace Takupoke.Integration.Tests;

/// <summary>Fictional geometry and expected results match the iOS SpecialScheduleTests fixtures.</summary>
public sealed class SpecialPdfParityTests
{
    private static PdfPageLayout ReplaceFirstExamTime(PdfPageLayout original, string text, bool consecutive = false)
    {
        var glyphs = original.Glyphs.Where(g => g.Y != 470 || (consecutive ? g.X < 300 : g.X >= 280)).ToList();
        for (var index = 0; index < text.Length; index++)
            glyphs.Add(new(text[index].ToString(), (consecutive ? 300 : 20) + index * 4, 470, 4, 8, 9000, 90000 + index));
        return original with { Glyphs = glyphs };
    }

    [Theory]
    [InlineData("11時限目8:50~9:35")]
    [InlineData("1時限目7:00~8:500")]
    [InlineData("١1時限目8:50~9:35")]
    [InlineData("\U000104A11時限目8:50~9:35")]
    public void SpecialPeriodHeadingsDoNotTruncateNumericTokens(string text)
    {
        var pages = Enumerable.Range(1, 6).Select(i => ReplaceFirstExamTime(ExamPage(i), text)).ToArray();
        Assert.Equal("P05", Assert.Throws<PdfParseException>(() => PdfScheduleParser.Special(pages, MaterialKind.Exam)).Stage);
    }

    [Theory]
    [InlineData("11・2時限連続7:00~8:00")]
    [InlineData("1・2時限連続7:00~8:500")]
    public void SpecialConsecutiveHeadingsDoNotAdoptNumericSuffix(string text)
    {
        var pages = Enumerable.Range(1, 6).Select(i => ReplaceFirstExamTime(ExamPage(i, merged: i == 1), text, true)).ToArray();
        Assert.Equal("P05", Assert.Throws<PdfParseException>(() => PdfScheduleParser.Special(pages, MaterialKind.Exam)).Stage);
    }

    [Fact]
    public void MissingContinuousHeadingKeepsVerifiedSinglePeriodFallback()
    {
        var pages = Enumerable.Range(1, 6).Select(i => ReplaceFirstExamTime(ExamPage(i, merged: i == 1), "", true)).ToArray();
        var result = PdfScheduleParser.Special(pages, MaterialKind.Exam);
        Assert.Equal(86, result.Lessons.Count);
        var merged = result.Lessons.Where(l => l.ClassName == "1_1" && l.SpanEnd == 2).ToArray();
        Assert.NotEmpty(merged);
        Assert.All(merged, lesson => Assert.Equal(new TimeRange("08:50", "10:35"), lesson.RecordedTime));
    }

    [Theory]
    [InlineData("1時限目8:50~9:35")]
    [InlineData("１ 時限目 ８：５０〜９：３５")]
    public void SpecialWholeNumericTokensRetainAdjacentAndWidthNormalizedHeadings(string text)
    {
        var pages = Enumerable.Range(1, 6).Select(i => ReplaceFirstExamTime(ExamPage(i, merged: i == 1), text)).ToArray();
        var parsed = PdfScheduleParser.Special(pages, MaterialKind.Exam);
        var result = Takupoke.Infrastructure.Storage.DataCodec.Decode<SpecialAnalysis>(
            Takupoke.Infrastructure.Storage.DataCodec.Encode(parsed));
        Assert.Equal(86, result.Lessons.Count);
        Assert.Equal(new TimeRange("08:50", "09:35"), result.PeriodTimes[1]);
        Assert.Contains(result.Lessons, l => l.ClassName == "1_1" && l.SpanEnd == 2
            && l.RecordedTime == new TimeRange("08:50", "10:20"));
    }

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
    private static PdfPageLayout ExamPage(int number, bool merged = false, bool metadata = false, bool omitLastTime = false)
    {
        var b = new Builder(true, 8); var columns = number == 6 ? 2 : 3;
        string[] labels = number switch { 1 => ["1-1", "1-2", "1-3"], 6 => ["1年", "2年"], _ => [$"{number}-CN", $"{number}-ES", $"{number}-IT"] };
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
            for (var column = 0; column < columns; column++) b.Write("架空科目A", 106 + column * 240, 126 + index * 40, 6);
            if (metadata && index == 0) { b.Write("架空教員A", 106, 136, 5); b.Write("架空教室A", 106, 145, 5); }
        }
        var times = new[] { "8:50~9:35", "9:50~10:35", "10:50~11:35", "11:50~12:35", "13:20~14:05", "14:20~15:05" };
        for (var index = 0; index < times.Length; index++) if (!omitLastTime || index != 5) b.Write($"{index + 1}時限目{times[index]}", 20, 470 + index * 15);
        b.Write("1・2時限連続8:50~10:20", 300, 470);
        return b.Page(850, 600);
    }
    private static PdfPageLayout ReturnPage(int[]? dateDays = null)
    {
        dateDays ??= [1,2,3,4,5];
        var b = new Builder(false, 4); b.Write("令和8年度 試験返却時間割", 20, 20);
        for (var day = 0; day < 5; day++)
        {
            b.Write($"4/{dateDays[day]}", 150 + day * 8 * 40, 70);
            for (var period = 0; period < 8; period++) b.Write((period + 1).ToString(), 150 + (day * 8 + period) * 40, 100);
        }
        var groups = new[] { ("1", new[] { "1", "2", "3" }), ("2", new[] { "CN", "ES", "IT" }), ("3", new[] { "CN", "ES", "IT" }),
            ("4", new[] { "CN", "ES", "IT" }), ("5", new[] { "CN", "ES", "IT" }), ("AI", new[] { "1", "2" }) };
        var row = 0;
        foreach (var (grade, classes) in groups)
        { b.Write(grade, 30, 132 + (row + classes.Length / 2) * 25); foreach (var cls in classes) { b.Write(cls, 124, 132 + row * 25); row++; } }
        for (var index = 0; index <= 40; index++) b.Line(140 + index * 40, index is 5 or 13 ? 145 : 110, 140 + index * 40, 545);
        b.Line(110, 110, 110, 545);
        for (var index = 0; index <= 17; index++) b.Line(0, 120 + index * 25, 1740, 120 + index * 25);
        b.Line(220, 332.5, 260, 332.5);
        b.Write("架空科目A", 222, 322, 3); b.Write("架空教員A", 222, 327, 3); b.Write("架空科目B", 222, 335, 3); b.Write("架空教員B", 222, 340, 3);
        b.Write("架空科目C", 502, 322, 3); b.Write("架空科目D", 302, 130, 3); b.Write("架空科目E", 622, 130, 3);
        b.Write($"4月{dateDays[0]}日の時間割は以下のとおりです。", 1300, 650); b.Write($"4月{dateDays[1]}日~{dateDays[4]}日は通常の授業日どおりの授業時間です。", 1300, 670);
        var times = new[] { "7:00~7:40", "7:50~8:30", "8:40~9:20", "9:30~10:10", "10:30~11:10", "11:10~11:50", "12:00~12:40", "12:40~13:20" };
        for (var index = 0; index < times.Length; index++) b.Write($"{index + 1}時限目{times[index]}", 20, 760 + (index + 1) * 15);
        return b.Page(1800, 1000);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReturnDedicatedClockFollowsOriginalColumnAfterSortedDatePersistence(bool reversed)
    {
        var result = PdfScheduleParser.Special([ReturnPage(reversed ? [2,1,3,4,5] : [1,2,3,4,5])], MaterialKind.ExamReturn);
        var expectedDate = reversed ? "2026-04-02" : "2026-04-01";
        Assert.Equal(expectedDate, result.DedicatedTimeDate);
        Assert.Equal(Enumerable.Range(1,5).Select(d => $"2026-04-0{d}"), result.CoveredDates);
        var restored = Takupoke.Infrastructure.Storage.DataCodec.Decode<SpecialAnalysis>(Takupoke.Infrastructure.Storage.DataCodec.Encode(result));
        foreach (var date in restored.CoveredDates)
        {
            var day = DateOnly.Parse(date); var first = date == expectedDate;
            var engine = new TimetableEngine(new(Specials: [restored]));
            Assert.Equal(first ? new("10:30", "11:10") : new TimeRange("12:50", "13:35"), restored.PeriodTime(day, 5));
            Assert.Equal(first ? new("11:10", "11:50") : new TimeRange("13:35", "14:20"), restored.PeriodTime(day, 6));
            var empty = new ScheduleChange(date, "2_CN", "5", "", "架空変更科目A", "", "", "変更", "", "");
            Assert.Equal(restored.PeriodTime(day, 5), Assert.Single(engine.ChangeTimes(empty)));
            if (date is "2026-04-01" or "2026-04-02")
            {
                var block = Assert.Single(engine.Blocks(day, "1_1").Where(b => b.StartPeriod == 5 && b.EndPeriod == 6));
                Assert.Equal(first ? new("10:30", "11:50") : new TimeRange("12:50", "14:20"), engine.CardTime(day, "1_1", block));
                Assert.Equal(first, engine.IsInProgress(day, "1_1", block, new DateTimeOffset(day.Year, day.Month, day.Day, 10,45,0,TimeSpan.FromHours(9))));
                var fragment = block with { StartPeriod = 6 };
                Assert.Equal(restored.PeriodTime(day,6), engine.CardTime(day,"1_1",fragment));
                var original = restored.Lessons.First(l => l.ClassName == "1_1" && l.Date == date && l.Period == 5);
                Assert.Equal(original.RecordedTime, restored.TimeFor(original));
            }
        }
    }

    private sealed class ClockProtector : Takupoke.Infrastructure.Storage.IKeyProtector, IDisposable
    {
        private readonly Takupoke.Infrastructure.Storage.EnvelopeCipher _cipher = new(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        public byte[] Protect(byte[] key) => _cipher.Encrypt(key, "fictional-clock-key");
        public byte[] Unprotect(byte[] bytes) => _cipher.Decrypt(bytes, "fictional-clock-key");
        public void Dispose() => _cipher.Dispose();
    }
    [Fact]
    public async Task DedicatedReturnDateSurvivesEncryptedSqliteStoreRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "takupoke-return-clock-" + Guid.NewGuid().ToString("N"));
        using var protector = new ClockProtector();
        try
        {
            var parsed = PdfScheduleParser.Special([ReturnPage([2,1,3,4,5])], MaterialKind.ExamReturn);
            await using (var store = new Takupoke.Infrastructure.Storage.SchoolDataStore(root, protector))
                await store.WriteAsync(await store.BeginAsync(), "fictional.special", parsed);
            await using var reopened = new Takupoke.Infrastructure.Storage.SchoolDataStore(root, protector);
            var restored = (await reopened.ReadAsync<SpecialAnalysis>(await reopened.BeginAsync(), "fictional.special"))!;
            Assert.Equal("2026-04-02", restored.DedicatedTimeDate);
            Assert.Equal(new TimeRange("10:30", "11:10"), restored.PeriodTime(new(2026,4,2),5));
            Assert.Equal(new TimeRange("12:50", "13:35"), restored.PeriodTime(new(2026,4,1),5));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
