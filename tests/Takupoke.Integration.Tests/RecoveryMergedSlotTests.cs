using System.Globalization;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Takupoke.Infrastructure.Storage;
using Xunit;
namespace Takupoke.Integration.Tests;
public sealed class RecoveryMergedSlotTests
{
    [Fact] public async Task SourceDerivedMergedCellKeepsBothSlotsAndOriginalEvidence()
    {
        var page=Layout(); var strict=PdfScheduleParser.Timetable([page]);
        Assert.Equal(new[]{1,2},strict.Lessons.Select(l=>l.Period));
        Assert.All(strict.Lessons,l=>Assert.Equal(new LessonNames("架空科A","架空師B","架空室C"),l.Names));
        var doc=RecoveryDocumentBuilder.Build(new string('a',64),MaterialKind.Timetable,[page],(_,b)=>!page.Glyphs.Any(g=>b.Contains(new(g.X,g.Y,g.Width,g.Height))));
        var cell=Assert.Single(doc.Cells,c=>c.SourceIds.Count>0);
        Assert.Equal(new[]{1,2},cell.Slots.Select(s=>s.Period));
        Assert.Equal(RecoveryBindingMode.Fixed,cell.BindingMode);
        var binding=Assert.Single(cell.LessonBindings);
        Assert.Equal(new[]{"p1s50"},binding.Subject);
        Assert.Equal(new[]{"p1s51"},binding.Teacher);
        Assert.Equal(new[]{"p1s52"},binding.Room);
        Assert.Empty(RecoveryValidator.InputErrors(doc));
        var run=await RecoveryEngine.RunAsync(doc,"windows",10,true,[],_=>null);
        Assert.Equal(RecoveryJobState.AwaitingConfirmation,run.State);Assert.NotNull(run.Result);
        var now=DateTimeOffset.UtcNow;var source=new SourceRecord("fake",MaterialKind.Timetable,"fake.pdf","fake.pdf","invented",doc.PdfHash,1,now,now,null);
        var formal=RecoveryAnalysisConverter.Convert(source,doc,run.Result,now).Timetable!;
        Assert.Equal(strict,formal with { Lessons=strict.Lessons });
        Assert.Equal(strict.Lessons.Select(l=>(l.ClassName,l.Weekday,l.Period,l.Names)),formal.Lessons.Select(l=>(l.ClassName,l.Weekday,l.Period,l.Names)));

    }
    [Fact] public void TrustRequiresExactlyOneEqualLessonAtEverySameClassAndDaySlot()
    {
        var names=new LessonNames("架空科A","架空師B","架空室C");
        var lessons=new[]{new NormalLesson("1_2",1,1,names,"原文",1),new NormalLesson("1_2",1,2,names,"原文",1)};
        LessonNames? Trust(IReadOnlyList<NormalLesson> rows, params RecoverySlot[] slots)=>RecoveryDocumentBuilder.TrustedNormalNames(new(2032,"前期",rows),slots,_=>{});
        var one=new RecoverySlot("1_2","1",1);var two=new RecoverySlot("1_2","1",2);
        Assert.Equal(names,Trust(lessons,one,two));Assert.Equal(names,Trust(lessons,two,one));Assert.Equal(names,Trust(lessons,one));
        Assert.Null(Trust(lessons));Assert.Null(Trust(lessons,one,one));
        Assert.Null(Trust([lessons[0]],one,two));
        Assert.Null(Trust([lessons[0],lessons[1] with { Names=names with { Teacher="別師" } }],one,two));
        Assert.Null(Trust([lessons[0],lessons[1],lessons[0]],one,two));
        Assert.Null(Trust([lessons[0],lessons[1] with { Weekday=2 }],one,two));
        Assert.Null(Trust([lessons[0],lessons[1] with { ClassName="1_1" }],one,two));
        Assert.Null(Trust(lessons,one,two with { Day="2" }));Assert.Null(Trust(lessons,one,two with { ClassName="1_1" }));
    }
    [Fact] public void TrustChargesEveryActualScanAndPropagatesSharedCancellationAndBudgetFailure()
    {
        var names=new LessonNames("架空科A");var normal=new TimetableAnalysis(2032,"前期",[new("1_2",1,1,names,"原文",1),new("1_2",1,2,names,"原文",1)]);
        RecoverySlot[] slots=[new("1_2","1",1),new("1_2","1",2)];long total=0;
        Assert.Equal(names,RecoveryDocumentBuilder.TrustedNormalNames(normal,slots,n=>total+=n));Assert.Equal(8,total);
        Assert.Throws<OperationCanceledException>(()=>RecoveryDocumentBuilder.TrustedNormalNames(normal,slots,_=>throw new OperationCanceledException()));
        Assert.Throws<InvalidDataException>(()=>RecoveryDocumentBuilder.TrustedNormalNames(normal,slots,_=>throw new InvalidDataException("shared budget exceeded")));
    }
    private static PdfPageLayout Layout()
    {
        var glyphs=new List<PdfGlyph>{new("令和14年度",98,13,104,26),new("前期",350,13,40,26),new("時間割",550,13,60,26),new("1",34,171,12,26),new("2",84,171,12,26)};
        var days=new[]{"月曜日","火曜日","水曜日","木曜日","金曜日"};
        for(var d=0;d<5;d++){glyphs.Add(new(days[d],450+d*720,74,60,26));for(var p=0;p<8;p++)glyphs.Add(new((p+1).ToString(CultureInfo.InvariantCulture),159+(d*8+p)*90,107,12,26));}
        glyphs.AddRange([new("架空科A",133,139,64,26),new("架空師B",133,171,64,26),new("架空室C",133,203,64,26)]);
        var rules=new List<PdfRule>{new(20,70,3720,70),new(20,136,3720,136),new(20,232,3720,232),new(120,104,3720,104),new(20,70,20,232),new(60,70,60,232),new(120,70,120,232)};
        for(var i=1;i<=40;i++)rules.Add(new(120+i*90,i%8==0?70:104,120+i*90,i==1?136:232));
        return new(3740,800,glyphs,rules);
    }
}
