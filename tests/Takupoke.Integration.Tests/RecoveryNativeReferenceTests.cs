using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Xunit;

namespace Takupoke.Integration.Tests;
public sealed class RecoveryNativeReferenceTests
{
    internal static RecoveryDocument Capture(RecoveryDocument doc,int width=4090,int height=3000) => doc with {
        Capture=new(doc.PdfHash,1,RecoveryValidator.Fingerprint(doc.Sources),[new(1,width,height,new string('c',64),doc.Sources.Count,true)],4) };
    private static RecoveryDocument WithNote()
    {
        var page=RecoveryPipelineTests.Layout(MaterialKind.Timetable);
        page=page with {Glyphs=page.Glyphs.Append(new("架空の教室注記",10,200,60,8,777,777)).ToArray()};
        return Capture(RecoveryDocumentBuilder.Build(new string('a',64),MaterialKind.Timetable,[page],(_,_)=>true,allowNativeReferences:true));
    }
    [Fact]
    public async Task OutsideNativeNotesRemainOriginalSourcesAndCannotSupplyBodyRoles()
    {
        var doc=WithNote();var note=Assert.Single(doc.ReferenceNotes!);
        Assert.Equal("架空の教室注記",string.Concat(note.SourceIds.Select(id=>doc.Sources.Single(s=>s.Id==id).Text)));
        Assert.Empty(RecoveryValidator.InputErrors(doc));
        var run=await RecoveryEngine.RunAsync(doc,"control",16,true,[],_=>null);
        Assert.NotNull(run.Result);Assert.True(RecoveryValidator.Validate(doc,run.Result).CanAdopt);
        var legacy=doc with {Capture=doc.Capture! with {ManualSchemaVersion=3}};
        Assert.Contains("nativeCapture",RecoveryValidator.InputErrors(legacy));
        var body=doc.Cells.First(c=>c.SourceIds.Count>0);
        var forged=doc with {ReferenceNotes=[new(1,body.Box,body.SourceIds)]};
        Assert.Contains("referenceRegion",RecoveryValidator.InputErrors(forged));
        Assert.Contains("referenceSources",RecoveryValidator.InputErrors(forged));
        var ocr=doc with {Sources=doc.Sources.Select(s=>note.SourceIds.Contains(s.Id) ? s with {FromOcr=true,NativeConfidence=.95} : s).ToArray()};
        ocr=ocr with {Capture=ocr.Capture! with {SourceSnapshot=RecoveryValidator.Fingerprint(ocr.Sources)}};
        Assert.Contains("referenceSources",RecoveryValidator.InputErrors(ocr));
    }
    [Fact]
    public void ProjectionMovesAllOriginalEvidenceTogetherAndKeepsNativeTextAndRoles()
    {
        var doc=WithNote() with {Capture=null};
        var projected=RecoveryLayoutGeometry.Project(doc,[(.5,.5)]);
        projected=Capture(projected,2045,1500);
        Assert.Empty(RecoveryValidator.InputErrors(projected));
        Assert.Equal(doc.Sources.Select(s=>s.Text),projected.Sources.Select(s=>s.Text));
        Assert.Equal(doc.Cells.Select(c=>c.LessonBindings.Count),projected.Cells.Select(c=>c.LessonBindings.Count));
        Assert.Equal(100,Assert.Single(projected.ReferenceNotes!).Box.Y);
        Assert.Throws<ArgumentException>(()=>RecoveryLayoutGeometry.Project(projected,[(1,1)]));
    }
    [Fact]
    public void NativeRoleProofRequiresItsOriginalPdfCurrentCaptureAndVerticalRoleOrder()
    {
        var doc=RecoveryManualAssistanceTests.Captured(0);
        var cell=doc.Cells.First(c=>c.LessonBindings.Count>0);
        var sources=doc.Sources.Select(s=>s with {FromOcr=false,NativeConfidence=null}).ToArray();
        doc=doc with {Sources=sources,Cells=doc.Cells.Select(c=>c.Id==cell.Id ? c with {NativeLayoutProof=new(1,doc.PdfHash)} : c).ToArray(),
            Capture=doc.Capture! with {ManualSchemaVersion=4,SourceSnapshot=RecoveryValidator.Fingerprint(sources)}};
        Assert.Empty(RecoveryValidator.InputErrors(doc));
        var proofCell=doc.Cells.Single(c=>c.Id==cell.Id);
        var bad=doc with {Cells=doc.Cells.Select(c=>c.Id==cell.Id ? c with {NativeLayoutProof=new(1,new string('b',64))} : c).ToArray()};
        Assert.Contains("nativeLayoutProof",RecoveryValidator.InputErrors(bad));
        var binding=proofCell.LessonBindings[0];
        bad=doc with {Cells=doc.Cells.Select(c=>c.Id==cell.Id ? c with {LessonBindings=[binding with {Subject=binding.Room,Room=binding.Subject}]} : c).ToArray()};
        Assert.Contains("nativeRoleOrder",RecoveryValidator.InputErrors(bad));
    }
    [Fact]
    public async Task NativeInkSupportsKeepTheirOriginalBaselineWithoutGivingOcrNewGeometryAuthority()
    {
        var doc=RecoveryParallelProofTests.Build(RecoveryParallelProofTests.TwoPages("parallel"));
        var cell=doc.Cells.Single(c=>c.ParallelSeparators is not null);var id=cell.LessonBindings[0].Teacher[0];
        var sources=doc.Sources.Select(s=>s with {NativeLayoutBox=s.Box,Box=s.Id==id ? s.Box with {Height=1} : s.Box}).ToArray();
        doc=doc with {Sources=sources,Capture=new(doc.PdfHash,2,RecoveryValidator.Fingerprint(sources),Enumerable.Range(1,2).Select(p=>new RecoveryCapturedPage(p,3740,800,new string('c',64),sources.Count(s=>s.Page==p),true)).ToArray(),4)};
        Assert.Empty(RecoveryValidator.InputErrors(doc));
        var run=await RecoveryEngine.RunAsync(doc,"control",16,true,[],_=>null);
        Assert.NotNull(run.Result);Assert.True(RecoveryValidator.Validate(doc,run.Result).CanAdopt);
        var bad=doc with {Sources=sources.Select(s=>s.Id==id ? s with {FromOcr=true,NativeConfidence=.95} : s).ToArray()};
        bad=bad with {Capture=bad.Capture! with {SourceSnapshot=RecoveryValidator.Fingerprint(bad.Sources)}};
        Assert.Contains("nativeLayoutSource",RecoveryValidator.InputErrors(bad));
    }
    [Fact]
    public void NativeBlankCheckObservesCancellationBeforeAnEarlyInkMatch()
    {
        var page=RecoveryPipelineTests.Layout(MaterialKind.Timetable);
        using var stop=new CancellationTokenSource();stop.Cancel();
        var scan=RecoveryLayoutGeometry.NativeBlankScanner(page,stop.Token);
        Assert.Throws<OperationCanceledException>(()=>scan(new(0,0,page.Width,page.Height)));
    }
    [Fact]
    public async Task TwoPhysicallySplitNativeLessonsKeepOneSlotAndTwoOriginalTuples()
    {
        var page=RecoveryDateHeaderOwnershipTests.ReturnPage();const int top=194+8*96;
        var added=new List<PdfGlyph>();
        for(var lesson=0;lesson<2;lesson++)for(var role=0;role<3;role++)
            added.Add(new(new[]{"架空分割科目","架空分割教員","架空分割教室"}[role]+lesson,144,top+5+lesson*48+role*12,60,8));
        page=page with {Glyphs=page.Glyphs.Where(g=>!(g.X==144 && g.Y>=top && g.Y<top+96)).Concat(added).ToArray(),
            Lines=page.Lines.Append(new(120,top+48,218,top+48)).ToArray()};
        var doc=Capture(RecoveryDocumentBuilder.Build(new string('a',64),MaterialKind.ExamReturn,[page],(_,_)=>true,allowNativeReferences:true));
        Assert.Empty(RecoveryValidator.InputErrors(doc));
        var cell=Assert.Single(doc.Cells,c=>c.ParallelCount==2);Assert.Single(cell.Slots);
        Assert.Equal(2,cell.LessonBindings.Count);Assert.Equal(cell.SourceIds.Count,cell.LessonBindings.SelectMany(b=>b.Subject.Concat(b.Teacher).Concat(b.Room)).Distinct().Count());
        var run=await RecoveryEngine.RunAsync(doc,"control",16,true,[],_=>null);
        Assert.NotNull(run.Result);Assert.True(RecoveryValidator.Validate(doc,run.Result).CanAdopt);
        Assert.Equal(new[]{"架空分割科目0","架空分割科目1"},run.Result.Cells.Single(c=>c.CellId==cell.Id).Lessons.Select(l=>l.Subject.Value));
    }
    [Fact]
    public void PartiallyClippedNativeReferenceNeedsReadOnlyOriginalPixelAcknowledgement()
    {
        var doc=WithNote();var ids=doc.ReferenceNotes![0].SourceIds.ToHashSet();
        var sources=doc.Sources.Select(s=>ids.Contains(s.Id) ? s with {RequiresVisualReview=true} : s).ToArray();
        doc=doc with {Sources=sources,Capture=doc.Capture! with {SourceSnapshot=RecoveryValidator.Fingerprint(sources)}};
        doc=RecoveryManualReviewBudgetTests.CaptureRequested(doc,manualSchemaVersion:4);
        var plan=Assert.IsType<RecoveryManualPlan>(RecoveryManualAssistance.Prepare(doc));
        var target=Assert.Single(plan.Targets);Assert.Equal(RecoveryHeaderReviewRole.Reference,target.HeaderProof!.Role);
        var review=RecoveryManualAssistanceTests.Correction(plan,target,target.OriginalOcr) with {ManualSchemaVersion=4};
        var result=RecoveryManualAssistance.Complete(plan,[review],"control");
        Assert.True(RecoveryValidator.Validate(doc,result).CanAdopt);
        Assert.False(RecoveryValidator.Validate(doc,result with {HumanCorrections=null}).CanAdopt);
        Assert.Throws<InvalidRecoveryOutputException>(()=>RecoveryManualAssistance.Complete(plan,[review with {CorrectedText="書き換え不可"}],"control"));
    }
    [Theory]
    [InlineData("snapshot")][InlineData("coverage")][InlineData("pageHash")][InlineData("sourceCount")]
    public void NativeProofWithoutHumanCorrectionsStillRequiresItsOriginalCaptureReceipt(string mutation)
    {
        var doc=WithNote();var capture=doc.Capture!;
        capture=mutation switch {
            "snapshot"=>capture with {SourceSnapshot=new string('0',64)},
            "coverage"=>capture with {Pages=capture.Pages.Select(p=>p with {InkCoverageVerified=false}).ToArray()},
            "pageHash"=>capture with {Pages=capture.Pages.Select(p=>p with {RasterHash="invalid"}).ToArray()},
            _=>capture with {Pages=capture.Pages.Select(p=>p with {SourceCount=p.SourceCount+1}).ToArray()}
        };
        Assert.Contains("nativeCapture",RecoveryValidator.InputErrors(doc with {Capture=capture}));
    }
}
