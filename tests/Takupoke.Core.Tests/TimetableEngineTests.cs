using Takupoke.Core;
using Xunit;

namespace Takupoke.Core.Tests;

public sealed class TimetableEngineTests
{
    private static readonly DateOnly Day = new(2032, 4, 5);
    private static NormalLesson Lesson(int period, string subject = "架空科目A", string teacher = "架空教員A", string room = "架空教室A") =>
        new("1_CN", Day.SchoolWeekday(), period, new(subject, teacher, room), "架空原文A", 1);
    private static ScheduleChange Change(string period = "1", string note = "変更", string after = "架空科目B") =>
        new(Day.Iso(), "1_CN", period, "架空科目A", after, "架空教員B", "架空教室B", note, "架空原文B", "架空正規化B");
    private static TimetableAnalysis Timetable(params NormalLesson[] lessons) => new(2032, "前期", lessons);
    private static SpecialAnalysis Special(MaterialKind kind, TimeRange time) => new(kind, 2032, [Day.Iso()], ["1_CN"],
        new Dictionary<int, TimeRange> { [1] = time }, [new(Day.Iso(), "1_CN", 1, 1, 1, time, ["架空科目C", "架空教員C", ""], 1)]);

    [Fact]
    public void AdjacentIdenticalLessonsMergeWithoutChangingInput()
    {
        var timetable = Timetable(Lesson(1), Lesson(2), Lesson(3, room: ""));
        var blocks = new TimetableEngine(new(timetable)).Blocks(Day, "1_CN");
        Assert.Equal(2, blocks.Count);
        Assert.Equal((1, 2), (blocks[0].StartPeriod, blocks[0].EndPeriod));
        Assert.Equal(3, timetable.Lessons.Count);
    }
    [Fact]
    public void ParallelLessonsRemainInSeparateLanes()
    {
        var blocks = new TimetableEngine(new(Timetable(Lesson(1), Lesson(1, "架空科目B"), Lesson(2)))).Blocks(Day, "1_CN");
        var positioned = TimetableEngine.Positioned(blocks);
        Assert.Equal(2, positioned.Count);
        Assert.Equal(new[] { 0, 1 }, positioned.Select(p => p.Lane));
    }
    [Fact]
    public void LastMakeupWinsAndAllOriginalsRemainInDetails()
    {
        var changes = new[] { Change(note: "補講"), Change(note: "休講", after: "") };
        var engine = new TimetableEngine(new(Timetable(Lesson(1)), changes));
        Assert.Equal("補講", Assert.IsType<ChangeContent>(Assert.Single(engine.Blocks(Day, "1_CN")).Content).Change.Note);
        Assert.Single(engine.Slot(Day, 1, "1_CN").BaseLessons);
        Assert.Equal(2, engine.Slot(Day, 1, "1_CN").Changes.Count);
    }
    [Theory]
    [InlineData("1", 1)]
    [InlineData("1,2", 2)]
    [InlineData("１～２", 2)]
    [InlineData("1,3", 0)]
    [InlineData("2,1", 0)]
    [InlineData("1,1", 0)]
    [InlineData("9", 0)]
    [InlineData("", 0)]
    public void OnlyConfirmedConsecutiveNotationAffectsGrid(string value, int count)
    {
        Assert.Equal(count, Change(value).GridPeriods?.Count ?? 0);
    }
    [Fact]
    public void DisjointChangeRemainsInListWithSeparateClockRanges()
    {
        var change = Change("1,3");
        var engine = new TimetableEngine(new(Timetable(Lesson(1)), [change]));
        Assert.Single(engine.Changes(new HashSet<string> { "1_CN" }, ChangeRange.All, Day, Day.Monday()));
        Assert.Equal(2, engine.ChangeTimes(change).Count);
        Assert.IsType<NormalContent>(Assert.Single(engine.Blocks(Day, "1_CN")).Content);
    }
    [Fact]
    public void ApiNoClassSuppressesNormalLessonsButKeepsMakeup()
    {
        var events = new[] { new SchoolEvent(Day.Iso(), "架空休業A", "授業なし", Classification: EventClassification.NoClass) };
        var engine = new TimetableEngine(new(Timetable(Lesson(1), Lesson(2)), [Change(note: "補講")], Events: events));
        Assert.IsType<ChangeContent>(Assert.Single(engine.Blocks(Day, "1_CN")).Content);
    }
    [Fact]
    public void MissingExamNeverBorrowsNormalLessonOrClock()
    {
        var events = new[] { new SchoolEvent(Day.Iso(), "架空試験A", "テスト") };
        var engine = new TimetableEngine(new(Timetable(Lesson(1)), [Change()], Events: events));
        Assert.IsType<ChangeContent>(Assert.Single(engine.Blocks(Day, "1_CN")).Content);
        Assert.Null(engine.SlotTime(Day, "1_CN", 1));
        Assert.Contains("試験時間割：未公開または未解析です", engine.MissingMessages(Day, "1_CN"));
    }
    [Fact]
    public void ExamAndReturnOverlapWithoutLosingEitherLesson()
    {
        var engine = new TimetableEngine(new(Timetable(Lesson(1)), Specials:
            [Special(MaterialKind.Exam, new("09:00", "09:50")), Special(MaterialKind.ExamReturn, new("10:00", "10:30"))]));
        Assert.Equal(2, engine.Blocks(Day, "1_CN").Count);
        Assert.Null(engine.SlotTime(Day, "1_CN", 1));
        Assert.Empty(engine.Slot(Day, 2, "1_CN").BaseLessons);
    }
    [Fact]
    public void NormalModeStillUsesSpecialsButIgnoresChanges()
    {
        var engine = new TimetableEngine(new(Timetable(Lesson(1)), [Change()], [Special(MaterialKind.Exam, new("09:00", "09:50"))]), includesChanges: false);
        Assert.IsType<SpecialContent>(Assert.Single(engine.Blocks(Day, "1_CN")).Content);
    }
    [Theory]
    [InlineData("留架空科目A")]
    [InlineData(" 留 架空科目A ")]
    public void InternationalStudentFilterUsesPrefix(string subject)
    {
        var data = new ScheduleData(Timetable(Lesson(1, subject)));
        Assert.Empty(new TimetableEngine(data).Blocks(Day, "1_CN"));
        Assert.Single(new TimetableEngine(data, international: true).Blocks(Day, "1_CN"));
    }
    [Theory]
    [InlineData(8, 49, false)]
    [InlineData(8, 50, true)]
    [InlineData(9, 34, true)]
    [InlineData(9, 35, false)]
    public void InProgressIncludesStartAndExcludesEnd(int hour, int minute, bool expected)
    {
        var engine = new TimetableEngine(new(Timetable(Lesson(1))));
        var block = Assert.Single(engine.Blocks(Day, "1_CN"));
        var now = new DateTimeOffset(Day.Year, Day.Month, Day.Day, hour, minute, 0, TimeSpan.FromHours(9));
        Assert.Equal(expected, engine.IsInProgress(Day, "1_CN", block, now));
    }
    [Fact]
    public void CancellationIsNeverInProgress()
    {
        var engine = new TimetableEngine(new(Timetable(Lesson(1)), [Change(note: "休講")]));
        var now = new DateTimeOffset(Day.Year, Day.Month, Day.Day, 8, 50, 0, TimeSpan.FromHours(9));
        Assert.False(engine.IsInProgress(Day, "1_CN", Assert.Single(engine.Blocks(Day, "1_CN")), now));
    }
    [Fact]
    public void UnknownTermAndOutOfTermNeverLeakNormalLessons()
    {
        Assert.Empty(new TimetableEngine(new(new(2032, null, [Lesson(1)]))).Blocks(Day, "1_CN"));
        Assert.Empty(new TimetableEngine(new(Timetable(Lesson(1)))).Blocks(new(2032, 10, 4), "1_CN"));
    }
    [Fact]
    public void ReturnUsesDedicatedClockOnlyOnFirstDate()
    {
        var second = Day.AddDays(1);
        var analysis = Special(MaterialKind.ExamReturn, new("09:00", "09:20")) with { CoveredDates = [Day.Iso(), second.Iso()] };
        Assert.Equal(new TimeRange("09:00", "09:20"), analysis.PeriodTime(Day, 1));
        Assert.Equal(new TimeRange("08:50", "09:35"), analysis.PeriodTime(second, 1));
    }
    [Fact]
    public void FixedClassSelectionAllowsOnlyFirstYearPairing()
    {
        Assert.Equal(20, ClassSelection.Candidates.Count);
        Assert.True(ClassSelection.IsValid(["1_2", "1_IT"]));
        Assert.True(ClassSelection.IsValid(["1_IT", "1_2"]));
        Assert.False(ClassSelection.IsValid(["1_CN", "1_ES"]));
        Assert.False(ClassSelection.IsValid(["2_CN", "2_IT"]));
        Assert.False(ClassSelection.IsValid(["AI_1", "1_1"]));
    }
    [Fact]
    public void SemesterBoundsAllowTheWeekOverlappingOctoberFirst()
    {
        var reference = new DateOnly(2032, 10, 1);
        var bounds = new TimetableEngine(new()).ReachableWeeks(reference, ["1_CN"]);
        Assert.Equal(reference.Monday(), bounds.Lower);
        Assert.True(bounds.Upper >= bounds.Lower);
    }
}
