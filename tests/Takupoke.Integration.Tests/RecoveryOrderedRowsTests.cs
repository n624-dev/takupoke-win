using System.Text.Json;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Takupoke.Infrastructure.Storage;
using Xunit;
namespace Takupoke.Integration.Tests;
public sealed class RecoveryOrderedRowsTests
{
    private const string Hash="bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private static PdfPageLayout[] Pages()
    {
        var classes=RecoveryValidator.SpecialClasses.Reverse().ToArray();
        return Enumerable.Range(1,5).Select(day=>
        {
            var glyphs=new List<PdfGlyph>();var rules=new List<PdfRule>();var order=0;
            void Put(string text,double x,double y,int line)
            {foreach(var (character,index) in text.Select((c,i)=>(c,i)))glyphs.Add(new(character.ToString(),x+index*5,y,5,9,line,order++));}
            Put("2027年度 前期",10,10,0);Put(new[]{"月","火","水","木","金"}[day-1],500,40,1);
            rules.AddRange([new(100,35,980,35),new(100,35,100,60),new(980,35,980,60)]);
            foreach(var y in new[]{60d,90d}.Concat(Enumerable.Range(1,17).Select(n=>90d+n*90)))rules.Add(new(0,y,980,y));
            rules.Add(new(0,60,0,1620));foreach(var column in Enumerable.Range(0,9)){var x=100+column*110;rules.Add(new(x,60,x,1620));}
            foreach(var period in Enumerable.Range(1,8))Put(period.ToString(),100+(period-1)*110+52,68,2);
            for(var row=0;row<17;row++)
            {
                var top=90+row*90;Put(classes[row],12,top+40,10+row*4);
                foreach(var period in Enumerable.Range(1,8))
                {var x=106+(period-1)*110;var suffix=$"{day}{row}{period}";Put("架空検証甲"+suffix,x,top+12,11+row*4);Put("架空担当乙"+suffix,x,top+38,12+row*4);Put("仮室検証丙"+suffix,x,top+64,13+row*4);}
            }
            return new PdfPageLayout(980,1620,glyphs,rules);
        }).ToArray();
    }
    [Fact]
    public async Task All680UnlabelledTuplesReachFormalProjectionWithOriginalGeometry()
    {
        var doc=RecoveryDocumentBuilder.Build(Hash,MaterialKind.Timetable,Pages(),(_,_)=>throw new Exception("No blank proof needed"));
        Assert.Equal(680,doc.Cells.Count);Assert.Equal(680,doc.RequiredSlots.Count);
        Assert.All(doc.Cells,c=>Assert.NotNull(c.OrderedRowProof));Assert.Empty(RecoveryValidator.InputErrors(doc));
        var run=await RecoveryEngine.RunAsync(doc,"windows",10,true,[],_=>null);
        Assert.Equal(RecoveryJobState.AwaitingConfirmation,run.State);
        var result=Assert.IsType<RecoveryResult>(run.Result);Assert.True(RecoveryValidator.Validate(doc,result).CanAdopt);
        var source=new SourceRecord("fictional",MaterialKind.Timetable,"fictional.pdf","fictional","fictional.pdf",Hash,1,DateTimeOffset.UnixEpoch,DateTimeOffset.UnixEpoch,null);
        var formal=RecoveryAnalysisConverter.Convert(source,doc,result,DateTimeOffset.UnixEpoch);
        var normal=Assert.IsType<TimetableAnalysis>(formal.Timetable);Assert.Equal(680,normal.Lessons.Count);
        var classes=RecoveryValidator.SpecialClasses.Reverse().ToArray();
        foreach(var lesson in normal.Lessons)
        {var suffix=$"{lesson.Weekday}{Array.IndexOf(classes,lesson.ClassName)}{lesson.Period}";Assert.Equal("架空検証甲"+suffix,lesson.Names.Subject);Assert.Equal("架空担当乙"+suffix,lesson.Names.Teacher);Assert.Equal("仮室検証丙"+suffix,lesson.Names.Room);}
        var decoded=JsonSerializer.Deserialize<RecoveryDocument>(JsonSerializer.Serialize(doc))!;
        Assert.Equal(RecoveryValidator.Fingerprint(doc),RecoveryValidator.Fingerprint(decoded));
    }
    [Fact]
    public void LiteralNativeHeaderSpaceDoesNotAuthorizeUnknownTextOrOcrWhitespace()
    {
        var doc=RecoveryDocumentBuilder.Build(Hash,MaterialKind.Timetable,Pages(),(_,_)=>false);
        var whitespace=Assert.Single(doc.Sources,s=>s.Page==1 && s.Text==" ");
        Assert.Empty(RecoveryValidator.InputErrors(doc));
        foreach(var changed in new[]{whitespace with {Text="架"},whitespace with {FromOcr=true}})
            Assert.Contains("unclassifiedSource",RecoveryValidator.InputErrors(doc with {Sources=doc.Sources.Select(s=>s.Id==whitespace.Id?changed:s).ToArray()}));
    }
    [Fact]
    public void MissingRowsOpaqueAtomsHorizontalPluralAndIncompleteHeadersCannotUseConvention()
    {
        var original=Pages();
        PdfPageLayout[] Mutate(Func<PdfGlyph,bool> select,Func<PdfGlyph,PdfGlyph?> change)
        {var pages=original.ToArray();pages[0]=pages[0] with {Glyphs=pages[0].Glyphs.Select(g=>select(g)?change(g):g).Where(g=>g is not null).Select(g=>g!).ToArray()};return pages;}
        bool InCell(PdfGlyph g)=>g.X>100 && g.X<210;
        foreach(var bad in new[]{
            Mutate(g=>InCell(g)&&g.SourceLine==12,_=>null),
            Mutate(g=>InCell(g)&&g.SourceLine==11&&g.X==106,g=>g with {Text="・"}),
            Mutate(g=>InCell(g)&&g.SourceLine==12,g=>g with {Y=102}),
            Mutate(g=>InCell(g)&&g.SourceLine is >=11 and <=13&&g.X>=126,g=>g with {X=g.X+20}),
            Mutate(g=>InCell(g)&&g.SourceLine==11,g=>g.X==106?g with {Text="架空検証甲101",Width=45}:null),
            Mutate(g=>InCell(g)&&g.SourceLine==11,g=>g with {SourceOrder=-1}),
            Mutate(_=>true,g=>g with {SourceLine=null,SourceOrder=null}),
            original.Take(4).ToArray()})
            Assert.ThrowsAny<Exception>(()=>RecoveryDocumentBuilder.Build(Hash,MaterialKind.Timetable,bad,(_,_)=>false));
    }
    [Fact]
    public async Task SwappedRolesMalformedProofAndBackdatedReceiptRefuse()
    {
        var doc=RecoveryDocumentBuilder.Build(Hash,MaterialKind.Timetable,Pages(),(_,_)=>false);
        var run=await RecoveryEngine.RunAsync(doc,"windows",10,true,[],_=>null);var result=Assert.IsType<RecoveryResult>(run.Result);
        var cell=doc.Cells[0];var binding=cell.LessonBindings[0];var swapped=new RecoveryLessonBinding(binding.Teacher,binding.Subject,binding.Room);
        var changed=cell with {LessonBindings=[swapped],SourceIds=swapped.Subject.Concat(swapped.Teacher).Concat(swapped.Room).ToArray(),OrderedRowProof=cell.OrderedRowProof! with {SourceIds=swapped.Subject.Concat(swapped.Teacher).Concat(swapped.Room).ToArray(),Rows=[cell.OrderedRowProof!.Rows[1],cell.OrderedRowProof.Rows[0],cell.OrderedRowProof.Rows[2]]}};
        Assert.False(RecoveryValidator.Validate(doc with {Cells=doc.Cells.Select(c=>c.Id==cell.Id?changed:c).ToArray()},result).CanAdopt);
        Assert.Contains("orderedRowEvidence",RecoveryValidator.Validate(doc with {Cells=doc.Cells.Select(c=>c.Id==cell.Id?changed with {OrderedRowProof=null}:c).ToArray()},result).Errors);
        var malformed=cell with {OrderedRowProof=cell.OrderedRowProof! with {Rows=[Enumerable.Repeat(cell.OrderedRowProof!.Rows[0][0],257).ToArray(),cell.OrderedRowProof.Rows[1],cell.OrderedRowProof.Rows[2]]}};
        Assert.False(RecoveryValidator.Validate(doc with {Cells=doc.Cells.Select(c=>c.Id==cell.Id?malformed:c).ToArray()},result).CanAdopt);
        var old=result with {Metadata=result.Metadata with {ValidatorVersion=9}};
        var receipt=new RecoveryAcceptance(Hash,RecoveryValidator.Fingerprint(old),RecoveryValidator.Fingerprint(doc),old.Metadata,DateTimeOffset.UnixEpoch.AddDays(1));
        Assert.False(RecoveryValidator.HistoricalAcceptanceEnvelopeValid(doc,old,receipt,null));
    }
}
