using System.Security.Cryptography;
using System.Text.Json;
using Takupoke.Core;
using Takupoke.Infrastructure.Materials;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Storage;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class PdfVisibilityTests
{
    [Theory]
    [InlineData(MaterialKind.Exam)]
    [InlineData(MaterialKind.ExamReturn)]
    public void SpecialTextShowOrderIncludesEmptyStringsQuotesAndWholeTJ(MaterialKind kind)
    {
        using var metadata = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Fixtures,"fictional-noto.json")));
        string Code(string value) => metadata.RootElement.GetProperty("glyphs").GetProperty(value).GetProperty("cid").GetInt32().ToString("X4");
        var content = $"BT /F1 8 Tf 1 0 0 1 20 450 Tm <{Code("A")}> Tj <> Tj [<{Code("C")}> -10 <> <{Code("N")}>] TJ <{Code("A")}> ' 0 0 <{Code("C")}> \" ET";
        var bytes = FictionalTimetablePdf.Make(Fixtures,extraGridContent:content);
        using var document = UglyToad.PdfPig.PdfDocument.Open(bytes,new UglyToad.PdfPig.ParsingOptions { UseLenientParsing=false,SkipMissingFonts=false });
        var original = document.GetPage(1).Letters.Take(5).ToArray();
        Assert.Equal(new[] {"A","C","N","A","C"},original.Select(l => l.Value));
        Assert.Equal(new[] {1,3,3,4,5},original.Select(l => l.TextSequence));
        var actual = Assert.Single(PdfPigLayoutReader.Read(bytes,kind)).Glyphs.Take(5).ToArray();
        Assert.Equal(original.Select(l => l.Value),actual.Select(g => g.Text));
        Assert.Equal(new int?[] {1,3,3,4,5},actual.Select(g => g.SourceLine));
    }
    [Theory]
    [InlineData(false,85)]
    [InlineData(true,7)]
    public void RealFontSpecialControlsKeepFullExamAndReturnTimetables(bool returns, int count)
    {
        var kind = returns ? MaterialKind.ExamReturn : MaterialKind.Exam;
        var layouts = returns ? new[] { SpecialPdfParityTests.ReturnPage() }
            : Enumerable.Range(1,6).Select(i => SpecialPdfParityTests.ExamPage(i)).ToArray();
        var bytes = FictionalTimetablePdf.MakeSpecial(Fixtures,layouts);
        using var font = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Fixtures,"fictional-noto-special.json")));
        Assert.Equal(font.RootElement.GetProperty("subsetSha256").GetString(),Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(Fixtures,"fictional-noto-special.cff")))));
        var parsed = PdfScheduleParser.Special(PdfPigLayoutReader.Read(bytes,kind),kind);
        Assert.Equal(count,parsed.Lessons.Count); Assert.Equal(17,parsed.CoveredClasses.Count); Assert.Equal(5,parsed.CoveredDates.Count);
        Assert.Contains(parsed.Lessons,l => l.Names.Subject == "架空科目A");
    }
    [Theory]
    [InlineData(false,"1 g",null,null)]
    [InlineData(true,"1 g",null,null)]
    [InlineData(false,"0 g","q 1 g 0 0 850 600 re f Q",null)]
    [InlineData(true,"0 g","q 1 g 0 0 1800 1000 re f Q",null)]
    [InlineData(false,"/GS0 gs",null,"/ca 0.5")]
    [InlineData(true,"/GS0 gs",null,"/CA 0")]
    [InlineData(false,"0 g","10 10 1 30 re 10 10 1 30 re f*",null)]
    [InlineData(true,"0 g","10 10 1 30 re 10 10 m 10 40 l 11 40 l 11 10 l h f",null)]
    public void SpecialPaintMustProveActualTextVisibility(bool returns,string paint,string? after,string? state)
    {
        var kind = returns ? MaterialKind.ExamReturn : MaterialKind.Exam;
        var layouts = returns ? new[] { SpecialPdfParityTests.ReturnPage() }
            : Enumerable.Range(1,6).Select(i => SpecialPdfParityTests.ExamPage(i)).ToArray();
        var bytes = FictionalTimetablePdf.MakeSpecial(Fixtures,layouts,paint,after,state);
        Assert.Equal("P01",Assert.Throws<PdfParseException>(() => PdfPigLayoutReader.Read(bytes,kind)).Stage);
    }
    [Theory]
    [InlineData(false,"10 10 1 30 re 20 10 1 30 re f*")]
    [InlineData(true,"10 10 1 30 re 11 10 1 30 re f")]
    public void IndependentCompoundFillControlsKeepTheSpecialTimetable(bool returns,string content)
    {
        var kind = returns ? MaterialKind.ExamReturn : MaterialKind.Exam;
        var layouts = returns ? new[] { SpecialPdfParityTests.ReturnPage() }
            : Enumerable.Range(1,6).Select(i => SpecialPdfParityTests.ExamPage(i)).ToArray();
        var parsed = PdfScheduleParser.Special(PdfPigLayoutReader.Read(FictionalTimetablePdf.MakeSpecial(Fixtures,layouts,afterText:content),kind),kind);
        Assert.Equal(returns ? 7:85,parsed.Lessons.Count);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HiddenSpecialReplacementKeepsPreviousGoodAfterEncryptedStoreRestart(bool returns)
    {
        var root = Path.Combine(Path.GetTempPath(),"takupoke-special-paint-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        using var protector = new Protector();
        try
        {
            var kind = returns ? MaterialKind.ExamReturn : MaterialKind.Exam;
            var layouts = returns ? new[] { SpecialPdfParityTests.ReturnPage() }
                : Enumerable.Range(1,6).Select(i => SpecialPdfParityTests.ExamPage(i)).ToArray();
            var black = FictionalTimetablePdf.MakeSpecial(Fixtures,layouts);
            var white = FictionalTimetablePdf.MakeSpecial(Fixtures,layouts,"1 g");
            var path = Path.Combine(root,"fictional.pdf"); string oldId, newId;
            await using (var store = new SchoolDataStore(root,protector))
            {
                var coordinator = new MaterialCoordinator(store,new(new Identity()));
                await File.WriteAllBytesAsync(path,black); Assert.True((await coordinator.SelectAsync(kind,path,2026)).Parsed);
                var lease = await store.BeginAsync(); oldId = (await store.ReadAsync<MaterialAnalysis>(lease,"analysis."+kind))!.OriginalId;
                await File.WriteAllBytesAsync(path,white); Assert.False((await coordinator.RefreshAsync(kind,2026)).Parsed);
                newId = (await store.ReadAsync<SourceRecord>(lease,"selection."+kind))!.Id; Assert.NotEqual(oldId,newId);
            }
            await using var reopened = new SchoolDataStore(root,protector); var restored = await reopened.BeginAsync();
            var kept = (await reopened.ReadAsync<MaterialAnalysis>(restored,"analysis."+kind))!;
            Assert.Equal(oldId,kept.OriginalId); Assert.Equal(returns ? 7:85,kept.Special!.Lessons.Count);
            Assert.Equal("P01",(await reopened.ReadAsync<MaterialAttempt>(restored,"attempt."+kind))!.Failure);
            Assert.Equal(black,await reopened.ReadOriginalAsync(restored,oldId));
            Assert.Equal(white,await reopened.ReadOriginalAsync(restored,newId));
        }
        finally { Directory.Delete(root,true); }
    }

    [Theory]
    [InlineData("q -10 -10 870 620 re W* n\n",true)]
    [InlineData("q 0 0 850 600 re W* n\n",false)]
    [InlineData("q 0 0 20 20 re W n\n",false)]
    public void RealFontSpecialReaderAcceptsOnlyClipsContainingEveryGlyphAndRule(string clip,bool accepted)
    {
        var bytes=FictionalTimetablePdf.MakeSpecial(Fixtures,[SpecialPdfParityTests.ExamPage(1)],afterText:"Q",beforeContent:clip);
        if(accepted) {
            var result=PdfScheduleParser.Special(PdfPigLayoutReader.Read(bytes,MaterialKind.Exam),MaterialKind.Exam);
            Assert.Equal(3,result.CoveredClasses.Count);Assert.Equal(15,result.Lessons.Count);
        } else Assert.Equal("P01",Assert.Throws<PdfParseException>(()=>PdfPigLayoutReader.Read(bytes,MaterialKind.Exam)).Stage);
    }

    [Theory]
    [InlineData("0 g 0 G 10 w 1 Tr",false)]
    [InlineData("0 g 0 G 10 w 2 Tr",true)]
    [InlineData("0 g 0.2 G 10 w 2 Tr",false)]
    public void SpecialStrokePaintRequiresItsOutlineUnlessTheCompleteSameInkFillRemains(string paint,bool outerAccepted)
    {
        var layout=new PdfPageLayout(500,500,[new("架",100,100,20,20),new("空",140,100,20,20)],[new(300,300,450,300)]);
        byte[] Make(string? cover=null)=>FictionalTimetablePdf.MakeSpecial(Fixtures,[layout],subjectPaint:paint,afterText:cover);
        var baseline=Make();Assert.Single(PdfPigLayoutReader.Read(baseline,MaterialKind.Exam));
        using var document=UglyToad.PdfPig.PdfDocument.Open(baseline);
        var ink=document.GetPage(1).Letters[0].BoundingBox;
        foreach(var inside in new[]{false,true}) {
            var right=ink.Left+(inside ? 1 : -0.05);
            var cover=FormattableString.Invariant($"1 g 0 {ink.Bottom-20:0.#########} {right:0.#########} {ink.Height+40:0.#########} re f\n");
            if(!inside && outerAccepted)Assert.Single(PdfPigLayoutReader.Read(Make(cover),MaterialKind.Exam));
            else Assert.Equal("P01",Assert.Throws<PdfParseException>(()=>PdfPigLayoutReader.Read(Make(cover),MaterialKind.Exam)).Stage);
        }
    }

    [Fact]
    public void SameInkModeTwoRequiresTheEntireOpaqueFilledGlyphInsideTheClip()
    {
        var paths=new PdfPathEngine(new(0,0,100,100,0),rectangularClips:true);
        paths.Operation("re",[10,10,80,80]);paths.Operation("W",[]);paths.Operation("n",[]);
        var paint=new PdfPaintVisibility();paint.Operation("w",[2]);paths.Operation("w",[2]);
        paint.Operation("Tr",[2]);paths.Operation("Tr",[2]);
        Assert.True(paint.FilledShapePreserved);
        paths.RequireUnclipped(new("架",11,30,10,10),sameInkFilledOutline:paint.FilledShapePreserved);
        Assert.Throws<PdfParseException>(()=>paths.RequireUnclipped(new("架",9,30,10,10),sameInkFilledOutline:paint.FilledShapePreserved));
        paint.Operation("g",[1]);Assert.False(paint.FilledShapePreserved);
        Assert.Throws<PdfParseException>(()=>paths.RequireUnclipped(new("架",11,30,10,10),sameInkFilledOutline:paint.FilledShapePreserved));
        paths.Operation("Tr",[1]);
        Assert.Throws<PdfParseException>(()=>paths.RequireUnclipped(new("架",11,30,10,10),sameInkFilledOutline:true));
        paint.Operation("Tr",[1]);Assert.False(paint.FilledShapePreserved);
        paths.Operation("Tr",[2]);paths.Operation("w",[0]);
        Assert.Throws<PdfParseException>(()=>paths.RequireUnclipped(new("架",30,30,10,10),sameInkFilledOutline:true));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void SpecialClipBoundsMiterJoinsAndRefusesUnboundedDeviceHairlines(int mode)
    {
        var paths=new PdfPathEngine(new(0,0,100,100,0),rectangularClips:true);
        paths.Operation("re",[10,10,80,80]);paths.Operation("W",[]);paths.Operation("n",[]);
        paths.Operation("w",[2]);paths.Operation("Tr",[mode]);
        paths.RequireUnclipped(new("架",30,30,10,10));
        Assert.Throws<PdfParseException>(()=>paths.RequireUnclipped(new("架",11,30,10,10)));
        paths.Operation("q",[]);paths.Operation("j",[1]);paths.RequireUnclipped(new("架",11,30,10,10));paths.Operation("Q",[]);
        Assert.Throws<PdfParseException>(()=>paths.RequireUnclipped(new("架",11,30,10,10)));
        paths.Operation("M",[2]);paths.RequireUnclipped(new("架",13,30,10,10));
        paths.Operation("M",[20]);Assert.Throws<PdfParseException>(()=>paths.RequireUnclipped(new("架",13,30,10,10)));
        paths.Operation("cm",[0.001,0,0,0.001,0,0]);paths.Operation("w",[0]);
        Assert.Throws<PdfParseException>(()=>paths.RequireUnclipped(new("架",30,30,10,10)));
        paths.Operation("m",[30000,30000]);paths.Operation("l",[40000,30000]);
        Assert.Throws<PdfParseException>(()=>paths.Operation("S",[]));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void SpecialClipContainsStrokeInkIncludingTransformAndTextMode(int mode)
    {
        var text=new PdfPathEngine(new(0,0,100,100,0),rectangularClips:true);
        text.Operation("re",[10,10,80,80]);text.Operation("W",[]);text.Operation("n",[]);
        text.Operation("q",[]);text.Operation("cm",[2,0,0,2,0,0]);text.Operation("w",[4]);text.Operation("j",[1]);text.Operation("Tr",[mode]);
        text.RequireUnclipped(new("架",20,20,10,10));
        Assert.Throws<PdfParseException>(()=>text.RequireUnclipped(new("架",11,20,10,10)));
        text.Operation("w",[0.1]);text.RequireUnclipped(new("架",10.2,20,10,10));
        text.Operation("Q",[]);text.RequireUnclipped(new("架",11,20,10,10));
        var rule=new PdfPathEngine(new(0,0,100,100,0),rectangularClips:true);
        rule.Operation("re",[10,10,80,80]);rule.Operation("W*",[]);rule.Operation("n",[]);
        rule.Operation("cm",[2,0,0,2,0,0]);rule.Operation("w",[4]);
        rule.Operation("m",[5.5,20]);rule.Operation("l",[5.5,30]);
        Assert.Throws<PdfParseException>(()=>rule.Operation("S",[]));
    }

    [Fact]
    public void RectangularSpecialClipsPreserveOnlyUncutInkAndRestoreOnQ()
    {
        var paths=new PdfPathEngine(new(0,0,100,100,0),rectangularClips:true);
        paths.Operation("q",[]);paths.Operation("re",[10,10,80,80]);paths.Operation("W*",[]);paths.Operation("n",[]);
        paths.RequireUnclipped(new("架",20,20,10,10));
        Assert.Throws<PdfParseException>(()=>paths.RequireUnclipped(new("架",5,20,10,10)));
        paths.Operation("Q",[]);paths.RequireUnclipped(new("架",5,20,10,10));
        Assert.Empty(paths.Finish());
        var ordinary=new PdfPathEngine(new(0,0,100,100,0));ordinary.Operation("re",[0,0,100,100]);
        Assert.Throws<PdfParseException>(()=>ordinary.Operation("W",[]));
        var curve=new PdfPathEngine(new(0,0,100,100,0),rectangularClips:true);
        curve.Operation("m",[0,0]);curve.Operation("c",[0,10,10,10,10,0]);
        Assert.Throws<PdfParseException>(()=>curve.Operation("W*",[]));
    }

    [Fact]
    public void IntersectingFilledRulesAndPartialEarlierBackgroundKeepContrastChecks()
    {
        var paint=new PdfPaintVisibility(partialBackgrounds:true);paint.Operation("g",[0.8]);
        paint.Paths(new([new (double,double)[]{(0,0),(30,0),(30,100),(0,100),(0,0)}],true,false,1,false));
        paint.Operation("g",[0]);
        paint.Paths(new([new (double,double)[]{(10,50),(90,50),(90,51),(10,51),(10,50)}],true,false,1,false));
        paint.Paths(new([new (double,double)[]{(50,10),(51,10),(51,90),(50,90),(50,10)}],true,false,1,false));
        paint.Validate();
        paint.Operation("g",[1]);
        paint.Paths(new([new (double,double)[]{(0,0),(100,0),(100,100),(0,100),(0,0)}],true,false,1,false));
        Assert.Throws<PdfParseException>(paint.Validate);
    }

    [Fact]
    public void OrdinaryTimetablesKeepTheirCompleteBackgroundRequirement()
    {
        foreach(var special in new[]{false,true}) {
            var paint=new PdfPaintVisibility(partialBackgrounds:special);
            paint.Operation("g",[0.8]);
            paint.Paths(new([new (double,double)[]{(0,0),(15,0),(15,50),(0,50),(0,0)}],true,false,1,false));
            paint.Operation("g",[0]);paint.Glyph(new("架",10,10,20,20));
            if(special)paint.Validate();else Assert.Throws<PdfParseException>(paint.Validate);
        }
    }

    [Fact]
    public async Task ScopedReturnSubsetCannotReplaceTheFullReturnAfterRestart()
    {
        var root=Path.Combine(Path.GetTempPath(),"takupoke-return-scope-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        using var protector=new Protector();
        try {
            var bytes=FictionalTimetablePdf.MakeSpecial(Fixtures,[SpecialPdfParityTests.ReturnPage()]);
            var path=Path.Combine(root,"fictional.pdf");await File.WriteAllBytesAsync(path,bytes);
            await using(var store=new SchoolDataStore(root,protector)) {
                var coordinator=new MaterialCoordinator(store,new(new Identity()));
                Assert.True((await coordinator.SelectAsync(MaterialKind.ExamReturn,path,2026)).Parsed);
                var lease=await store.BeginAsync();var analysis=(await store.ReadAsync<MaterialAnalysis>(lease,"analysis.ExamReturn"))!;
                var partial=analysis.Special! with { CoveredDates=["2026-04-01"],CoveredClasses=["3_IT"],
                    Lessons=analysis.Special!.Lessons.Where(l=>l.ClassName=="3_IT" && l.Date=="2026-04-01").ToArray(),
                    Scopes=[new(1,["3_IT"],["2026-04-01"])] };
                Assert.True(partial.ValidScopes());
                await Assert.ThrowsAsync<InvalidDataException>(()=>store.SaveAnalysisAsync(lease,analysis with { Special=partial }));
            }
            await using var reopened=new SchoolDataStore(root,protector);var restored=await reopened.BeginAsync();
            var kept=(await reopened.ReadAsync<MaterialAnalysis>(restored,"analysis.ExamReturn"))!;
            Assert.Equal(17,kept.Special!.CoveredClasses.Count);Assert.Equal(5,kept.Special.CoveredDates.Count);
        } finally { Directory.Delete(root,true); }
    }

    [Fact]
    public async Task FivePageExamScopeSurvivesEncryptedRestartAndMalformedReplacement()
    {
        var root=Path.Combine(Path.GetTempPath(),"takupoke-variable-exam-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        using var protector=new Protector();
        try {
            var pages=Enumerable.Range(1,5).Select(i=>SpecialPdfParityTests.ExamPage(i)).ToArray();
            var bytes=FictionalTimetablePdf.MakeSpecial(Fixtures,pages);
            var path=Path.Combine(root,"fictional.pdf");await File.WriteAllBytesAsync(path,bytes);
            await using(var store=new SchoolDataStore(root,protector)) {
                var coordinator=new MaterialCoordinator(store,new(new Identity()));
                Assert.True((await coordinator.SelectAsync(MaterialKind.Exam,path,2026)).Parsed);
                var lease=await store.BeginAsync();var analysis=(await store.ReadAsync<MaterialAnalysis>(lease,"analysis.Exam"))!;
                Assert.Equal(15,analysis.Special!.CoveredClasses.Count);Assert.True(analysis.Special.ValidScopes());
                var invalid=analysis with { Special=analysis.Special with { Scopes=null } };
                await Assert.ThrowsAsync<InvalidDataException>(()=>store.SaveAnalysisAsync(lease,invalid));
                var broken=pages[0] with { Glyphs=pages[0].Glyphs.Where(g=>g.Y!=70).ToArray() };
                await File.WriteAllBytesAsync(path,FictionalTimetablePdf.MakeSpecial(Fixtures,[broken]));
                Assert.False((await coordinator.RefreshAsync(MaterialKind.Exam,2026)).Parsed);
            }
            await using var reopened=new SchoolDataStore(root,protector);var restored=await reopened.BeginAsync();
            var kept=(await reopened.ReadAsync<MaterialAnalysis>(restored,"analysis.Exam"))!;
            Assert.Equal(75,kept.Special!.Lessons.Count);Assert.Equal(15,kept.Special.CoveredClasses.Count);
            Assert.False(kept.Special.Applies(new(2026,4,1),"AI_1"));Assert.True(kept.Special.Applies(new(2026,4,1),"2_CN"));
            Assert.Equal(bytes,await reopened.ReadOriginalAsync(restored,kept.OriginalId));
        } finally { Directory.Delete(root,true); }
    }

    private static string Fixtures => Path.Combine(AppContext.BaseDirectory, "fixtures");
    private static TimetableAnalysis Parse(byte[] bytes) => PdfScheduleParser.Timetable(PdfPigLayoutReader.Read(bytes, MaterialKind.Timetable));
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void RuleContrastUsesTheActualOpaqueBackgroundAndRejectsLaterCover(bool whiteInk, bool blackBackground)
    {
        var visibility = new PdfPaintVisibility();
        visibility.Operation("g", [blackBackground ? 0 : 1]);
        visibility.Paths(new([new (double,double)[]{(0,0),(100,0),(100,100),(0,100),(0,0)}],true,false,1,false));
        visibility.Operation("G", [whiteInk ? 1 : 0]);
        visibility.Paths(new([new (double,double)[]{(10,50),(90,50)}],false,true,1,false));
        if (whiteInk == blackBackground) visibility.Validate(); else Assert.Throws<PdfParseException>(visibility.Validate);
        visibility.Operation("g", [1]);
        visibility.Paths(new([new (double,double)[]{(0,0),(100,0),(100,100),(0,100),(0,0)}],true,false,1,false));
        Assert.Throws<PdfParseException>(visibility.Validate);
    }

    [Fact]
    public void MergedAndVisibleSplitRuleControlsUseTheirActualPeriodSpan()
    {
        Assert.Equal(new[]{1,2}, Parse(FictionalTimetablePdf.Make(Fixtures, mergedFirstPeriods:true)).Lessons.Select(l=>l.Period));
        Assert.Single(Parse(FictionalTimetablePdf.Make(Fixtures, mergedFirstPeriods:true,
            extraGridContent:"q 0 G 200 360 m 200 410 l S Q")).Lessons);
    }
    [Theory]
    [InlineData("q 1 G 200 360 m 200 410 l S Q")]
    [InlineData("q 1 g 199.5 360 1 50 re f Q")]
    public void InvisibleStrokeOrFilledBoundaryCannotChangeTheAcceptedMergedSpan(string content)
        => Assert.Throws<PdfParseException>(() => Parse(FictionalTimetablePdf.Make(Fixtures, mergedFirstPeriods:true, extraGridContent:content)));
    [Fact]
    public void EntirelyWhiteRulesCannotProveAWhitePageTable()
        => Assert.Throws<PdfParseException>(() => Parse(FictionalTimetablePdf.Make(Fixtures, gridPaint:"1 G")));

    [Theory]
    [InlineData("/CropBox [0 420 2000 500]")]
    [InlineData("/Annots [<< /Type /Annot /Subtype /Square /Rect [162 362 198 408] /C [1 1 1] /IC [1 1 1] /CA 1 /BS << /W 0 >> >>]")]
    public void CroppedOutAndAnnotationObscuredTimetablesRejectTheWholeInput(string pageEntries)
        => Assert.Equal("P01", Assert.Throws<PdfParseException>(() => Parse(FictionalTimetablePdf.Make(Fixtures, pageEntries: pageEntries))).Stage);
    [Theory]
    [InlineData("/CropBox [0 0 2000 500]")]
    [InlineData("/Annots []")]
    public void FullCropAndNoAnnotationControlsKeepTheSameLesson(string pageEntries)
        => Assert.Equal("架空科目A", Assert.Single(Parse(FictionalTimetablePdf.Make(Fixtures, pageEntries: pageEntries)).Lessons).Names.Subject);

    [Fact]
    public void RealFontControlUsesItsActualCidMappingAndProducesOneFictionalLesson()
    {
        using var metadata = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Fixtures, "fictional-noto.json")));
        Assert.Equal(metadata.RootElement.GetProperty("subsetSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(Fixtures, "fictional-noto.cff")))));
        var result = Parse(FictionalTimetablePdf.Make(Fixtures));
        var lesson = Assert.Single(result.Lessons); Assert.Equal("架空科目A", lesson.Names.Subject); Assert.Equal("1_CN", lesson.ClassName);
        Assert.Equal(2032, result.SchoolYear);
    }
    [Theory]
    [InlineData("/DeviceGray cs 1 sc")]
    [InlineData("/DeviceRGB cs 1 1 1 scn")]
    [InlineData("/DeviceCMYK cs 0 0 0 0 sc")]
    [InlineData("/DeviceGray CS 1 SC 1 Tr")]
    [InlineData("/DeviceRGB CS 1 1 1 SCN 1 Tr")]
    public void DeviceColourSpaceWhiteInkStillRejectsTheWholeInput(string paint)
        => Assert.Equal("P01", Assert.Throws<PdfParseException>(() => Parse(FictionalTimetablePdf.Make(Fixtures, paint))).Stage);
    [Theory]
    [InlineData("/DeviceRGB cs q /DeviceGray cs 1 sc Q 0 0 0 scn")]
    [InlineData("/DeviceRGB cs 1 1 1 scn 0 g 0 sc")]
    [InlineData("/DeviceGray CS 0 SC 1 Tr")]
    [InlineData("/DeviceRGB CS 0 0 0 SCN 1 Tr")]
    [InlineData("/DeviceCMYK cs")]
    public void DeviceColourSpacesTrackRestorationImplicitSelectionAndInitialBlack(string paint)
        => Assert.Single(Parse(FictionalTimetablePdf.Make(Fixtures, paint)).Lessons);
    [Fact]
    public void UnverifiedSpacesAndIncorrectComponentCountsAreRejected()
    {
        var paint = new PdfPaintVisibility();
        Assert.Throws<PdfParseException>(() => paint.SelectColourSpace(false, "Pattern"));
        Assert.Throws<PdfParseException>(() => paint.SelectColourSpace(false, "ICCBased"));
        paint.SelectColourSpace(false, "DeviceRGB");
        Assert.Throws<PdfParseException>(() => paint.Operation("sc", [0]));
        Assert.Throws<PdfParseException>(() => paint.Operation("scn", [0, 0, 2]));
        paint.Operation("q", []); paint.SelectColourSpace(false, "DeviceGray"); paint.Operation("Q", []);
        Assert.Throws<PdfParseException>(() => paint.Operation("sc", [0]));
    }
    [Theory]
    [InlineData("/DeviceGray cs 0 sc")]
    [InlineData("/DeviceRGB cs 0 0 0 scn")]
    [InlineData("/DeviceCMYK cs 0 0 0 1 sc")]
    public void StandardDeviceColourSpaceOperatorsProduceTheSameVerifiedBlackLesson(string paint)
        => Assert.Equal("架空科目A", Assert.Single(Parse(FictionalTimetablePdf.Make(Fixtures, paint)).Lessons).Names.Subject);
    [Theory]
    [InlineData("0 g")]
    [InlineData("0 0 0 rg")]
    [InlineData("0 0 0 1 k")]
    [InlineData("q 1 g Q")]
    [InlineData("q 1 g q 0 g Q Q")]
    public void VisibleBlackDeviceColoursAndRestoredStatesKeepTheLesson(string paint)
        => Assert.Equal("架空科目A", Assert.Single(Parse(FictionalTimetablePdf.Make(Fixtures, paint)).Lessons).Names.Subject);
    [Theory]
    [InlineData("1 g")]
    [InlineData("1 1 1 rg")]
    [InlineData("0 0 0 0 k")]
    public void WhiteTextOnTheWhitePageRejectsTheWholeInput(string paint)
        => Assert.Equal("P01", Assert.Throws<PdfParseException>(() => Parse(FictionalTimetablePdf.Make(Fixtures, paint))).Stage);
    [Theory]
    [InlineData("1 g", "0 g", true)]
    [InlineData("0 g", "0 g", false)]
    [InlineData("0.5 g", "0.5 g", false)]
    [InlineData("0 g", "0.5 g", true)]
    [InlineData("1 1 1 rg", "0 0 1 rg", true)]
    [InlineData("0 0 0 0 k", "0 0 0 1 k", true)]
    public void OpaqueColouredBackgroundsAreComparedRatherThanDroppingWhiteLetters(string text, string background, bool visible)
    {
        var bytes = FictionalTimetablePdf.Make(Fixtures, text, background);
        if (visible) Assert.Single(Parse(bytes).Lessons); else Assert.Throws<PdfParseException>(() => Parse(bytes));
    }
    [Theory]
    [InlineData("1 g 0 G 1 Tr", true)]
    [InlineData("0 g 1 G 1 Tr", false)]
    [InlineData("1 g 0 G 2 Tr", true)]
    [InlineData("0 g 1 G 2 Tr", false)]
    [InlineData("0 g 3 Tr", false)]
    public void StrokeAndFillModesRequireVerifiedVisibleInk(string paint, bool visible)
    {
        var bytes = FictionalTimetablePdf.Make(Fixtures, paint);
        if (visible) Assert.Single(Parse(bytes).Lessons); else Assert.Throws<PdfParseException>(() => Parse(bytes));
    }
    [Theory]
    [InlineData("/ca 0.5", false)]
    [InlineData("/CA 0", false)]
    [InlineData("/BM /Multiply", false)]
    [InlineData("/ca 1 /CA 1 /BM /Normal", true)]
    public void UnprovedTransparencyAndBlendModesRejectInsteadOfPretendingOpaque(string state, bool accepted)
    {
        var bytes = FictionalTimetablePdf.Make(Fixtures, "/GS0 gs 0 g", graphicsState: state);
        if (accepted) Assert.Single(Parse(bytes).Lessons); else Assert.Throws<PdfParseException>(() => Parse(bytes));
    }
    [Fact]
    public void LaterPaintAndMixedInvisibleCellsCannotBecomePartialSuccess()
    {
        Assert.Throws<PdfParseException>(() => Parse(FictionalTimetablePdf.Make(Fixtures, "0 g", "1 g", backgroundAfterText: true)));
        Assert.Throws<PdfParseException>(() => Parse(FictionalTimetablePdf.Make(Fixtures, extraWhiteCell: true)));
    }
    [Fact]
    public void PartialBackgroundsAndOverlappingFillSubpathsRemainUnverified()
    {
        static PdfGlyph Glyph() => new("架空科目A", 10, 10, 20, 10, 0, 0);
        var partial = new PdfPaintVisibility();
        partial.Paths(new([new (double, double)[] { (10, 10), (20, 10), (20, 20), (10, 20), (10, 10) }], true, false, 1, false));
        partial.Glyph(Glyph()); Assert.Throws<PdfParseException>(partial.Validate);
        // The same rectangle traversed twice can cancel under even-odd fill;
        // its bounding box is not proof of an opaque background.
        var retraced = new PdfPaintVisibility();
        Assert.Throws<PdfParseException>(() => retraced.Paths(new([
            new (double, double)[] { (0, 0), (50, 0), (50, 50), (0, 50), (0, 0), (50, 0), (50, 50), (0, 50), (0, 0) }], true, false, 1, false)));
        var holes = new PdfPaintVisibility();
        Assert.Throws<PdfParseException>(() => holes.Paths(new([
            new (double, double)[] { (0, 0), (50, 0), (50, 50), (0, 50), (0, 0) },
            new (double, double)[] { (10, 10), (30, 10), (30, 30), (10, 30), (10, 10) }], true, false, 1, false)));
    }
    [Fact]
    public void ColourValidationPreservesCancellationAndRejectsUnverifiedColourSpaces()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => PdfPigLayoutReader.Read(FictionalTimetablePdf.Make(Fixtures), MaterialKind.Timetable, cancellation.Token));
        foreach (var command in new[] { "2 g", "-1 0 0 rg", "/Pattern cs /FictionalPattern scn" })
            Assert.Throws<PdfParseException>(() => Parse(FictionalTimetablePdf.Make(Fixtures, command)));
    }
    [Theory]
    [InlineData("white")]
    [InlineData("crop")]
    [InlineData("annotation")]
    public async Task InvisibleReplacementKeepsThePreviousAnalysisAndBothOriginalsAcrossRestart(string failureMode)
    {
        var root = Path.Combine(Path.GetTempPath(), "takupoke-pdf-visibility-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        using var protector = new Protector();
        try
        {
            var file = Path.Combine(root, "fictional.pdf"); var black = FictionalTimetablePdf.Make(Fixtures); var white = failureMode switch {
                "crop" => FictionalTimetablePdf.Make(Fixtures, pageEntries: "/CropBox [0 420 2000 500]"),
                "annotation" => FictionalTimetablePdf.Make(Fixtures, pageEntries: "/Annots [<< /Type /Annot /Subtype /Square /Rect [162 362 198 408] /C [1 1 1] /IC [1 1 1] /CA 1 >>]"),
                _ => FictionalTimetablePdf.Make(Fixtures, "1 g")
            };
            var oldId = ""; var currentId = "";
            await using (var store = new SchoolDataStore(root, protector))
            {
                var coordinator = new MaterialCoordinator(store, new(new Identity()));
                await File.WriteAllBytesAsync(file, black); Assert.True((await coordinator.SelectAsync(MaterialKind.Timetable, file, 2032)).Parsed);
                var lease = await store.BeginAsync(); oldId = (await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Timetable"))!.OriginalId;
                await File.WriteAllBytesAsync(file, white); var failed = await coordinator.RefreshAsync(MaterialKind.Timetable, 2032);
                Assert.False(failed.Parsed); Assert.NotNull(failed.Error);
                currentId = (await store.ReadAsync<SourceRecord>(lease, "selection.Timetable"))!.Id; Assert.NotEqual(oldId, currentId);
            }
            await using var reopened = new SchoolDataStore(root, protector); var reopenedLease = await reopened.BeginAsync();
            var analysis = (await reopened.ReadAsync<MaterialAnalysis>(reopenedLease, "analysis.Timetable"))!;
            Assert.Equal(oldId, analysis.OriginalId); Assert.Equal("架空科目A", Assert.Single(analysis.Timetable!.Lessons).Names.Subject);
            Assert.Equal("P01", (await reopened.ReadAsync<MaterialAttempt>(reopenedLease, "attempt.Timetable"))!.Failure);
            Assert.Equal(black, await reopened.ReadOriginalAsync(reopenedLease, oldId)); Assert.Equal(white, await reopened.ReadOriginalAsync(reopenedLease, currentId));
            var source = (await reopened.ReadAsync<SourceRecord>(reopenedLease, "selection.Timetable"))!;
            Assert.False(MaterialCoordinator.AnalysisMatches(analysis, source, MaterialKind.Timetable, 2032));
            Assert.False(MaterialCoordinator.AnalysisMatches(analysis with { ParserVersion = 8 }, source with { Digest = analysis.SourceDigest }, MaterialKind.Timetable, 2032));
            Assert.False(MaterialCoordinator.AnalysisMatches(analysis with { ParserVersion = 9 }, source with { Digest = analysis.SourceDigest }, MaterialKind.Timetable, 2032));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    private sealed class Identity : IFileIdentityProvider
    { string IFileIdentityProvider.Identity(FileStream stream) => Path.GetFullPath(stream.Name); }
    private sealed class Protector : IKeyProtector, IDisposable
    {
        private readonly EnvelopeCipher _cipher = new(RandomNumberGenerator.GetBytes(32));
        public byte[] Protect(byte[] bytes) => _cipher.Encrypt(bytes, "fictional-pdf-key");
        public byte[] Unprotect(byte[] bytes) => _cipher.Decrypt(bytes, "fictional-pdf-key");
        public void Dispose() => _cipher.Dispose();
    }
}
