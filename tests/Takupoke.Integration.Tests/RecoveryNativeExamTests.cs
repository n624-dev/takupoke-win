using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Xunit;
namespace Takupoke.Integration.Tests;
public sealed class RecoveryNativeExamTests
{
    private static PdfPageLayout[] Pages(bool lowerRows=false)
    {
        return RecoveryDateHeaderOwnershipTests.ExamPages().Select(p=>p with {
            Glyphs=p.Glyphs.Where(g=>g.Text is not ("AI_1" or "AI_2" or "架空室")).Select(g=> {
                var glyph=lowerRows && g.Text is "架空科" or "架空師" ? g with {Y=g.Y+32} : g;
                return glyph with {InkBounds=new(glyph.X,glyph.Y,glyph.X+glyph.Width,glyph.Y+glyph.Height)};
            }).ToArray()
        }).ToArray();
    }
    [Fact]
    public async Task CompleteNativeExamFamilyKeepsTwoRowsEmptyRoomAndOriginalGradeOnlyHeaders()
    {
        var pages=Pages();
        Assert.Equal("P17",Assert.Throws<PdfParseException>(()=>PdfScheduleParser.Special(pages,MaterialKind.Exam)).Stage);
        var parsed=PdfScheduleParser.Special(pages,MaterialKind.Exam,allowNativeTwoRowExam:true);
        Assert.Equal(17,parsed.CoveredClasses.Count);Assert.All(parsed.Lessons,l=>Assert.Empty(l.Names.Room));
        var doc=RecoveryDocumentBuilder.Build(new string('a',64),MaterialKind.Exam,pages,
            (p,b)=>RecoveryLayoutGeometry.NativeTextFree(pages[p-1],b),allowStructureProposal:true,allowNativeReferences:true);
        doc=doc with {Capture=new(doc.PdfHash,6,RecoveryValidator.Fingerprint(doc.Sources),pages.Select((p,i)=>new RecoveryCapturedPage(i+1,(int)p.Width,(int)p.Height,new string('c',64),doc.Sources.Count(s=>s.Page==i+1),true)).ToArray(),4)};
        Assert.Empty(RecoveryValidator.InputErrors(doc));
        var run=await RecoveryEngine.RunAsync(doc,"control",16,true,[],_=>null);
        Assert.NotNull(run.Result);Assert.True(RecoveryValidator.Validate(doc,run.Result).CanAdopt);
        foreach(var cls in new[]{"AI_1","AI_2"})Assert.Equal(cls[^1]+"年",string.Concat(doc.ClassEvidence[cls].Select(id=>doc.Sources.Single(s=>s.Id==id).Text)));
        var bad=doc with {ClassEvidence=doc.ClassEvidence.Where(p=>p.Key!="1_1").ToDictionary(p=>p.Key,p=>p.Value)};
        Assert.NotEmpty(RecoveryValidator.InputErrors(bad));
        bad=doc with {Capture=doc.Capture with {ManualSchemaVersion=3}};
        Assert.NotEmpty(RecoveryValidator.InputErrors(bad));
    }
    [Fact]
    public void LowerTeacherAndRoomRowsCannotShiftIntoSubjectAndTeacher()
    {
        Assert.Equal("P17",Assert.Throws<PdfParseException>(()=>PdfScheduleParser.Special(Pages(lowerRows:true),MaterialKind.Exam,allowNativeTwoRowExam:true)).Stage);
    }
    [Fact]
    public async Task FullWidthNativeClockFragmentsKeepWholeOriginalAtomsAcrossLargePrintedGaps()
    {
        string Wide(string value)=>string.Concat(value.Select(c=>c is >= '0' and <= '9' ? (char)(c-'0'+'０') : c==':' ? '：' : c));
        var pages=Pages().Select(p=>p with {Glyphs=p.Glyphs.SelectMany((g,index)=> {
            if(g.Y<700 || !(g.Text.Contains('時') || g.Text.Contains('〜')))return new[]{g};
            var text=Wide(g.Text);var clock=g.Text.Contains('〜');var split=text.IndexOf('〜');
            return text.Select((c,n)=> {
                var x=g.X+n*6+(clock && n>=split ? 24 : 0)+(clock && n>split ? 24 : 0);
                return new PdfGlyph(c.ToString(),x,g.Y,6,g.Height,(int)g.Y,10000+index*128+n) {InkBounds=new(x,g.Y,x+6,g.Y+g.Height)};
            }).ToArray();
        }).ToArray()}).ToArray();
        var doc=RecoveryDocumentBuilder.Build(new string('a',64),MaterialKind.Exam,pages,(p,b)=>RecoveryLayoutGeometry.NativeTextFree(pages[p-1],b),allowNativeReferences:true);
        doc=doc with {Capture=new(doc.PdfHash,6,RecoveryValidator.Fingerprint(doc.Sources),pages.Select((p,i)=>new RecoveryCapturedPage(i+1,(int)p.Width,(int)p.Height,new string('c',64),doc.Sources.Count(s=>s.Page==i+1),true)).ToArray(),4)};
        Assert.Equal(30,doc.Times.Count);Assert.Empty(RecoveryValidator.InputErrors(doc));
        var run=await RecoveryEngine.RunAsync(doc,"control",16,true,[],_=>null);
        Assert.NotNull(run.Result);Assert.True(RecoveryValidator.Validate(doc,run.Result).CanAdopt);
        Assert.Contains(doc.Sources,s=>s.Text=="：");
    }
}
