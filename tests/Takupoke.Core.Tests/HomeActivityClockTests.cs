using Takupoke.Core;
using Xunit;

namespace Takupoke.Core.Tests;

public sealed class HomeActivityClockTests
{
    private static DateTimeOffset At(string time) => DateTimeOffset.Parse("2032-04-05T" + time + ":00+09:00");
    [Fact]
    public void OnlyCardSetChangesTriggerRedrawIncludingSimultaneousEdges()
    {
        var clock = new HomeActivityClock();
        clock.Configure(At("08:00"), [new("A", At("09:00"), At("10:00")), new("B", At("10:00"), At("11:00"))]);
        Assert.Equal(At("09:00"), clock.NextBoundary);
        Assert.False(clock.Tick(At("08:59")));
        Assert.True(clock.Tick(At("09:00")));
        Assert.False(clock.Tick(At("09:59")));
        Assert.True(clock.Tick(At("10:00"))); // still one card, but its identity changed
        Assert.Equal(At("11:00"), clock.NextBoundary);
        Assert.True(clock.Tick(At("11:00")));
        Assert.False(clock.Tick(At("11:01")));
    }
    [Fact]
    public void JapaneseMidnightAndNewlyConfiguredDayScheduleDoNotUseOldIntervals()
    {
        var clock = new HomeActivityClock(); var midnight = At("00:00").AddDays(1);
        clock.Configure(midnight.AddSeconds(-1), []);
        Assert.Equal(midnight, clock.NextBoundary);
        Assert.True(clock.Tick(midnight));
        Assert.False(clock.Tick(midnight.AddSeconds(1)));
        clock.Configure(midnight, [new("next-day", midnight.AddHours(9), midnight.AddHours(10))]);
        Assert.Equal(midnight.AddHours(9), clock.NextBoundary);
        Assert.True(clock.Tick(midnight.AddHours(9)));
        Assert.False(clock.Tick(midnight.AddHours(9).AddMinutes(15)));
    }
    [Fact]
    public void ClockRollbackForwardJumpReconfigureAndClearReevaluateActualActivity()
    {
        var clock = new HomeActivityClock();
        clock.Configure(At("09:30"), [new("A", At("09:00"), At("10:00"))]);
        Assert.True(clock.Tick(At("08:30"))); Assert.Equal(At("09:00"), clock.NextBoundary);
        Assert.True(clock.Tick(At("09:00")));
        Assert.True(clock.Tick(At("12:00")));
        clock.Configure(At("12:00"), [new("fresh", At("12:30"), At("13:00"))]);
        Assert.Equal(At("12:30"), clock.NextBoundary);
        Assert.True(clock.Tick(At("12:30")));
        clock.Clear(); Assert.Null(clock.NextBoundary);
        Assert.False(clock.Tick(At("13:00"))); Assert.False(clock.Tick(At("08:00")));
    }
    [Fact]
    public void SameCardKeepsItsNormalBreakAndLunchInActivityRanges()
    {
        var day = new DateOnly(2032, 4, 5);
        var normal = new TimetableAnalysis(2032, "前期", new[] {2,3}.Select(period =>
            new NormalLesson("1_CN", 1, period, new("架空科目A"), "架空原文", 1)).ToArray());
        var engine = new TimetableEngine(new(Timetable: normal));
        var block = Assert.Single(engine.Blocks(day, "1_CN"));
        Assert.Equal(new[] {(575,620),(630,675)}, engine.ActivityMinuteRanges(day, "1_CN", block));
        var intervals = engine.ActivityMinuteRanges(day, "1_CN", block).Select(range =>
            new HomeActivityInterval("same-card", At("00:00").AddMinutes(range.Start), At("00:00").AddMinutes(range.End)));
        var clock = new HomeActivityClock(); clock.Configure(At("09:00"), intervals);
        Assert.True(clock.Tick(At("09:35"))); Assert.True(clock.Tick(At("10:20")));
        Assert.False(clock.Tick(At("10:25"))); Assert.True(clock.Tick(At("10:30")));
        Assert.False(engine.IsInProgress(day, "1_CN", block, At("10:25")));
        Assert.True(engine.IsInProgress(day, "1_CN", block, At("10:30")));
    }
}
