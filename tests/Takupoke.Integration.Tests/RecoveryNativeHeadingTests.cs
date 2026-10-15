using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Xunit;
namespace Takupoke.Integration.Tests;
public sealed class RecoveryNativeHeadingTests
{
    [Theory]
    [InlineData("令和8年度時間割")]
    [InlineData("令和８年度時間割")]
    public void OriginalJoinedHeadingPreservesFullWidthYearEvidence(string text)
    {
        var page=RecoveryPipelineTests.Layout(MaterialKind.Timetable);
        page=page with { Glyphs=page.Glyphs.Where(g=>!g.Text.Contains("年度") && g.Text!="時間割")
            .Concat(text.Select((c,i)=>new PdfGlyph(c.ToString(),200+i*6,10,6,8,0,i))).ToArray() };
        var doc=RecoveryDocumentBuilder.Build(new string('a',64),MaterialKind.Timetable,[page],(_,_)=>true);
        Assert.Equal(2026,doc.SchoolYear);
        Assert.Contains(doc.Sources,s=>s.Text==text[2].ToString());
        Assert.Empty(RecoveryValidator.InputErrors(doc));
    }
    [Theory]
    [InlineData(MaterialKind.Timetable)][InlineData(MaterialKind.ExamReturn)]
    public void WholeNativeWeekdayAnnotationRetainsAllOriginalAtoms(MaterialKind kind)
    {
        var page=RecoveryPipelineTests.Layout(kind);
        page=page with { Glyphs=page.Glyphs.Select(g=> {
            if(g.Y!=45)return g;
            var day=kind==MaterialKind.Timetable ? g.Text : "日月火水木金土"[(int)new DateOnly(2026,10,int.Parse(g.Text.Replace("10月", "").Replace("日",""))).DayOfWeek].ToString();
            return g with { Text=kind==MaterialKind.Timetable ? "（"+day+"）" : g.Text+"（"+day+"）",Width=g.Width+10 };
        }).ToArray() };
        var doc=RecoveryDocumentBuilder.Build(new string('a',64),kind,[page],(_,_)=>true);
        Assert.Equal(5,doc.Days.Count);Assert.Empty(RecoveryValidator.InputErrors(doc));
        if(kind!=MaterialKind.Timetable) {
            var id=doc.DayEvidence.First().Value[0];
            var bad=doc with { Sources=doc.Sources.Select(s=>s.Id==id ? s with { Text=s.Text.Replace("木","金") } : s).ToArray() };
            Assert.NotEmpty(RecoveryValidator.InputErrors(bad));
        }
    }

    [Fact]
    public void CloseNativeClassAndBodyGlyphsAreSeparatedByTheirOriginalPhysicalOwners()
    {
        var page=RecoveryDateHeaderOwnershipTests.ReturnPage();
        page=page with { Glyphs=page.Glyphs.Select(g=>g.X==90 ? g with { X=119.5-g.Width } : g.X==144 ? g with { X=121 } : g).ToArray() };
        var doc=RecoveryDocumentBuilder.Build(new string('a',64),MaterialKind.ExamReturn,[page],(_,_)=>true,allowStructureProposal:true);
        Assert.Equal(680,doc.RequiredSlots.Count);Assert.Equal(17,doc.Classes.Count);
        Assert.DoesNotContain(RecoveryValidator.InputErrors(doc),error=>error.Contains("class",StringComparison.OrdinalIgnoreCase) || error=="unclassifiedSource");
        Assert.All(doc.ClassEvidence.Values.SelectMany(ids=>ids),id=>Assert.Equal("header",doc.Sources.Single(s=>s.Id==id).CellId));
    }
}
