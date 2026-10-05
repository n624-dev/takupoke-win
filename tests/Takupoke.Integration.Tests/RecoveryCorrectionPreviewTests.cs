using System.Text.Json;
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
}
