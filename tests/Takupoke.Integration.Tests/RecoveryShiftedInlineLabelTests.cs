using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class RecoveryShiftedInlineLabelTests
{
    [Theory]
    [InlineData(MaterialKind.Timetable, false)]
    [InlineData(MaterialKind.Timetable, true)]
    [InlineData(MaterialKind.Exam, false)]
    [InlineData(MaterialKind.ExamReturn, false)]
    public async Task UniqueExplicitRolesOnSeparateRowsDoNotNeedIdenticalHorizontalLabelStarts(MaterialKind kind, bool emptyTeacher)
    {
        var page = Layout(kind, emptyTeacher: emptyTeacher);
        var snapshot = page.Glyphs.ToArray();
        var doc = RecoveryDocumentBuilder.Build(new string('a', 64), kind, [page], (_, box) => InkFree(page, box));
        var cell = Assert.Single(doc.Cells, c => !c.ConfirmedEmpty);
        Assert.Equal(3, cell.RoleScopes.Count);
        Assert.All(cell.RoleScopes, s => Assert.Equal(0, s.LessonIndex));
        var run = await RecoveryEngine.RunAsync(doc, "windows", 10, true, [], _ => null);
        Assert.Equal(RecoveryJobState.AwaitingConfirmation, run.State);
        Assert.True(RecoveryValidator.Validate(doc, run.Result!).CanAdopt);
        var lesson = Assert.Single(run.Result!.Cells.SelectMany(c => c.Lessons));
        Assert.Equal("科甲", lesson.Subject.Value);
        Assert.Equal(emptyTeacher ? "" : "教甲", lesson.Teacher.Value);
        Assert.Equal("室甲", lesson.Room.Value);
        var sources = doc.Sources.ToDictionary(s => s.Id);
        Assert.Equal("科甲", string.Concat(lesson.Subject.Evidence.Select(id => sources[id].Text)));
        Assert.Equal("室甲", string.Concat(lesson.Room.Evidence.Select(id => sources[id].Text)));
        if (emptyTeacher) Assert.Equal(RecoveryValueState.Empty, lesson.Teacher.State);
        Assert.Equal(snapshot, page.Glyphs);
    }

    [Theory]
    [InlineData(.4)]
    [InlineData(.95)]
    public void DeterministicallyProvedInlineBodyRolesCanUseSourceBoundSchemaTwoReview(double confidence)
    {
        var page = Layout(MaterialKind.Timetable);
        var doc = RecoveryDocumentBuilder.Build(new string('a', 64), MaterialKind.Timetable, [page], (_, box) => InkFree(page, box));
        var cell = Assert.Single(doc.Cells, c => !c.ConfirmedEmpty);
        Assert.Equal(RecoveryBindingMode.RoleProposal, cell.BindingMode);
        var raw = Assert.IsType<RecoveredCell>(RecoveryRules.Recover(doc, cell));
        var ids = raw.Lessons[0].Room.Evidence.ToHashSet();
        var sources = doc.Sources.Select(s => s with { FromOcr = true, NativeConfidence = ids.Contains(s.Id) ? confidence : .95 }).ToArray();
        doc = doc with { Sources = sources, Capture = new(doc.PdfHash, 1, RecoveryValidator.Fingerprint(sources),
            [new(1, (int)page.Width, (int)page.Height, new string('c', 64), sources.Length, true)], 2) };
        var target = new RecoveryFieldTarget(cell.Id, 0, RecoveryFieldRole.Room);
        doc = RecoveryManualReviewBudgetTests.CaptureRequested(doc, [target]);
        var snapshot = RecoveryValidator.Fingerprint(doc);
        var plan = Assert.IsType<RecoveryManualPlan>(RecoveryManualAssistance.Prepare(doc, [target]));
        var correction = RecoveryManualAssistanceTests.Correction(plan, Assert.Single(plan.Targets), "独立架空訂正") with { ManualSchemaVersion = 2 };
        var result = RecoveryManualAssistance.Complete(plan, [correction], "host-control");
        Assert.True(RecoveryValidator.Validate(doc, result).CanAdopt);
        Assert.Equal("独立架空訂正", result.Cells.Single(c => c.CellId == cell.Id).Lessons[0].Room.Value);
        Assert.Equal(snapshot, RecoveryValidator.Fingerprint(doc));
        Assert.All(ids, id => Assert.Equal(confidence, doc.Sources.Single(s => s.Id == id).NativeConfidence));
        var preview = new Takupoke.Infrastructure.Storage.RecoveryPreview("fictional", new(1, new(2026, 1)), doc, result, DateTimeOffset.UtcNow);
        var display = Assert.IsType<Takupoke.Win.ViewModels.RecoveryPreviewDisplay>(Takupoke.Win.ViewModels.RecoveryPreviewDisplay.Create(preview, CancellationToken.None));
        Assert.Contains(RecoveryFieldRole.Room, display.Cells.Single(c => c.CellId == cell.Id).Lessons[0].EditableRoles);
        Assert.Null(RecoveryManualAssistance.CaptureTargets(doc with { Capture = doc.Capture! with { ManualSchemaVersion = 1 } }));
        var ambiguous = doc with { Cells = doc.Cells.Select(c => c.Id == cell.Id ? c with { RoleScopes = c.RoleScopes.Append(c.RoleScopes[0]).ToArray() } : c).ToArray() };
        Assert.Null(RecoveryManualAssistance.CaptureTargets(ambiguous, [target]));
        var labelId = cell.RoleScopes.Single(s => s.Role == RecoveryFieldRole.Room).LabelSourceIds[0];
        var uncertainLabelSources = doc.Sources.Select(s => s.Id == labelId ? s with { NativeConfidence = .4 } : s).ToArray();
        var uncertainLabel = doc with { Sources = uncertainLabelSources,
            Capture = doc.Capture! with { SourceSnapshot = RecoveryValidator.Fingerprint(uncertainLabelSources) } };
        uncertainLabel = RecoveryManualReviewBudgetTests.CaptureRequested(uncertainLabel, [target]);
        var labelPlan = Assert.IsType<RecoveryManualPlan>(RecoveryManualAssistance.Prepare(uncertainLabel, [target]));
        var labelTarget = Assert.Single(labelPlan.Targets);
        Assert.Equal(new[] { labelId }, labelTarget.OriginalLabelIds);
        Assert.True(labelTarget.Crop.Contains(uncertainLabel.Sources.Single(s => s.Id == labelId).Box));
        var labelReview = RecoveryManualAssistanceTests.Correction(labelPlan, labelTarget, labelTarget.OriginalOcr) with { ManualSchemaVersion = 2 };
        var unchanged = RecoveryManualAssistance.Complete(labelPlan, [labelReview], "host-control");
        Assert.True(RecoveryValidator.Validate(uncertainLabel, unchanged).CanAdopt);
        Assert.Equal(.4, uncertainLabel.Sources.Single(s => s.Id == labelId).NativeConfidence);
        Assert.Equal(labelTarget.OriginalOcr, unchanged.Cells.Single(c => c.CellId == cell.Id).Lessons[0].Room.Value);
        Assert.Throws<InvalidRecoveryOutputException>(() => RecoveryManualAssistance.Complete(labelPlan,
            [labelReview with { OriginalLabelIds = null }], "host-control"));
        Assert.Throws<InvalidRecoveryOutputException>(() => RecoveryManualAssistance.Complete(labelPlan,
            [labelReview with { OriginalLabelIds = ids.ToArray() }], "host-control"));
        var wrongLabels = uncertainLabel.Sources.Select(s => s.Id == labelId ? s with { Text = "項目不明" } : s).ToArray();
        Assert.Null(RecoveryManualAssistance.CaptureTargets(uncertainLabel with { Sources = wrongLabels,
            Capture = uncertainLabel.Capture! with { SourceSnapshot = RecoveryValidator.Fingerprint(wrongLabels) } }, [target]));
    }

    [Fact]
    public void ShiftedParallelColumnsStillNeedIndependentCorrespondingAnchors()
    {
        var page = Layout(MaterialKind.Timetable, parallel: true);
        Assert.Throws<InvalidDataException>(() => RecoveryDocumentBuilder.Build(new string('a', 64),
            MaterialKind.Timetable, [page], (_, box) => InkFree(page, box)));
    }

    [Fact]
    public void SingleRoleLabelsOnTheSameRowCannotUseTheSeparateRowProof()
    {
        var page = Layout(MaterialKind.Timetable, overlapRows: true);
        Assert.Throws<InvalidDataException>(() => RecoveryDocumentBuilder.Build(new string('a', 64),
            MaterialKind.Timetable, [page], (_, box) => InkFree(page, box)));
    }

    private static PdfPageLayout Layout(MaterialKind kind, bool emptyTeacher = false, bool parallel = false, bool overlapRows = false)
    {
        var original = RecoveryPipelineTests.Layout(kind);
        var glyphs = original.Glyphs.Where(g => g.Y < 80 || g.X < 70 || g.Y >= 140).ToList();
        for (var role = 0; role < 3; role++)
        {
            var y = overlapRows && role == 1 ? 85 : 85 + role * 20;
            var label = new[] { "科目", "教員", "教室" }[role];
            var value = new[] { "科甲", "教甲", "室甲" }[role];
            glyphs.Add(new(label, 74 + role * 2.5, y, 4, 8));
            if (!emptyTeacher || role != 1) glyphs.Add(new(value, 91 + role * 2.5, y, 6, 8));
            if (parallel)
            {
                glyphs.Add(new(label, 120 + role * 2.5, y, 4, 8));
                glyphs.Add(new(new[] { "科乙", "教乙", "室乙" }[role], 135 + role * 2.5, y, 6, 8));
            }
        }
        return original with { Glyphs = glyphs };
    }

    private static bool InkFree(PdfPageLayout page, RecoveryBox box) =>
        !page.Glyphs.Any(g => box.Contains(new(g.X, g.Y, g.Width, g.Height)));
}
