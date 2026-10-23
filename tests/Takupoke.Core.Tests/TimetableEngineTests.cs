using Takupoke.Core;
using Xunit;

namespace Takupoke.Core.Tests;

public sealed class TimetableEngineTests
{
    [Theory]
    [InlineData(2, 3, "10:25", false)]
    [InlineData(2, 3, "10:25", true)]
    [InlineData(4, 5, "12:25", false)]
    [InlineData(4, 5, "12:25", true)]
    public void ChangeAndMakeupMergesRespectBreaksAndLunch(int start, int end, string gap, bool custom)
    {
        var times = new ScheduleTimes(1, [new(Day.Iso(), [new(1,"08:55","09:40"), new(2,"09:40","10:25"), new(3,"10:35","11:20"), new(4,"11:20","12:05"),
            new(5,"12:55","13:40"), new(6,"13:40","14:25"), new(7,"14:35","15:20"), new(8,"15:20","16:05")])]).Validated();
        foreach (var kind in new[] { "変更", "補講" })
        {
            var engine = new TimetableEngine(new(Changes: [Change($"{start},{end}", kind)], Times: custom ? times : null));
            var block = Assert.Single(engine.Blocks(Day, "1_CN"));
            Assert.NotNull(engine.CardTime(Day, "1_CN", block));
            Assert.False(engine.IsInProgress(Day, "1_CN", block, DateTimeOffset.Parse(Day.Iso() + "T" + gap + ":00+09:00")));
            var first = custom ? times.Time(Day, start)! : ScheduleTimes.Normal[start];
            var last = custom ? times.Time(Day, end)! : ScheduleTimes.Normal[end];
            Assert.True(engine.IsInProgress(Day, "1_CN", block, DateTimeOffset.Parse(Day.Iso() + "T" + first.Start + ":00+09:00")));
            Assert.False(engine.IsInProgress(Day, "1_CN", block, DateTimeOffset.Parse(Day.Iso() + "T" + last.End + ":00+09:00")));
        }
    }
    [Theory]
    [InlineData(MaterialKind.Exam)]
    [InlineData(MaterialKind.ExamReturn)]
    public void IndependentSpecialCellsKeepTheirGapWhenVisuallyMerged(MaterialKind kind)
    {
        var clocks = new Dictionary<int, TimeRange> { [1] = new("09:00", "09:20"), [2] = new("09:30", "09:50") };
        var source = new SpecialAnalysis(kind, 2032, [Day.Iso()], ["1_CN"], clocks,
            new[] {1,2}.Select(period => new SpecialLesson(Day.Iso(), "1_CN", period, period, period, clocks[period], ["架空科目A"], 1)).ToArray(), kind == MaterialKind.ExamReturn ? Day.Iso() : null);
        var engine = new TimetableEngine(new(Specials: [source]));
        var block = Assert.Single(engine.Blocks(Day, "1_CN"));
        Assert.False(engine.IsInProgress(Day, "1_CN", block, DateTimeOffset.Parse(Day.Iso() + "T09:25:00+09:00")));
        Assert.True(engine.IsInProgress(Day, "1_CN", block, DateTimeOffset.Parse(Day.Iso() + "T09:00:00+09:00")));
        Assert.False(engine.IsInProgress(Day, "1_CN", block, DateTimeOffset.Parse(Day.Iso() + "T09:50:00+09:00")));
        var continuous = source with { Lessons = source.Lessons.Select(lesson => lesson with { SpanStart = 1, SpanEnd = 2, RecordedTime = new("09:00", "09:50") }).ToArray() };
        var joined = new TimetableEngine(new(Specials: [continuous]));
        Assert.True(joined.IsInProgress(Day, "1_CN", Assert.Single(joined.Blocks(Day, "1_CN")), DateTimeOffset.Parse(Day.Iso() + "T09:25:00+09:00")));
    }
    [Fact]
    public void HiddenSpecialDoesNotRemoveTheVisibleCommonClock()
    {
        var exam = Special(MaterialKind.Exam, new("09:00", "09:40"));
        exam = exam with { Lessons = exam.Lessons.Select(lesson => lesson with { Lines = ["留 架空試験科目A"] }).ToArray() };
        var returns = Special(MaterialKind.ExamReturn, new("10:00", "10:20"));
        foreach (var sources in new[] { new[] { exam, returns }, new[] { returns, exam } })
        {
            var data = new ScheduleData(Specials: sources);
            var hidden = new TimetableEngine(data);
            Assert.Single(hidden.Blocks(Day, "1_CN"));
            Assert.Equal(new("10:00", "10:20"), hidden.CommonPeriodTime(1, [Day], ["1_CN"]));
            Assert.Null(new TimetableEngine(data, international: true).CommonPeriodTime(1, [Day], ["1_CN"]));
        }
    }
    [Fact]
    public void ChangeClockIsUnknownWhenOneRequiredSpecialKindIsMissing()
    {
        var events = new[] { "テスト", "テスト返却" }.Select(tag => new SchoolEvent(Day.Iso(), "架空行事", tag)).ToArray();
        var exam = Special(MaterialKind.Exam, new("09:00", "09:45"));
        var returns = Special(MaterialKind.ExamReturn, new("09:00", "09:45"));
        foreach (var sources in new[] { new[] { exam }, new[] { returns }, new[] { exam, returns } })
        {
            var change = Change();
            var data = new ScheduleData(Changes: [change], Specials: sources, Events: events);
            var engine = new TimetableEngine(data);
            var block = Assert.Single(engine.Blocks(Day, "1_CN"));
            Assert.Equal(sources.Length == 2 ? new TimeRange("09:00", "09:45") : null, engine.CardTime(Day, "1_CN", block));
            Assert.Equal(sources.Length == 2 ? new TimeRange("09:00", "09:45") : null, engine.CommonPeriodTime(1, [Day], ["1_CN"]));
            if (sources.Length == 1)
            {
                Assert.Equal(new TimeRange?[] { null }, engine.ChangeTimes(change));
                Assert.False(engine.IsInProgress(Day, "1_CN", block, DateTimeOffset.Parse(Day.Iso() + "T09:20:00+09:00")));
                var own = new TimetableEngine(data with { Changes = null });
                Assert.NotNull(own.CardTime(Day, "1_CN", Assert.Single(own.Blocks(Day, "1_CN"))));
            }
        }
    }

    [Fact]
    public void OverlappingSpecialCardsKeepIndividualTimesAndProgressWhileCommonAndChangeRemainAmbiguous()
    {
        var exam = Special(MaterialKind.Exam, new("09:00", "09:40"));
        var returns = Special(MaterialKind.ExamReturn, new("10:00", "10:20"));
        SpecialAnalysis[][] combinations = [[exam], [returns], [exam, returns], [returns, exam],
            [exam, Special(MaterialKind.ExamReturn, new("09:00", "09:40"))]];
        foreach (var sources in combinations)
        {
            var engine = new TimetableEngine(new(Timetable(Lesson(1)), Specials: sources));
            var blocks = engine.Blocks(Day, "1_CN");
            Assert.Equal(sources.Length, blocks.Count);
            foreach (var block in blocks)
            {
                var item = Assert.IsType<SpecialContent>(block.Content);
                Assert.Equal(item.Time, engine.CardTime(Day, "1_CN", block));
                Assert.Equal(item.Time == new TimeRange("09:00", "09:40"), engine.IsInProgress(Day, "1_CN", block,
                    new DateTimeOffset(2032, 4, 5, 9, 10, 0, TimeSpan.FromHours(9))));
            }
            var ambiguous = sources.Length == 2 && sources[0].PeriodTimes[1] != sources[1].PeriodTimes[1];
            var common = engine.CommonPeriodTime(1, [Day], ["1_CN"]);
            Assert.Equal(ambiguous ? null : sources[0].PeriodTimes[1], common);
            Assert.Equal(common, engine.CardTime(Day, "1_CN", new(1, 1, new ChangeContent(Change()))));
        }
    }

    [Fact]
    public void MergedSpecialCardsUseOnlyTheirOwnSourceAndNeverBorrowMissingTimes()
    {
        SpecialAnalysis Source(MaterialKind kind, string start, string middle, string end, bool joined) => new(kind, 2032,
            [Day.Iso()], ["1_CN"], new Dictionary<int, TimeRange> { [1] = new(start, middle), [2] = new(middle, end) },
            new[] { 1, 2 }.Select(p => new SpecialLesson(Day.Iso(), "1_CN", p, joined ? 1 : p, joined ? 2 : p,
                joined ? new(start, end) : p == 1 ? new(start, middle) : new(middle, end), ["架空特別科目A"], 1)).ToArray(), kind == MaterialKind.ExamReturn ? Day.Iso() : null);
        foreach (var joined in new[] { false, true })
        {
            var engine = new TimetableEngine(new(Specials: [Source(MaterialKind.Exam, "09:00", "09:20", "09:40", joined),
                Source(MaterialKind.ExamReturn, "10:00", "10:10", "10:20", joined)]));
            var blocks = engine.Blocks(Day, "1_CN");
            Assert.Equal(2, blocks.Count);
            foreach (var block in blocks)
            {
                Assert.Equal((1, 2), (block.StartPeriod, block.EndPeriod));
                var item = Assert.IsType<SpecialContent>(block.Content);
                Assert.Equal(item.Kind == MaterialKind.Exam ? new("09:00", "09:40") : new TimeRange("10:00", "10:20"), engine.CardTime(Day, "1_CN", block));
            }
            Assert.Null(Assert.Single(engine.ChangeTimes(Change("1,2"))));
        }
        var missing = Source(MaterialKind.Exam, "09:00", "09:20", "09:40", false) with { PeriodTimes = new Dictionary<int, TimeRange>() };
        missing = missing with { Lessons = missing.Lessons.Select(l => l with { RecordedTime = null }).ToArray() };
        var unknown = new TimetableEngine(new(Specials: [missing, Source(MaterialKind.ExamReturn, "10:00", "10:10", "10:20", false)]));
        Assert.Null(unknown.CardTime(Day, "1_CN", unknown.Blocks(Day, "1_CN")[0]));
    }

    private static readonly DateOnly Day = new(2032, 4, 5);
    private static NormalLesson Lesson(int period, string subject = "架空科目A", string teacher = "架空教員A", string room = "架空教室A") =>
        new("1_CN", Day.SchoolWeekday(), period, new(subject, teacher, room), "架空原文A", 1);
    private static ScheduleChange Change(string period = "1", string note = "変更", string after = "架空科目B") =>
        new(Day.Iso(), "1_CN", period, "架空科目A", after, "架空教員B", "架空教室B", note, "架空原文B", "架空正規化B");
    private static TimetableAnalysis Timetable(params NormalLesson[] lessons) => new(2032, "前期", lessons);
    private static SpecialAnalysis Special(MaterialKind kind, TimeRange time) => new(kind, 2032, [Day.Iso()], ["1_CN"],
        new Dictionary<int, TimeRange> { [1] = time }, [new(Day.Iso(), "1_CN", 1, 1, 1, time, ["架空科目C", "架空教員C", ""], 1)], kind == MaterialKind.ExamReturn ? Day.Iso() : null);

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
    [InlineData(MaterialKind.Timetable)]
    [InlineData(MaterialKind.Exam)]
    [InlineData(MaterialKind.ExamReturn)]
    public void HiddenInternationalChangesKeepOrdinaryAndSpecialLessonsIncludingSaturday(MaterialKind sourceKind)
    {
        foreach (var day in new[] { Day, new DateOnly(2032, 4, 10) })
        foreach (var kind in new[] { "休講", "変更", "補講" })
        {
            var row = Change(note: kind) with { ChangeDate = day.Iso(), BeforeSubject = "留 架空科目B", AfterSubject = kind == "休講" ? "" : "留 架空科目C" };
            var clock = new TimeRange("09:00", "09:45");
            var specials = sourceKind == MaterialKind.Timetable ? Array.Empty<SpecialAnalysis>() :
                new[] { new SpecialAnalysis(sourceKind, 2032, [day.Iso()], ["1_CN"], new Dictionary<int, TimeRange> { [1] = clock },
                    [new(day.Iso(), "1_CN", 1, 1, 1, clock, ["架空科目A"], 1)], sourceKind == MaterialKind.ExamReturn ? day.Iso() : null) };
            var data = new ScheduleData(Timetable(Lesson(1)), [row], specials,
                Events: [new(day.Iso(), "架空振替A", "曜日振替", ScheduleDay: 1, Classification: EventClassification.WeekdayOverride)]);
            var hidden = new TimetableEngine(data);
            var block = Assert.Single(hidden.Blocks(day, "1_CN"));
            if (sourceKind == MaterialKind.Timetable) Assert.IsType<NormalContent>(block.Content);
            else Assert.Equal(sourceKind, Assert.IsType<SpecialContent>(block.Content).Kind);
            Assert.Equal("架空科目A", block.Content.Names.Subject);
            Assert.Equal(sourceKind == MaterialKind.Timetable ? new TimeRange("08:50", "09:35") : clock, hidden.SlotTime(day, "1_CN", 1));
            Assert.Equal(row, Assert.Single(hidden.Slot(day, 1, "1_CN").Changes));
            Assert.IsType<ChangeContent>(Assert.Single(new TimetableEngine(data, international: true).Blocks(day, "1_CN")).Content);
            Assert.Equal(block.Content, Assert.Single(new TimetableEngine(data, includesChanges: false, international: true).Blocks(day, "1_CN")).Content);
        }
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

    [Theory]
    [InlineData(MaterialKind.Exam)]
    [InlineData(MaterialKind.ExamReturn)]
    public void ReplacedMergedSpecialUsesVerifiedClocksForRemainingFragments(MaterialKind kind)
    {
        var clocks = new Dictionary<int, TimeRange> { [1] = new("09:00", "09:20"), [2] = new("09:30", "09:50"), [3] = new("10:00", "10:20") };
        var source = new SpecialAnalysis(kind, 2032, [Day.Iso()], ["1_CN"], clocks,
            Enumerable.Range(1, 3).Select(p => new SpecialLesson(Day.Iso(), "1_CN", p, 1, 3, new("09:00", "10:20"), ["架空特別科目A"], 1)).ToArray(), kind == MaterialKind.ExamReturn ? Day.Iso() : null);
        foreach (var replaced in new[] { "1", "3", "2", "1,2", "" })
        {
            var engine = new TimetableEngine(new(Changes: replaced.Length == 0 ? [] : [Change(replaced)], Specials: [source]));
            foreach (var changed in engine.Blocks(Day, "1_CN").Where(b => b.Content is ChangeContent))
            {
                var expected = new TimeRange(clocks[changed.StartPeriod].Start, clocks[changed.EndPeriod].End);
                Assert.Equal(expected, engine.CardTime(Day, "1_CN", changed));
                var start = TimeOnly.Parse(expected.Start); var end = TimeOnly.Parse(expected.End);
                DateTimeOffset At(TimeOnly time) => new(Day.ToDateTime(time), TimeSpan.FromHours(9));
                Assert.False(engine.IsInProgress(Day, "1_CN", changed, At(start.AddMinutes(-1))));
                Assert.True(engine.IsInProgress(Day, "1_CN", changed, At(start)));
                Assert.False(engine.IsInProgress(Day, "1_CN", changed, At(end)));
            }
            foreach (var block in engine.Blocks(Day, "1_CN").Where(b => b.Content is SpecialContent))
            {
                var expected = block.StartPeriod == 1 && block.EndPeriod == 3 ? new TimeRange("09:00", "10:20")
                    : new TimeRange(clocks[block.StartPeriod].Start, clocks[block.EndPeriod].End);
                Assert.Equal(expected, engine.CardTime(Day, "1_CN", block));
                if (block.StartPeriod > 1) Assert.False(engine.IsInProgress(Day, "1_CN", block, new(2032, 4, 5, 9, 5, 0, TimeSpan.FromHours(9))));
            }
        }
        var missing = source with { PeriodTimes = new Dictionary<int, TimeRange>() };
        var unknown = new TimetableEngine(new(Changes: [Change("1,2")], Specials: [missing]));
        Assert.Null(unknown.CardTime(Day, "1_CN", unknown.Blocks(Day, "1_CN").Single(b => b.Content is SpecialContent)));
        Assert.All(source.Lessons, l => Assert.Equal(new TimeRange("09:00", "10:20"), l.RecordedTime));
    }
    [Fact]
    public void ConflictingOverridesWarnAndUseRealWeekdayWithoutLosingOtherTags()
    {
        SchoolEvent Override(int weekday) => new(Day.Iso(), "架空振替A", "曜日振替", Day.AddDays(2).Iso(), EventClassification.WeekdayOverride, weekday);
        var first = Override(2); var second = Override(3);
        var timetable = Timetable(Lesson(1));
        var events = new[] { first, second, new SchoolEvent(Day.Iso(), "架空メモA", "行事メモ") };
        var engine = new TimetableEngine(new(timetable, Events: events));
        Assert.Equal("曜日振替を確定できません", engine.Plan(Day).WeekdayOverrideWarning);
        Assert.IsType<NormalContent>(Assert.Single(engine.Blocks(Day, "1_CN")).Content);
        Assert.Equal("曜日振替を確定できません", engine.Plan(Day.AddDays(1)).WeekdayOverrideWarning);
        Assert.Null(new TimetableEngine(new(timetable, Events: [first, first])).Plan(Day).WeekdayOverrideWarning);
        Assert.Null(new TimetableEngine(new(timetable, Events: [first])).Plan(Day).WeekdayOverrideWarning);
        Assert.Null(new TimetableEngine(new(timetable)).Plan(Day).WeekdayOverrideWarning);
        Assert.Equal(3, engine.Plan(Day).Events.Count);
        var supplementary = new TimetableEngine(new(timetable, Events: [first, second, new(Day.Iso(), "架空補講A", "補講日", Classification: EventClassification.Supplementary)]));
        Assert.Empty(supplementary.Blocks(Day, "1_CN"));
        Assert.True(supplementary.Plan(Day).IsSupplementary);
        Assert.Equal("曜日振替を確定できません", supplementary.Plan(Day).WeekdayOverrideWarning);
    }


    [Fact]
    public void LegacyReturnRangeRemainsVerbatimButUnknownColumnDateCannotSupplyPeriodClocks()
    {
        var source = Special(MaterialKind.ExamReturn, new("10:30", "11:50")) with
        {
            CoveredDates = ["2032-04-04", Day.Iso()], DedicatedTimeDate = null,
            Lessons = [new(Day.Iso(), "1_CN", 5, 5, 6, new("10:30", "11:50"), ["架空科目A"], 1)]
        };
        var restored = System.Text.Json.JsonSerializer.Deserialize<SpecialAnalysis>(System.Text.Json.JsonSerializer.Serialize(source))!;
        Assert.Null(restored.PeriodTime(Day, 5)); Assert.Null(restored.PeriodTime(Day.AddDays(-1), 5));
        Assert.Equal(new TimeRange("10:30", "11:50"), restored.TimeFor(restored.Lessons[0]));
        Assert.Null((restored with { DedicatedTimeDate = "2032-04-01" }).PeriodTime(Day,5));
    }
}
