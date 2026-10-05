using System.Globalization;
using System.Text.Json;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Takupoke.Infrastructure.Storage;
using Xunit;
namespace Takupoke.Integration.Tests;
public sealed class RecoveryParallelProofTests
{
    internal static PdfPageLayout[] TwoPages(string mode)
    {
        var second = Layout("wholeformal");
        second = second with { Glyphs = second.Glyphs.Select(g => g.Text == "2" && g.X == 84 && g.Y == 171 ? g with { Text = "1" } : g).ToArray() };
        return [Layout(mode), second];
    }
    internal static RecoveryDocument Build(PdfPageLayout[] pages) => RecoveryDocumentBuilder.Build(new string('a',64), MaterialKind.Timetable, pages,
        (pi,b) => !pages[pi-1].Glyphs.Any(g => Math.Max(b.X,g.X) < Math.Min(b.X+b.Width,g.X+g.Width) && Math.Max(b.Y,g.Y) < Math.Min(b.Y+b.Height,g.Y+g.Height)));
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OriginalParallelTuplesSurviveMultiPageStrictRefusalAndEveryMergedSlot(bool merged)
    {
        var pages = TwoPages(merged ? "parallelMerged" : "parallel");
        Assert.Equal("P04", Assert.Throws<PdfParseException>(() => PdfScheduleParser.Timetable(pages)).Stage);
        var doc = Build(pages);
        Assert.Equal(80, doc.RequiredSlots.Count); Assert.Empty(RecoveryValidator.InputErrors(doc));
        var cell = Assert.Single(doc.Cells, c => c.ParallelSeparators is not null);
        Assert.Equal(2, cell.ParallelCount); Assert.Equal(merged ? new[]{1,2} : new[]{1}, cell.Slots.Select(s => s.Period));
        Assert.Equal(new[]{"subject","teacher","room"}, cell.ParallelSeparators!.Keys);
        Assert.All(cell.ParallelSeparators.Values,id => Assert.Equal("・", Assert.Single(doc.Sources,s => s.Id == id).Text));
        var run = await RecoveryEngine.RunAsync(doc,"windows",10,true,[],_ => null);
        Assert.Equal(RecoveryJobState.AwaitingConfirmation,run.State); Assert.NotNull(run.Result);
        Assert.True(RecoveryValidator.Validate(doc,run.Result).CanAdopt);
        var roundtrip = DataCodec.Decode<RecoveryDocument>(DataCodec.Encode(doc));
        Assert.Equal(RecoveryValidator.Fingerprint(doc),RecoveryValidator.Fingerprint(roundtrip));
        Assert.True(RecoveryValidator.Validate(roundtrip,run.Result).CanAdopt);
        var now = DateTimeOffset.Parse("2032-04-01T00:00:00Z"); var source = new SourceRecord("fictional",MaterialKind.Timetable,"fictional.pdf","fictional.pdf","fictional",doc.PdfHash,0,now,now,null);
        var formal = RecoveryAnalysisConverter.Convert(source,doc,run.Result,now).Timetable!;
        // Literal values are checked only after production conversion.
        Assert.Equal(merged ? 7 : 5, formal.Lessons.Count);
        foreach(var period in merged ? new[]{1,2} : new[]{1})
        {
            var pair = formal.Lessons.Where(l => l.ClassName == "1_2" && l.Weekday == 1 && l.Period == period).ToArray();
            Assert.Equal(new[]{new LessonNames("架空科A","架空師B","架空室C"),new LessonNames("架空科D","架空師E","架空室F")},pair.Select(l => l.Names));
        }
        Assert.Equal(new[]{"1_1","1_2"}, doc.Classes.Order());
        var expected = pages.SelectMany(p => PdfScheduleParser.Timetable([p]).Lessons);
        Assert.Equal(expected.Select(l => (l.ClassName,l.Weekday,l.Period,l.Names)), formal.Lessons.Select(l => (l.ClassName,l.Weekday,l.Period,l.Names)));
    }
    [Theory]
    [InlineData("wholeformal",4)]
    [InlineData("merged",5)]
    public async Task OriginalCompleteAndMergedMultiPageControlsRemainExact(string mode,int count)
    {
        var pages=TwoPages(mode);Assert.Throws<PdfParseException>(()=>PdfScheduleParser.Timetable(pages));var doc=Build(pages);
        var run=await RecoveryEngine.RunAsync(doc,"windows",10,true,[],_=>null);
        Assert.Equal(RecoveryJobState.AwaitingConfirmation,run.State); Assert.Equal(80,doc.RequiredSlots.Count);
        Assert.Equal(count,run.Result!.Cells.Sum(c=>c.Lessons.Count*doc.Cells.Single(x=>x.Id==c.CellId).Slots.Count));
        Assert.All(doc.Cells,c=>Assert.Null(c.ParallelSeparators));
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("missingRole")]
    [InlineData("duplicate")]
    [InlineData("crossCell")]
    [InlineData("wrongText")]
    [InlineData("wrongRole")]
    [InlineData("reverseBindings")]
    [InlineData("bindingReuse")]
    public void UnprovedOrMisownedSeparatorsCannotPassPreflightResultOrConversion(string fault)
    {
        var doc=Build(TwoPages("parallel"));var cell=Assert.Single(doc.Cells,c=>c.ParallelSeparators is not null);
        var proof=new Dictionary<string,string>(cell.ParallelSeparators!);var sources=doc.Sources;
        var changed=fault switch {
            "missing"=>cell with{ParallelSeparators=null},
            "reverseBindings"=>cell with{LessonBindings=cell.LessonBindings.Reverse().ToArray()},
            "bindingReuse"=>cell with{LessonBindings=[cell.LessonBindings[0] with{Subject=cell.LessonBindings[0].Subject.Concat([proof["subject"]]).ToArray()},cell.LessonBindings[1]]},
            _=>cell
        };
        if(fault=="missingRole")proof.Remove("room");
        if(fault=="duplicate")proof["room"]=proof["teacher"];
        if(fault=="crossCell")proof["teacher"]=doc.Cells.First(c=>c.Id!=cell.Id&&c.SourceIds.Count>0).SourceIds[0];
        if(fault=="wrongRole")(proof["subject"],proof["teacher"])=(proof["teacher"],proof["subject"]);
        if(fault=="wrongText")sources=doc.Sources.Select(s=>s.Id==proof["teacher"]?s with{Text="x"}:s).ToArray();
        if(fault is not ("missing" or "reverseBindings" or "bindingReuse"))changed=cell with{ParallelSeparators=proof};
        var bad=doc with{Cells=doc.Cells.Select(c=>c.Id==cell.Id?changed:c).ToArray(),Sources=sources};
        Assert.NotEmpty(RecoveryValidator.InputErrors(bad));
        var result=new RecoveryResult(doc.PdfHash,doc.Kind,doc.SchoolYear,doc.Term,RecoveryRules.RecoverAll(doc).Select(c=>c!).ToArray(),new("rule","rules","3","3","1",2,RecoveryValidator.Version,"test"));
        Assert.False(RecoveryValidator.Validate(bad,result).CanAdopt);
        var now=DateTimeOffset.UtcNow;var source=new SourceRecord("fictional",MaterialKind.Timetable,"fictional.pdf","fictional.pdf","fictional",doc.PdfHash,0,now,now,null);
        Assert.Throws<InvalidDataException>(()=>RecoveryAnalysisConverter.Convert(source,bad,result,now));
    }
    [Theory]
    [InlineData("parallelMismatch")]
    [InlineData("blankTeacher")]
    [InlineData("blankRoom")]
    [InlineData("blankBoth")]
    public void UnknownOrUnprovedRoleLayoutsRemainTerminal(string mode)=>Assert.Throws<InvalidDataException>(()=>Build(TwoPages(mode)));
    [Fact]
    public async Task EmptyParallelTeacherHalfNeedsActualInkFreeBandAndNeverShiftsTheRightTuple()
    {
        var pages=TwoPages("parallel");
        var omitted=pages[0].Glyphs.Where(g=>g.Y==171&&g.X>=133&&g.X<165).ToArray();Assert.Equal(4,omitted.Length);
        pages[0]=pages[0] with{Glyphs=pages[0].Glyphs.Except(omitted).ToArray()};
        var doc=Build(pages);var cell=Assert.Single(doc.Cells,c=>c.ParallelSeparators is not null);Assert.Contains("teacher",cell.BlankFields);
        var run=await RecoveryEngine.RunAsync(doc,"windows",10,true,[],_=>null);Assert.Equal(RecoveryJobState.AwaitingConfirmation,run.State);
        var pair=run.Result!.Cells.Single(c=>c.CellId==cell.Id).Lessons;
        Assert.Equal(RecoveryValueState.Empty,pair[0].Teacher.State);Assert.Equal("架空師E",pair[1].Teacher.Value);
        Assert.Throws<InvalidDataException>(()=>RecoveryDocumentBuilder.Build(doc.PdfHash,MaterialKind.Timetable,pages,(pi,b)=>
            !pages[pi-1].Glyphs.Concat(pi==1?omitted:[]).Any(g=>Math.Max(b.X,g.X)<Math.Min(b.X+b.Width,g.X+g.Width)&&Math.Max(b.Y,g.Y)<Math.Min(b.Y+b.Height,g.Y+g.Height))));
    }
    [Fact]
    public void AWholeOriginalOcrLineCannotInventASubstringSeparatorAtom()
    {
        var pages=TwoPages("parallel");var first=pages[0];var body=first.Glyphs.Where(g=>g.Y==139&&g.X>=133&&g.X<210).OrderBy(g=>g.X).ToArray();
        pages[0]=first with{Glyphs=first.Glyphs.Except(body).Concat([new PdfGlyph(string.Concat(body.Select(g=>g.Text)),133,139,72,26)]).ToArray()};
        Assert.Throws<InvalidDataException>(()=>Build(pages));
    }
    [Fact]
    public void EveryCoveredSlotMustHaveTheSameOrderedStrictTuples()
    {
        var a=new LessonNames("架空A","架空師A","架空室A");var b=new LessonNames("架空B","架空師B","架空室B");
        NormalLesson Row(int period,LessonNames names)=>new("1_2",1,period,names,"架空原文",1);
        var rows=new[]{Row(1,a),Row(1,b),Row(2,a),Row(2,b)};RecoverySlot[] slots=[new("1_2","1",1),new("1_2","1",2)];
        IReadOnlyList<LessonNames>? Trust(IReadOnlyList<NormalLesson> values)=>RecoveryDocumentBuilder.TrustedNormalTuples(new(2032,"前期",values),slots,_=>{});
        Assert.Equal(new[]{a,b},Trust(rows));Assert.Null(Trust(rows.Take(3).ToArray()));Assert.Null(Trust([rows[0],rows[1],rows[3],rows[2]]));
        Assert.Null(Trust([rows[0],rows[1],rows[2],rows[3] with{Names=b with{Room="架空別室"}}]));Assert.Null(Trust([..rows,Row(2,a)]));
        Assert.Throws<OperationCanceledException>(()=>RecoveryDocumentBuilder.TrustedNormalTuples(new(2032,"前期",rows),slots,_=>throw new OperationCanceledException()));
    }
    [Fact]
    public async Task OversizedAndRepeatedSeparatorProofInputsCannotEscapeLimitsOrReachProviders()
    {
        var doc=Build(TwoPages("parallel"));var cell=Assert.Single(doc.Cells,c=>c.ParallelSeparators is not null);
        var provider=new ProviderProbe();var oversized=cell with{ParallelSeparators=new Dictionary<string,string>{["subject"]="x",["teacher"]="y",["room"]="z",["extra"]="w"}};
        var bad=doc with{Cells=doc.Cells.Select(c=>c.Id==cell.Id?oversized:c).ToArray()};
        var run=await RecoveryEngine.RunAsync(bad,"windows",10,true,[provider],_=>null);
        Assert.Equal(RecoveryJobState.Failed,run.State);Assert.Contains("inputLimit",run.Errors);Assert.Equal(0,provider.Calls);
        var repeated=Enumerable.Repeat(cell.SourceIds[0],10000).ToArray();
        bad=doc with{Cells=Enumerable.Range(0,3000).Select(i=>cell with{Id="invented-duplicate-"+i,SourceIds=repeated}).ToArray()};
        run=await RecoveryEngine.RunAsync(bad,"windows",10,true,[provider],_=>null);
        Assert.Equal(RecoveryJobState.Failed,run.State);Assert.Contains("validationLimit",run.Errors);Assert.Equal(0,provider.Calls);
        using var canceled=new CancellationTokenSource();canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>RecoveryEngine.RunAsync(doc,"windows",10,true,[provider],_=>null,canceled.Token));
        Assert.Equal(0,provider.Calls);
    }
    private sealed class ProviderProbe:ILocalRecoveryProvider
    {
        public int Calls;public string Id=>"windowsLanguageModel";public bool LocalOnly=>true;
        public RecoveryMetadata Metadata=>new(Id,"fictional","1","1","4",2,RecoveryValidator.Version,"test");
        public Task<LocalProviderState> AvailabilityAsync(CancellationToken token){Calls++;return Task.FromResult(LocalProviderState.Ready);}
        public Task<IReadOnlyList<RecoveryLesson>> RecoverCellAsync(RecoveryPromptCell cell,CancellationToken token){Calls++;return Task.FromResult<IReadOnlyList<RecoveryLesson>>([]);}
    }
    [Fact]
    public void HistoricalV5NonparallelJsonAndScopeFingerprintRemainByteExact()
    {
        // Captured by an independent host control before editing 3b5b23b.
        var bytes=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"fixtures","recovery-historical-v5-vector.json"));
        var doc=JsonSerializer.Deserialize<RecoveryDocument>(bytes)!;
        Assert.All(doc.Cells,c=>Assert.Null(c.ParallelSeparators));
        Assert.Equal(bytes,JsonSerializer.SerializeToUtf8Bytes(doc));
        Assert.Equal("b6a18de1e38b1b13de4037626337271b884e9a09531567c8b8c0228bb5832c5f",RecoveryValidator.Fingerprint(doc));
        Assert.Equal("65a687702e3bbd0c577a340ac1b82546297ee15c7061e7bc55ba2afd4c0cdc6f",Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(DataCodec.Encode(doc))));
        Assert.Equal(2,RecoveryValidator.SchemaVersion);
    }
internal static PdfPageLayout Layout(string mode){
var glyphs=new List<PdfGlyph>{new("令和14年度",98,13,104,26),new("前期",350,13,40,26),new("時間割",550,13,60,26),new("1",34,171,12,26),new("2",84,171,12,26)};
var days=new[]{"月曜日","火曜日","水曜日","木曜日","金曜日"};
for(var d=0;d<5;d++){glyphs.Add(new(days[d],450+d*720,74,60,26));for(var r=0;r<8;r++)glyphs.Add(new((r+1).ToString(CultureInfo.InvariantCulture),159+(d*8+r)*90,107,12,26));}
var roles=new[]{"架空科A","架空師B","架空室C"};
for(var role=0;role<3;role++){
if(mode=="unreadWholeCell"||role==1&&(mode is "blankTeacher" or "blankBoth" or "unreadTeacher")||role==2&&(mode is "blankRoom" or "blankBoth"))continue;
if(mode.StartsWith("parallel")){var value=role==1&&mode=="parallelMismatch"?"架空師B":roles[role]+"・"+new[]{"架空科D","架空師E","架空室F"}[role];for(var j=0;j<value.Length;j++)glyphs.Add(new(value[j].ToString(),133+j*8,139+role*32,8,26));}
else glyphs.Add(new(roles[role],133,139+role*32,64,26));
}
// A complete independent physical neighbor supplies the existing Strict calibration.
for(var role=0;role<3;role++)glyphs.Add(new(new[]{"架空科G","架空師H","架空室I"}[role],313,139+role*32,64,26));
var rules=new List<PdfRule>{new(20,70,3720,70),new(20,136,3720,136),new(20,232,3720,232),new(120,104,3720,104),new(20,70,20,232),new(60,70,60,232),new(120,70,120,232)};
for(var i=1;i<=40;i++)rules.Add(new(120+i*90,i%8==0?70:104,120+i*90,i==1&&(mode is "merged" or "parallelMerged")?136:232));
return new(3740,800,glyphs,rules);
}
}
