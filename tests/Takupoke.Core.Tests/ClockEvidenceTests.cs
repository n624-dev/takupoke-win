using Takupoke.Core;
using Xunit;

namespace Takupoke.Core.Tests;

public sealed class ClockEvidenceTests
{
    private static readonly DateOnly Day = new(2032, 4, 5);
    private static DateTimeOffset At(string time) => new(Day.ToDateTime(TimeOnly.Parse(time)), TimeSpan.FromHours(9));
    private static SpecialAnalysis Source(MaterialKind kind, TimeRange? recorded, bool known = true) => new(kind, 2032, [Day.Iso()], ["1_CN"],
        known ? new Dictionary<int, TimeRange> { [1] = new("08:50", "09:35"), [2] = new("09:50", "10:35") } : new Dictionary<int, TimeRange>(),
        new[] { 1, 2 }.Select(p => new SpecialLesson(Day.Iso(), "1_CN", p, 1, 2, recorded, ["架空科目A", "架空教員A", "架空教室A"], 1)).ToArray(), kind == MaterialKind.ExamReturn ? Day.Iso() : null);
    private static ScheduleChange Change(string periods, bool teacherOnly = false) => new(Day.Iso(), "1_CN", periods, "架空科目A",
        teacherOnly ? "架空科目A" : "架空科目B", "架空教員B", "架空教室A", "変更", "架空原文", "架空正規化");
    [Theory]
    [InlineData(MaterialKind.Exam)]
    [InlineData(MaterialKind.ExamReturn)]
    public void WholeReplacementKeepsRecordedContinuousClockWhilePartialChangesUsePeriodClocks(MaterialKind kind)
    {
        foreach (var teacherOnly in new[] { false, true })
        foreach (var (periods, expected) in new[] { ("1,2", new TimeRange("08:50", "10:20")), ("1", new("08:50", "09:35")), ("2", new("09:50", "10:35")) })
        {
            var row = Change(periods, teacherOnly);
            var engine = new TimetableEngine(new(Changes: [row], Specials: [Source(kind, new("08:50", "10:20"))]));
            var block = Assert.Single(engine.Blocks(Day, "1_CN"), b => b.Content is ChangeContent);
            Assert.Equal(expected, engine.CardTime(Day, "1_CN", block)); Assert.Equal(expected, Assert.Single(engine.ChangeTimes(row)));
            Assert.True(engine.IsInProgress(Day, "1_CN", block, At(expected.Start)));
            Assert.False(engine.IsInProgress(Day, "1_CN", block, At(expected.End)));
            if (periods == "1,2")
            {
                Assert.False(engine.IsInProgress(Day, "1_CN", block, At("10:25")));
                Assert.True(engine.IsInProgress(Day, "1_CN", block, At("09:40")));
            }
        }
        var unrecorded = new TimetableEngine(new(Changes: [Change("1,2")], Specials: [Source(kind, null)]));
        Assert.Equal(new("08:50", "10:35"), unrecorded.CardTime(Day, "1_CN", Assert.Single(unrecorded.Blocks(Day, "1_CN"))));
    }
    [Fact]
    public void WholeReplacementRequiresEveryApplicableDocumentToProvideTheSameRange()
    {
        foreach (var second in new[] { Source(MaterialKind.ExamReturn, new("08:50", "10:20")), Source(MaterialKind.ExamReturn, new("08:50", "10:30")), Source(MaterialKind.ExamReturn, null, known: false) })
        {
            var engine = new TimetableEngine(new(Changes: [Change("1,2")], Specials: [Source(MaterialKind.Exam, new("08:50", "10:20")), second]));
            var expected = second.Lessons[0].RecordedTime == new TimeRange("08:50", "10:20") ? new TimeRange("08:50", "10:20") : null;
            Assert.Equal(expected, engine.CardTime(Day, "1_CN", Assert.Single(engine.Blocks(Day, "1_CN"))));
        }
    }
    [Theory]
    [InlineData(1, 2, "09:35", true)]
    [InlineData(2, 3, "10:25", false)]
    [InlineData(4, 5, "12:25", false)]
    public void NormalVisualMergesKeepBreaksAndLunchWithDefaultAndDateSpecificClocks(int first, int last, string gap, bool active)
    {
        foreach (var custom in new[] { false, true })
        foreach (var sameNames in new[] { false, true })
        {
            static string Shift(string value) => TimeOnly.Parse(value).AddMinutes(5).ToString("HH:mm");
            var times = custom ? new ScheduleTimes(1, [new(Day.Iso(), ScheduleTimes.Normal.Select(pair => new PeriodTime(pair.Key, Shift(pair.Value.Start), Shift(pair.Value.End))).ToArray())]).Validated() : null;
            var lessons = new[] { first, last }.Select(p => new NormalLesson("1_CN", 1, p,
                new(sameNames || p == first ? "架空科目A" : "架空科目B"), "架空原文", 1)).ToArray();
            var engine = new TimetableEngine(new(new(2032, "前期", lessons), Times: times));
            var blocks = engine.Blocks(Day, "1_CN"); Assert.Equal(sameNames ? 1 : 2, blocks.Count);
            Assert.Equal(active, blocks.Any(b => engine.IsInProgress(Day, "1_CN", b, At(gap))));
            var start = times?.Time(Day, first)?.Start ?? ScheduleTimes.Normal[first].Start;
            var end = times?.Time(Day, last)?.End ?? ScheduleTimes.Normal[last].End;
            Assert.True(engine.IsInProgress(Day, "1_CN", blocks[0], At(start)));
            Assert.False(blocks.Any(b => engine.IsInProgress(Day, "1_CN", b, At(end))));
            if (custom) Assert.False(blocks.Any(b => engine.IsInProgress(Day, "1_CN", b, At(ScheduleTimes.Normal[first].Start))));
        }
    }
}
