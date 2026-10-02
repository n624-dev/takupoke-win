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
    [Fact]
    public void PendingChangesAreFilteredByCurrentClassesAndDateWithoutChangingTheStableOsTag()
    {
        var baseline = NotificationDiff.Reconcile(new(), NotificationDiff.Fingerprints([Change(), Change(date: "2032-04-06", cls: "2_CN")]),
            new Dictionary<string, string>(), Today, new HashSet<string> { "1_CN", "2_CN" }, true, true);
        var updated = NotificationDiff.Fingerprints([Change("架空更新A"), Change("架空更新B", "2032-04-06", "2_CN")]);
        var pending = NotificationDiff.Reconcile(baseline, updated, new Dictionary<string, string>(), Today, new HashSet<string> { "1_CN", "2_CN" }, true, true);
        Assert.Equal(2, pending.Pending!["changes"].Count);
        var filtered = NotificationDiff.Reconcile(pending, updated, new Dictionary<string, string>(), Today.AddDays(1), new HashSet<string> { "2_CN" }, true, true);
        var remaining = filtered.Pending!["changes"];
        Assert.Equal(1, remaining.Count);
        Assert.Equal(pending.Pending["changes"].Fingerprint, remaining.Fingerprint);
        Assert.Equal(new ChangeNoticeTarget("2032-04-06", "2_CN", "1,2"), Assert.Single(remaining.ChangeTargets!));
        Assert.Empty(NotificationDiff.Reconcile(filtered, updated, new Dictionary<string, string>(), Today.AddDays(2), Classes, true, true).Pending!);
    }
    [Fact]
    public void LegacyChangeNoticeIsDiscardedButComparisonBaselineRemainsUsable()
    {
        var changes = NotificationDiff.Fingerprints([Change()]);
        var legacy = new NotificationBaseline(changes, Pending: new Dictionary<string, PendingNotice> { ["changes"] = new("fake-legacy", 7) });
        var next = NotificationDiff.Reconcile(legacy, changes, new Dictionary<string, string>(), Today, Classes, true, true);
        Assert.Empty(next.Pending!);
        Assert.Equal(changes, next.Changes);
        var fresh = NotificationDiff.Reconcile(next, NotificationDiff.Fingerprints([Change("架空更新")]), new Dictionary<string, string>(), Today, Classes, true, true);
        Assert.Equal(1, fresh.Pending!["changes"].Count);
    }
    [Fact]
    public void UnsentSlotsAccumulateWithoutCountingRepeatedUpdatesToTheSameSlotTwice()
    {
        var baseline = NotificationDiff.Reconcile(new(), NotificationDiff.Fingerprints([Change(), Change(date: "2032-04-06")]), new Dictionary<string, string>(), Today, Classes, true, true);
        var first = NotificationDiff.Reconcile(baseline, NotificationDiff.Fingerprints([Change("架空更新A"), Change(date: "2032-04-06")]), new Dictionary<string, string>(), Today, Classes, true, true);
        Assert.Equal(1, first.Pending!["changes"].Count);
        var second = NotificationDiff.Reconcile(first, NotificationDiff.Fingerprints([Change("架空更新B"), Change("架空更新C", "2032-04-06")]), new Dictionary<string, string>(), Today, Classes, true, true);
        Assert.Equal(2, second.Pending!["changes"].Count);
        Assert.Equal(2, second.Pending["changes"].ChangeTargets!.Count);
    }
}
