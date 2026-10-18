using System.Security.Cryptography;
using System.Text.Json;
using Takupoke.Core.Recovery;
using Takupoke.Win.ViewModels;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class RecoveryManualReviewBudgetTests
{
    private static RecoveryDocument ReviewDocument()
    {
        var doc = RecoveryManualAssistanceTests.Captured(4);
        return CaptureRequested(doc);
    }
    internal static RecoveryDocument CaptureRequested(RecoveryDocument doc, IReadOnlyList<RecoveryFieldTarget>? selected = null, int manualSchemaVersion = 2)
    {
        doc = doc with { Capture = doc.Capture! with { ManualSchemaVersion = manualSchemaVersion } };
        var targets = Assert.IsAssignableFrom<IReadOnlyList<RecoveryManualTarget>>(selected is null
            ? RecoveryManualAssistance.CaptureTargets(doc) : RecoveryManualAssistance.CaptureTargets(doc, selected));
        var crops = targets.Select(t =>
        {
            var x = (int)Math.Floor(t.Crop.X); var y = (int)Math.Floor(t.Crop.Y);
            var width = (int)Math.Ceiling(t.Crop.X + t.Crop.Width) - x;
            var height = (int)Math.Ceiling(t.Crop.Y + t.Crop.Height) - y;
            var pixels = Enumerable.Repeat((byte)73, width * height * 4).ToArray();
            return new RecoveryOriginalCrop(t.Target, t.Page, x, y, width, height,
                doc.Capture!.Pages.Single(p => p.Page == t.Page).RasterHash, pixels,
                Convert.ToHexStringLower(SHA256.HashData(pixels)));
        }).ToArray();
        return doc with { Capture = doc.Capture! with { OriginalCrops = crops } };
    }

    private static RecoveryHumanCorrection Review(RecoveryManualPlan plan, RecoveryManualTarget target, string text) =>
        RecoveryManualAssistanceTests.Correction(plan, target, text) with { ManualSchemaVersion = 2 };

    [Fact]
    public void ExplicitSelectedConfidentOcrCanBeCorrectedWithoutLoweringItsNativeScore()
    {
        var doc = RecoveryManualAssistanceTests.Captured(0);
        var cell = doc.Cells.First(c => c.LessonBindings.Count > 0);
        var target = new RecoveryFieldTarget(cell.Id, 0, RecoveryFieldRole.Subject);
        doc = CaptureRequested(doc, [target]);
        var before = JsonSerializer.SerializeToUtf8Bytes(doc);
        Assert.Null(RecoveryManualAssistance.Prepare(doc));
        var plan = Assert.IsType<RecoveryManualPlan>(RecoveryManualAssistance.Prepare(doc, [target]));
        Assert.True(RecoveryManualAssistance.VerifyPlan(plan));
        var correction = Review(plan, Assert.Single(plan.Targets), "架空の明示訂正");
        var result = RecoveryManualAssistance.Complete(plan, [correction], "host-control");
        Assert.True(RecoveryValidator.Validate(doc, result).CanAdopt);
        Assert.All(plan.Targets[0].OriginalParentIds, id => Assert.Equal(.95, doc.Sources.Single(s => s.Id == id).NativeConfidence));
        Assert.Equal(before, JsonSerializer.SerializeToUtf8Bytes(doc));
        Assert.False(RecoveryValidator.Validate(doc, result with { HumanCorrections = null }).CanAdopt);
        var preview = new Takupoke.Infrastructure.Storage.RecoveryPreview("fictional", new(1, new(2032, 1)), doc, result, DateTimeOffset.UtcNow);
        var display = Assert.IsType<RecoveryPreviewDisplay>(RecoveryPreviewDisplay.Create(preview, CancellationToken.None));
        Assert.Single(display.Corrections);
        Assert.Contains(RecoveryFieldRole.Subject, display.Cells.Single(c => c.CellId == cell.Id).Lessons[0].EditableRoles);
    }

    [Fact]
    public void HeaderAndUnknownSelectionCannotBecomeAHighConfidenceCorrection()
    {
        var doc = RecoveryManualAssistanceTests.Captured(0);
        Assert.Null(RecoveryManualAssistance.CaptureTargets(doc, [new("unknown", 0, RecoveryFieldRole.Subject)]));
        var cell = doc.Cells.First(c => c.LessonBindings.Count > 0);
        Assert.Null(RecoveryManualAssistance.CaptureTargets(doc, [new(cell.Id, -1, RecoveryFieldRole.Subject)]));
        Assert.Null(RecoveryManualAssistance.CaptureTargets(doc, [new(cell.Id, 0, (RecoveryFieldRole)999)]));
        var target = new RecoveryFieldTarget(cell.Id, 0, RecoveryFieldRole.Subject);
        Assert.Null(RecoveryManualAssistance.CaptureTargets(doc, [target, target]));
    }

    [Fact]
    public void UnchangedReviewsAreShownAsCheckedFieldsRatherThanTextCorrections()
    {
        var plan = Assert.IsType<RecoveryManualPlan>(RecoveryManualAssistance.Prepare(ReviewDocument()));
        var result = RecoveryManualAssistance.Complete(plan, plan.Targets.Select(t => Review(plan, t, t.OriginalOcr)).ToArray(), "host-control");
        var preview = new Takupoke.Infrastructure.Storage.RecoveryPreview("fictional", new(1, new(2032, 1)), plan.Document, result, DateTimeOffset.UtcNow);
        var display = Assert.IsType<RecoveryPreviewDisplay>(RecoveryPreviewDisplay.Create(preview, CancellationToken.None));
        Assert.Empty(display.Corrections); Assert.Equal(4, display.ReviewedFieldCount);
    }

    [Fact]
    public void FourUncertainFieldsCanBeConfirmedWithoutChangingTheirOcrOrConfidence()
    {
        var doc = ReviewDocument(); var original = JsonSerializer.SerializeToUtf8Bytes(doc);
        var plan = Assert.IsType<RecoveryManualPlan>(RecoveryManualAssistance.Prepare(doc));
        var reviews = plan.Targets.Select(t => Review(plan, t, t.OriginalOcr)).ToArray();
        var result = RecoveryManualAssistance.Complete(plan, reviews, "host-control");
        Assert.True(RecoveryValidator.Validate(doc, result).CanAdopt);
        Assert.Equal(4, result.HumanCorrections!.Count);
        Assert.Equal(original, JsonSerializer.SerializeToUtf8Bytes(doc));
        Assert.Equal(4, doc.Sources.Count(s => s.NativeConfidence == .4));
        Assert.All(reviews, r => Assert.Equal("user", r.Provenance));
        Assert.False(RecoveryValidator.Validate(doc, result with { HumanCorrections = null }).CanAdopt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void ConfirmationsDoNotConsumeTheThreeActualFieldEditBudget(int changed)
    {
        var plan = Assert.IsType<RecoveryManualPlan>(RecoveryManualAssistance.Prepare(ReviewDocument()));
        var reviews = plan.Targets.Select((t, i) => Review(plan, t,
            i < changed ? "独立架空訂正" + i : t.OriginalOcr)).ToArray();
        var result = RecoveryManualAssistance.Complete(plan, reviews, "host-control");
        Assert.True(RecoveryValidator.Validate(plan.Document, result).CanAdopt);
    }

    [Fact]
    public void FourActualEditsAndUnreviewedOrForgedFieldsRemainRejected()
    {
        var plan = Assert.IsType<RecoveryManualPlan>(RecoveryManualAssistance.Prepare(ReviewDocument()));
        var reviews = plan.Targets.Select(t => Review(plan, t, t.OriginalOcr)).ToArray();
        Assert.Throws<InvalidRecoveryOutputException>(() => RecoveryManualAssistance.Complete(plan,
            reviews.Select((r, i) => r with { CorrectedText = "独立架空訂正" + i }).ToArray(), "host-control"));
        Assert.Throws<InvalidRecoveryOutputException>(() => RecoveryManualAssistance.Complete(plan, reviews[..3], "host-control"));
        Assert.Throws<InvalidRecoveryOutputException>(() => RecoveryManualAssistance.Complete(plan,
            reviews.Select(r => r with { ManualSchemaVersion = 1 }).ToArray(), "host-control"));
        var broken = plan.Document with { Capture = plan.Document.Capture! with {
            OriginalCrops = plan.Document.Capture!.OriginalCrops!.Select((c, i) => i == 0 ? c with { Sha256 = new string('0', 64) } : c).ToArray() } };
        Assert.Null(RecoveryManualAssistance.Prepare(broken));
        Assert.Null(RecoveryManualAssistance.Prepare(RecoveryManualAssistanceTests.Captured(4)));
    }

    [Fact]
    public void ReviewInputRequiresEveryAcknowledgementAndLimitsActualChanges()
    {
        var state = new RecoveryManualInputState();
        var original = Enumerable.Range(0, 4).ToDictionary(i => "field" + i, i => "架空" + i);
        state.Bind("review", original, manualSchemaVersion: 2);
        Assert.False(state.Ready);
        foreach (var key in original.Keys) state.Acknowledge(key, true);
        Assert.True(state.Ready); Assert.Equal(0, state.ChangedCount);
        foreach (var key in original.Keys.Take(3)) { state.Edit(key, "訂正" + key); state.Acknowledge(key, true); }
        Assert.True(state.Ready); Assert.Equal(3, state.ChangedCount);
        state.Edit("field3", "第四訂正"); state.Acknowledge("field3", true);
        Assert.False(state.Ready); Assert.Throws<InvalidOperationException>(() => state.Submission());
        state.Edit("field3", original["field3"]);
        Assert.False(state.Ready);
        state.Acknowledge("field3", true); Assert.True(state.Ready);
        state.Clear(); Assert.Empty(state.Values); Assert.False(state.Ready);
        state.Bind("legacy", original); Assert.Empty(state.Values);
    }

    [Fact]
    public void ReviewInputStillHasABoundedNumberOfFields()
    {
        var state = new RecoveryManualInputState();
        var fields = Enumerable.Range(0, RecoveryManualAssistance.MaximumReviewFields + 1)
            .ToDictionary(i => "field" + i, _ => "架空");
        state.Bind("too-many", fields, manualSchemaVersion: 2);
        Assert.Empty(state.Values); Assert.False(state.Ready);
    }
}
