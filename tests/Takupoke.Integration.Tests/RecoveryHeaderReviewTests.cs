using System.Text.Json;
using Takupoke.Core.Recovery;
using Takupoke.Win.ViewModels;
using Xunit;
namespace Takupoke.Integration.Tests;

public sealed class RecoveryHeaderReviewTests
{
    internal static RecoveryDocument CapturedHeader()
    {
        var doc = RecoveryManualAssistanceTests.Captured(0);
        var id = doc.ClassEvidence.Values.First()[0];
        var sources = doc.Sources.Select(s => s.Id == id ? s with { NativeConfidence = .75 } : s).ToArray();
        doc = doc with { Sources = sources, Capture = doc.Capture! with { ManualSchemaVersion = 2,
            SourceSnapshot = RecoveryValidator.Fingerprint(sources) } };
        return RecoveryManualReviewBudgetTests.CaptureRequested(doc);
    }
    [Fact]
    public void HeaderCanOnlyBeConfirmedUnchangedWithOwnedOriginalPixelsAndAudit()
    {
        var doc = CapturedHeader(); var snapshot = JsonSerializer.SerializeToUtf8Bytes(doc);
        var plan = Assert.IsType<RecoveryManualPlan>(RecoveryManualAssistance.Prepare(doc));
        var target = Assert.Single(plan.Targets);
        Assert.Equal(RecoveryHeaderReviewRole.Class, target.HeaderProof!.Role);
        Assert.DoesNotContain(doc.Cells, c => c.Id == target.Target.CellId);
        var review = RecoveryManualAssistanceTests.Correction(plan, target, target.OriginalOcr) with { ManualSchemaVersion = 2 };
        var result = RecoveryManualAssistance.Complete(plan, [review], "control");
        Assert.True(RecoveryValidator.Validate(doc, result).CanAdopt);
        Assert.Equal(snapshot, JsonSerializer.SerializeToUtf8Bytes(doc));
        Assert.Contains(doc.Sources, s => s.NativeConfidence == .75);
        Assert.False(RecoveryValidator.Validate(doc, result with { HumanCorrections = null }).CanAdopt);
        Assert.Throws<InvalidRecoveryOutputException>(() => RecoveryManualAssistance.Complete(plan,
            [review with { CorrectedText = "AI_1" }], "control"));
        Assert.Throws<InvalidRecoveryOutputException>(() => RecoveryManualAssistance.Complete(plan,
            [review with { HeaderProof = null }], "control"));
        Assert.Throws<InvalidRecoveryOutputException>(() => RecoveryManualAssistance.Complete(plan,
            [review with { HeaderProof = review.HeaderProof! with { ScopeKey = "AI_1" } }], "control"));
        var preview = new Takupoke.Infrastructure.Storage.RecoveryPreview("fictional", new(1, new(2032, 1)), doc, result, DateTimeOffset.UtcNow);
        var display = Assert.IsType<RecoveryPreviewDisplay>(RecoveryPreviewDisplay.Create(preview, CancellationToken.None));
        Assert.Empty(display.Corrections); Assert.Equal(1, display.ReviewedFieldCount);
        Assert.Null(RecoveryManualAssistance.Prepare(doc with { Capture = doc.Capture! with { ManualSchemaVersion = 1 } }));
    }
    [Fact]
    public void HeaderInputIsReadOnlyAndStillRequiresExplicitAcknowledgement()
    {
        var plan = Assert.IsType<RecoveryManualPlan>(RecoveryManualAssistance.Prepare(CapturedHeader()));
        var target = Assert.Single(plan.Targets); var originals = new Dictionary<string,string> { [target.Target.Key] = target.OriginalOcr };
        var state = new RecoveryManualInputState(); state.Bind("source", originals, 2, originals, [target.Target.Key]);
        Assert.False(state.Ready);
        Assert.Throws<InvalidOperationException>(() => state.Edit(target.Target.Key, "AI_1"));
        Assert.Equal(target.OriginalOcr, state.Values[target.Target.Key]);
        state.Acknowledge(target.Target.Key, true); Assert.True(state.Ready);
        Assert.Equal(target.OriginalOcr, state.Submission()[target.Target.Key]);
    }
    [Fact]
    public void UnknownOrMissingStructuralHeaderCannotBeTypedIntoExistence()
    {
        var doc = CapturedHeader(); var id = doc.ClassEvidence.Values.First()[0];
        var sources = doc.Sources.Select(s => s.Id == id ? s with { Text = "不明クラス" } : s).ToArray();
        var invalid = doc with { Sources = sources, Capture = doc.Capture! with { SourceSnapshot = RecoveryValidator.Fingerprint(sources) } };
        Assert.Null(RecoveryManualAssistance.CaptureTargets(invalid));
        Assert.Null(RecoveryManualAssistance.Prepare(invalid));
    }
}
