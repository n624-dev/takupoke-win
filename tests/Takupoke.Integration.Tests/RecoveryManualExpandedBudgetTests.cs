using System.Text.Json;
using Takupoke.Core.Recovery;
using Takupoke.Win.ViewModels;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class RecoveryManualExpandedBudgetTests
{
    [Fact]
    public void FourActualBodyCorrectionsUseNewContractAndPreserveAcquisition()
    {
        var doc = RecoveryManualReviewBudgetTests.CaptureRequested(
            RecoveryManualAssistanceTests.Captured(4), manualSchemaVersion: 3);
        var original = JsonSerializer.SerializeToUtf8Bytes(doc);
        var plan = Assert.IsType<RecoveryManualPlan>(RecoveryManualAssistance.Prepare(doc));
        var corrections = plan.Targets.Select((t, i) => RecoveryManualAssistanceTests.Correction(plan, t, "架空訂正" + i)
            with { ManualSchemaVersion = 3 }).ToArray();
        Assert.Equal(4, corrections.Length); Assert.Equal(128, RecoveryManualAssistance.CorrectionLimit(doc));
        var result = RecoveryManualAssistance.Complete(plan, corrections, "control");
        Assert.True(RecoveryValidator.Validate(doc, result).CanAdopt);
        Assert.Equal(original, JsonSerializer.SerializeToUtf8Bytes(doc));
        Assert.Equal(4, result.HumanCorrections!.Count);
        Assert.Throws<InvalidRecoveryOutputException>(() => RecoveryManualAssistance.Complete(plan,
            corrections.Select(c => c with { ManualSchemaVersion = 2 }).ToArray(), "control"));
        Assert.Throws<InvalidRecoveryOutputException>(() => RecoveryManualAssistance.Complete(plan, corrections[..3], "control"));
    }

    [Fact]
    public void NewInputContractAllows128ExplicitEditsButNot129Targets()
    {
        var values = Enumerable.Range(0, 128).ToDictionary(i => "field" + i, i => "架空原文" + i);
        var state = new RecoveryManualInputState(); state.Bind("new", values, 3);
        foreach (var key in values.Keys) { state.Edit(key, "訂正" + key); state.Acknowledge(key, true); }
        Assert.True(state.Ready); Assert.Equal(128, state.ChangedCount); Assert.Equal(128, state.Submission().Count);
        state.Acknowledge("field127", false); Assert.False(state.Ready);
        state.Bind("too-many", values.Concat(new[] { new KeyValuePair<string,string>("field128", "架空") }).ToDictionary(p => p.Key, p => p.Value), 3);
        Assert.Empty(state.Values); Assert.False(state.Ready);
        state.Bind("legacy", values.Take(4).ToDictionary(p => p.Key, p => p.Value), 2);
        foreach (var key in state.Values.Keys.ToArray()) { state.Edit(key, "訂正" + key); state.Acknowledge(key, true); }
        Assert.False(state.Ready); Assert.Equal(4, state.ChangedCount);
    }

    [Fact]
    public void HeaderIsStillReadOnlyAndCannotConsumeNewBudgetForTyping()
    {
        var doc = RecoveryHeaderReviewTests.CapturedHeader();
        doc = doc with { Capture = doc.Capture! with { ManualSchemaVersion = 3 } };
        var plan = Assert.IsType<RecoveryManualPlan>(RecoveryManualAssistance.Prepare(doc));
        var target = Assert.Single(plan.Targets); var values = new Dictionary<string,string> { [target.Target.Key] = target.OriginalOcr };
        var state = new RecoveryManualInputState(); state.Bind("new", values, 3, values, [target.Target.Key]);
        Assert.Throws<InvalidOperationException>(() => state.Edit(target.Target.Key, "AI_1"));
        state.Acknowledge(target.Target.Key, true); Assert.True(state.Ready);
        var correction = RecoveryManualAssistanceTests.Correction(plan, target, target.OriginalOcr) with { ManualSchemaVersion = 3 };
        Assert.True(RecoveryValidator.Validate(doc, RecoveryManualAssistance.Complete(plan, [correction], "control")).CanAdopt);
        Assert.Throws<InvalidRecoveryOutputException>(() => RecoveryManualAssistance.Complete(plan,
            [correction with { CorrectedText = "AI_1" }], "control"));
    }

    [Fact]
    public void UnknownFutureSchemaDoesNotAuthorizeReview()
    {
        var doc = RecoveryManualAssistanceTests.Captured(1);
        Assert.Null(RecoveryManualAssistance.Prepare(doc with { Capture = doc.Capture! with { ManualSchemaVersion = 4 } }));
        var state = new RecoveryManualInputState(); state.Bind("future", new Dictionary<string,string> { ["field"] = "架空" }, 4);
        Assert.Empty(state.Values); Assert.False(state.Ready);
    }
}
