using System.Text.Json;
using System.Collections.Immutable;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Recovery;
using Takupoke.Infrastructure.Storage;
using Takupoke.Win.ViewModels;
using Xunit;

namespace Takupoke.Integration.Tests;
public sealed class RecoveryCorrectionPreviewTests
{
    private static RecoveryPreview Preview(int count = 1, string mode = "wholeformal", string prefix = "架空入力")
    {
        var doc = RecoveryManualAssistanceTests.Captured(count, mode);
        var plan = Assert.IsType<RecoveryManualPlan>(RecoveryManualAssistance.Prepare(doc));
        var result = RecoveryManualAssistance.Complete(plan, plan.Targets.Select((t,i) => RecoveryManualAssistanceTests.Correction(plan,t,prefix+i)).ToArray(),"fictional-host");
        return new("fictional-source",new(1,new(2032,1)),doc,result,DateTimeOffset.Parse("2032-04-01T00:00:00Z"));
    }
    private static MaterialAnalysis Formal(RecoveryPreview p)
    {
        var source = new SourceRecord(p.SourceId,MaterialKind.Timetable,"fictional.pdf","fictional","fictional",p.Document.PdfHash,0,p.CreatedAt,p.CreatedAt,null);
        var formal = RecoveryAnalysisConverter.Convert(source,p.Document,p.Result,p.CreatedAt);
        return formal with { Recovery = new(p.Document,p.Result,new(p.Document.PdfHash,RecoveryValidator.Fingerprint(p.Result),RecoveryValidator.Fingerprint(p.Document),p.Result.Metadata,p.CreatedAt)) };
    }
    [Theory][InlineData(1)][InlineData(3)]
    public void CorrectionsMapOriginalUserProvenanceAndEveryOccupiedSlotWithoutChangingReceipt(int count)
    {
        var p=Preview(count);var bytes=JsonSerializer.SerializeToUtf8Bytes(p);
        var display=Assert.IsType<RecoveryPreviewDisplay>(RecoveryPreviewDisplay.Create(p,CancellationToken.None));
        Assert.Equal(count,display.Corrections.Length);Assert.Equal(p.Document.Classes,display.Classes);Assert.Equal(80,display.Cells.Sum(c=>c.Slots.Length));
        var plan=RecoveryManualAssistance.Prepare(p.Document)!;
        foreach(var correction in display.Corrections)
        {
            var target=plan.Targets.Single(t=>t.Target.CellId==correction.CellId&&t.Target.LessonIndex==correction.LessonIndex&&t.Target.Role==correction.Role);
            Assert.Equal(target.OriginalOcr,correction.OriginalOcr);Assert.Equal("user",correction.Provenance);
            Assert.Equal(p.Result.HumanCorrections!.Single(c=>c.Target==target.Target).CorrectedText,correction.UserText);
            Assert.Equal(p.Document.Cells.Single(c=>c.Id==correction.CellId).Slots,correction.Slots);
        }
        Assert.Equal(bytes,JsonSerializer.SerializeToUtf8Bytes(p));
    }
    [Fact]
    public void MergedParallelCorrectionsNeverHighlightNeighborLesson()
    {
        var p=Preview(3,"parallelMerged");var display=RecoveryPreviewDisplay.Create(p,CancellationToken.None)!;
        var cell=display.Cells.Single(c=>c.CellId==display.Corrections[0].CellId);
        Assert.Equal(new[]{1,2},cell.Slots.Select(s=>s.Period));Assert.Equal(2,cell.Lessons.Length);
        Assert.All(display.Corrections,c=>Assert.Equal(0,c.LessonIndex));
        Assert.Equal("架空科D",cell.Lessons[1].Subject);Assert.DoesNotContain(display.Corrections,c=>c.CellId==cell.CellId&&c.LessonIndex==1);
    }
    [Fact]
    public void DisplayCorrectionsOwnTheirValuesAfterOriginalParentListMutates()
    {
        var p=Preview();var correction=p.Result.HumanCorrections![0];var ids=correction.OriginalParentIds.ToArray();
        p=p with {Result=p.Result with {HumanCorrections=[correction with {OriginalParentIds=ids}]}};
        var display=RecoveryPreviewDisplay.Create(p,CancellationToken.None)!;var raw=display.Corrections[0].OriginalOcr;
        ids[0]="fictional-mutated-parent";Assert.Equal(raw,display.Corrections[0].OriginalOcr);
    }
    [Fact]
    public void OnlyActualValidatedIdenticalOriginalFormalProjectionAllowsDiff()
    {
        var old=Preview(prefix:"架空以前入力");var current=Preview(prefix:"架空今回入力");var formal=Formal(old);
        Assert.True(RecoveryAnalysisConverter.MayDisplay(formal));
        var display=RecoveryPreviewDisplay.Create(current,CancellationToken.None,formal)!;
        Assert.True(display.PreviousComparable);var change=Assert.Single(display.PreviousChanges);
        Assert.Equal("架空以前入力0",change.PreviousText);Assert.Equal("架空今回入力0",change.CurrentText);
        Assert.Equal(current.Result.HumanCorrections![0].Target.Role,change.Role);
    }
    [Fact]
    public void SameVerifiedFormalResultHasNoInventedDiff()
    {
        var p=Preview();var display=RecoveryPreviewDisplay.Create(p,CancellationToken.None,Formal(p))!;
        Assert.True(display.PreviousComparable);Assert.Empty(display.PreviousChanges);
    }
    [Fact]
    public void OrdinaryProjectionUsesOriginalClassKeysAfterDepartmentMigration()
    {
        var p = Preview(); var formal = Formal(p);
        var sourceClass = formal.Timetable!.Lessons[0].SourceClassName ?? formal.Timetable.Lessons[0].ClassName;
        // A proven ordinary result explicitly records its parsing route. Its
        // projected class can differ while the PDF cell key remains unchanged.
        var ordinary = formal with { Recovery = null, OrdinaryPdf = true,
            Timetable = formal.Timetable! with { Lessons = formal.Timetable.Lessons.Select(l => l with
                { ClassName = "1_CN", SourceClassName = l.SourceClassName ?? l.ClassName }).ToArray() } };
        var display = RecoveryPreviewDisplay.Create(p, CancellationToken.None, ordinary)!;
        Assert.True(display.PreviousComparable); Assert.Empty(display.PreviousChanges);
        var changed = ordinary with { Timetable = ordinary.Timetable with { Lessons = ordinary.Timetable.Lessons.Select((l,i) =>
            i == 0 ? l with { Names = l.Names with { Subject = "架空以前の科目" } } : l).ToArray() } };
        Assert.Contains(RecoveryPreviewDisplay.Create(p,CancellationToken.None,changed)!.PreviousChanges,
            change => change.PreviousText == "架空以前の科目" && change.Slots.Any(slot => slot.ClassName == sourceClass));
    }
    [Theory][InlineData("missingAudit")][InlineData("differentOriginal")][InlineData("differentDigest")][InlineData("tamperedProjection")][InlineData("tamperedAudit")]
    public void UnmatchedOrUnverifiedPreviousDataNeverGuessesAlignment(string fault)
    {
        var p=Preview();var old=Formal(p);old=fault switch {
            "missingAudit"=>old with {Recovery=null}, "differentOriginal"=>old with {OriginalId="fictional-other"},
            "differentDigest"=>old with {SourceDigest=new string('b',64)},
            "tamperedProjection"=>old with {SchoolYear=2033},
            _=>old with {Recovery=old.Recovery! with {Acceptance=old.Recovery.Acceptance with {ResultHash=new string('c',64)}}}
        };
        var display=RecoveryPreviewDisplay.Create(p,CancellationToken.None,old)!;
        Assert.False(display.PreviousComparable);Assert.Empty(display.PreviousChanges);
    }
    [Fact]
    public void NewSourceEvenWithIdenticalValuesHasNoGuessedPreviousMapping()
    {
        var old=Preview();var current=Preview(prefix:"架空今回入力");
        current=current with {SourceId="fictional-new-source"};
        Assert.False(RecoveryPreviewDisplay.Create(current,CancellationToken.None,Formal(old))!.PreviousComparable);
    }
    [Theory][InlineData(0,false)][InlineData(320,false)][InlineData(679.9,false)][InlineData(680,true)][InlineData(1000,true)][InlineData(double.NaN,false)][InlineData(double.PositiveInfinity,false)]
    public void ResponsiveChoiceHasFiniteBoundaryWithoutInputStateChanges(double width,bool expected)=>Assert.Equal(expected,RecoveryCorrectionLayout.SideBySide(width));
    [Fact]
    public void CancellationAndInvalidCurrentHashCannotProduceDisplay()
    {
        var p=Preview();Assert.Null(RecoveryPreviewDisplay.Create(p with {Result=p.Result with {PdfHash=new string('b',64)}},CancellationToken.None));
        using var c=new CancellationTokenSource();c.Cancel();Assert.Throws<OperationCanceledException>(()=>RecoveryPreviewDisplay.Create(p,c.Token));
    }

    [Fact]
    public void ConfidentCodeAdviceIsDisplayOnlyAndDisappearsAfterActualSourceOwnedReview()
    {
        var doc = RecoveryManualAssistanceTests.Captured(0);
        var cell = doc.Cells.First(c => c.LessonBindings.Count > 0);
        var target = new RecoveryFieldTarget(cell.Id, 0, RecoveryFieldRole.Room);
        var roomId = Assert.Single(cell.LessonBindings[0].Room);
        var sources = doc.Sources.Select(s => s.Id == roomId ? s with { Text = "架空室I1O0" } : s).ToArray();
        doc = doc with { Sources = sources, Capture = doc.Capture! with { SourceSnapshot = RecoveryValidator.Fingerprint(sources) } };
        doc = RecoveryManualReviewBudgetTests.CaptureRequested(doc, [target], 3);
        var plan = Assert.IsType<RecoveryManualPlan>(RecoveryManualAssistance.Prepare(doc, [target]));
        var review = RecoveryManualAssistanceTests.Correction(plan, Assert.Single(plan.Targets), plan.Targets[0].OriginalOcr) with { ManualSchemaVersion = 3 };
        var confirmed = RecoveryManualAssistance.Complete(plan, [review], "fictional-host");
        var raw = confirmed with { HumanCorrections = null };
        Assert.True(RecoveryValidator.Validate(doc, raw).CanAdopt);
        var preview = new RecoveryPreview("fictional-source", new(1,new(2032,1)), doc, raw, DateTimeOffset.UtcNow);
        var before = JsonSerializer.SerializeToUtf8Bytes(preview);
        var display = Assert.IsType<RecoveryPreviewDisplay>(RecoveryPreviewDisplay.Create(preview,CancellationToken.None));
        Assert.Contains(RecoveryFieldRole.Room, display.Cells.Single(c => c.CellId == cell.Id).Lessons[0].CodeReviewRoles);
        Assert.All(plan.Targets[0].OriginalParentIds, id => Assert.Equal(.95, doc.Sources.Single(s => s.Id == id).NativeConfidence));
        Assert.Equal(before, JsonSerializer.SerializeToUtf8Bytes(preview));
        display = Assert.IsType<RecoveryPreviewDisplay>(RecoveryPreviewDisplay.Create(preview with { Result = confirmed },CancellationToken.None));
        Assert.DoesNotContain(RecoveryFieldRole.Room, display.Cells.Single(c => c.CellId == cell.Id).Lessons[0].CodeReviewRoles);
        Assert.Empty(display.Corrections); Assert.Equal(1,display.ReviewedFieldCount);
    }

    [Theory]
    [InlineData("架空室I1lO0",true)][InlineData("架空室A301",true)]
    [InlineData("架空室A237",false)][InlineData("架空室101",false)][InlineData("架空教員",false)][InlineData("BIOLOGY",false)]
    public void CodeAdviceSpellsCharacterTypesWithoutNormalizingOrInventingWarnings(string value, bool expected)
    {
        var description = RecoveryCodeReview.Describe(value);
        Assert.Equal(expected, description is not null);
        if (value == "架空室I1lO0")
            foreach (var label in new[] { "I（大文字アイ）", "l（小文字エル）", "1（数字のいち）", "O（大文字オー）", "0（数字のゼロ）" })
                Assert.Contains(label, description);
    }

    [Fact]
    public void RoomSelectionDeduplicatesMergedSlotsKeepsOtherClassesAndFailsAtomicallyAtBudget()
    {
        var display = RecoveryPreviewDisplay.Create(Preview(3,"parallelMerged"),CancellationToken.None)!;
        var cls = display.Classes[0];
        var room = new RecoveryDisplayLesson("架空科", "架空員", false, "架空室A201", false) { EditableRoles = [RecoveryFieldRole.Room] };
        var other = new RecoveryFieldTarget("other-class",0,RecoveryFieldRole.Teacher);
        var selected = new HashSet<RecoveryFieldTarget> { other };
        display = display with { Cells = [
            new([new(cls,"1",1), new(cls,"1",2)],RecoveryValueState.Present,[room,room]) { CellId="merged" },
            new([new("other","1",1)],RecoveryValueState.Present,[room]) { CellId="other" },
            new([new(cls,"1",3)],RecoveryValueState.Present,[room with { EditableRoles=[] }]) { CellId="vector" }] };
        Assert.True(display.SelectRooms(cls,selected));
        Assert.Equal(3,selected.Count); Assert.Contains(other,selected);
        Assert.Equal(new[] { 0,1 }, display.RoomTargets(cls).Select(t => t.LessonIndex));
        Assert.DoesNotContain(selected,t => t.CellId is "other" or "vector");
        Assert.True(display.SelectRooms(cls,selected)); Assert.Equal(3,selected.Count);
        selected = Enumerable.Range(0,127).Select(i => new RecoveryFieldTarget("prior"+i,0,RecoveryFieldRole.Subject)).ToHashSet();
        var before = selected.ToHashSet();
        Assert.False(display.SelectRooms(cls,selected)); Assert.True(before.SetEquals(selected));
        Assert.False(display.SelectRooms("unknown",selected)); Assert.True(before.SetEquals(selected));
        display = display with { ReviewedTargets = before.Append(new("already-reviewed",0,RecoveryFieldRole.Room)).ToImmutableArray() };
        selected.Clear();
        Assert.False(display.SelectRooms(cls,selected)); Assert.Empty(selected);
        Assert.True(display.SelectionFits(display.ReviewedTargets));
        Assert.False(display.SelectionFits([new("new-field",0,RecoveryFieldRole.Room)]));
    }
}
