using Takupoke.Core;
using Xunit;

namespace Takupoke.Core.Tests;

public sealed class NotificationDiffTests
{
    private static ScheduleChange Change(string subject = "架空科目A", string date = "2032-04-05", string cls = "1_CN") => new(date, cls, "1,2", "架空科目B", subject, "", "", "", "架空原文", "架空比較文");
    private static readonly DateOnly Today = new(2032, 4, 5);
    private static readonly HashSet<string> Classes = ["1_CN"];
    [Fact]
    public void InitialImportSavesBaselineWithoutNotification()
    {
        var result = NotificationDiff.Reconcile(new(), NotificationDiff.Fingerprints([Change()]), new Dictionary<string, string> { ["exam"] = "fake-first" }, Today, Classes, true, true);
        Assert.Empty(result.Pending!);
        Assert.NotEmpty(result.Changes!);
    }
    [Fact]
    public void ReorderingDuplicatesAndClassSelectionAloneDoNotNotify()
    {
        var previous = NotificationDiff.Fingerprints([Change(), Change("架空科目C")]);
        var next = NotificationDiff.Fingerprints([Change("架空科目C"), Change(), Change()]);
        Assert.Equal(0, NotificationDiff.ChangeCount(previous, next, Today, Classes));
        Assert.Equal(0, NotificationDiff.ChangeCount(previous, next, Today, new HashSet<string> { "2_CN" }));
    }
    [Fact]
    public void ChangedSameSlotCountsOnceAndIgnoresPastOrOtherClasses()
    {
        var previous = NotificationDiff.Fingerprints([Change()]);
        var next = NotificationDiff.Fingerprints([Change("架空科目C"), Change(date: "2032-04-04"), Change(cls: "2_CN")]);
        Assert.Equal(1, NotificationDiff.ChangeCount(previous, next, Today, Classes));
        Assert.Equal(1, NotificationDiff.ChangeCount(previous, [], Today, Classes));
    }
    [Fact]
    public void OnlySuccessfulSpecialAnalysisDigestsAreComparedAndDisabledKindsClearPending()
    {
        var previous = new NotificationBaseline(SpecialDigests: new Dictionary<string, string> { ["exam"] = "fake-first" });
        var result = NotificationDiff.Reconcile(previous, null, new Dictionary<string, string> { ["exam"] = "fake-second" }, Today, Classes, true, true);
        Assert.Equal(1, result.Pending!["exam"].Count);
        Assert.Empty(NotificationDiff.Reconcile(result, null, new Dictionary<string, string>(), Today, Classes, true, false).Pending!);
    }
}
