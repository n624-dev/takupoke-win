using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Takupoke.Core;
using Takupoke.Infrastructure.Parsing;
using Xunit;

namespace Takupoke.Integration.Tests;

/// Invented in-memory fonts/PDFs exercise the native reader contract, not OCR
/// recognition or a complete timetable. No school fonts or documents are used.
public sealed class PdfMissingUnicodeRecoveryTests
{
    [Fact]
    public void StrictStillRefusesMissingMapAndRecoveryKeepsExactFontEvidence()
    {
        var font = Font(); var bytes = Pdf(font);
        using var native = UglyToad.PdfPig.PdfDocument.Open(bytes);
        Assert.Equal(new[] { 1, 2 }, native.GetPage(1).Text.EnumerateRunes().Select(r => r.Value));
        Assert.Throws<PdfParseException>(() => PdfPigLayoutReader.Read(bytes, MaterialKind.Timetable));
        var capture = new RecoveryReadCapture();
        var page = Assert.Single(PdfPigLayoutReader.ReadWithMissingUnicodeRecovery(bytes, MaterialKind.Timetable, default, capture));
        Assert.True(capture.Complete); Assert.Equal("AB", string.Concat(page.Glyphs.Select(g => g.Text)));
        Assert.Equal(4, page.Lines.Count);
        var hash = Convert.ToHexStringLower(SHA256.HashData(font));
        for (var index = 0; index < page.Glyphs.Count; index++)
        {
            var glyph = page.Glyphs[index]; var proof = Assert.IsType<Takupoke.Core.Recovery.RecoveryFontEvidence>(glyph.FontEvidence);
            Assert.Equal("F1", proof.Resource); Assert.Equal(hash, proof.FontHash); Assert.Equal("identity-default", proof.CidMapHash);
            Assert.Equal(index + 1, proof.Code); Assert.Equal(proof.Code, proof.Cid); Assert.Equal(index + 1, proof.GlyphId);
            Assert.Equal(65 + index, proof.Scalar); Assert.Equal(1, proof.ReaderVersion);
            Assert.Equal(index, glyph.SourceOrder); Assert.True(glyph.Width > 0 && glyph.Height > 0);
        }
    }
    [Theory]
    [InlineData("1 g ")] [InlineData("3 Tr ")]
    public void UnicodeRecoveryCannotBypassVisibility(string prefix)
    {
        var capture = new RecoveryReadCapture();
        Assert.Throws<PdfParseException>(() => PdfPigLayoutReader.ReadWithMissingUnicodeRecovery(Pdf(Font(), prefix: prefix), MaterialKind.Timetable, default, capture));
        Assert.False(capture.Complete);
    }
    [Fact]
    public void ExplicitInvalidMapIsNotReplacedByFontMetadata()
    {
        var capture = new RecoveryReadCapture();
        Assert.Throws<PdfParseException>(() => PdfPigLayoutReader.ReadWithMissingUnicodeRecovery(Pdf(Font(), toUnicode: "broken map"), MaterialKind.Timetable, default, capture));
        Assert.False(capture.Complete);
    }
    [Fact]
    public void KnownToUnicodeRetainsItsExistingAuthorityAndNeedsNoRecoveryProof()
    {
        var bytes = Pdf(Font(), toUnicode: "1 begincodespacerange <0000> <ffff> endcodespacerange 2 beginbfchar <0001> <0058> <0002> <0059> endbfchar");
        var strict = Assert.Single(PdfPigLayoutReader.Read(bytes, MaterialKind.Timetable));
        var recovered = Assert.Single(PdfPigLayoutReader.ReadWithMissingUnicodeRecovery(bytes, MaterialKind.Timetable, default, new()));
        Assert.Equal("XY", string.Concat(strict.Glyphs.Select(g => g.Text)));
        Assert.Equal(strict.Glyphs, recovered.Glyphs); Assert.All(recovered.Glyphs, g => Assert.Null(g.FontEvidence));
    }
    [Fact]
    public void ActualGidZeroCannotDisappearFromTheRecoveryOutput()
    {
        var capture = new RecoveryReadCapture();
        Assert.Throws<PdfParseException>(() => PdfPigLayoutReader.ReadWithMissingUnicodeRecovery(Pdf(Font(), shown: "00010000"), MaterialKind.Timetable, default, capture));
        Assert.False(capture.Complete);
    }
    [Fact]
    public void ExplicitCidToGidMappingIsPartOfTheEvidence()
    {
        var cidMap = new byte[] { 0, 0, 0, 2, 0, 1 };
        var page = Assert.Single(PdfPigLayoutReader.ReadWithMissingUnicodeRecovery(Pdf(Font(), cidMap: cidMap), MaterialKind.Timetable, default, new()));
        Assert.Equal("BA", string.Concat(page.Glyphs.Select(g => g.Text)));
        Assert.Equal(2, page.Glyphs[0].FontEvidence!.GlyphId);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(cidMap)), page.Glyphs[0].FontEvidence!.CidMapHash);
    }
    [Theory]
    [InlineData(0)] [InlineData(90)] [InlineData(180)] [InlineData(270)]
    public void NativeDrawingTraceAndGlyphsUseTheSamePageRotation(int rotation)
    {
        var page=Assert.Single(PdfPigLayoutReader.ReadWithMissingUnicodeRecovery(Pdf(Font(), rotation:rotation), MaterialKind.Timetable, default, new()));
        Assert.Equal("AB",string.Concat(page.Glyphs.Select(g=>g.Text))); page.ValidateViewport(1);
    }
    [Theory]
    [InlineData("2 Tc 75 Tz 3 Ts", "1 0 0 1 20 100")]
    [InlineData("", "1 0.2 0.1 1 20 100")]
    [InlineData("", "0 1 -1 0 100 50")]
    [InlineData("", "-1 0 0 1 100 100")]
    public void EffectiveDrawingBasisMatchesScalingRiseSpacingAndTextTransforms(string options,string matrix)
    {
        var page=Assert.Single(PdfPigLayoutReader.ReadWithMissingUnicodeRecovery(Pdf(Font(), options:options, matrix:matrix), MaterialKind.Timetable, default,new()));
        Assert.Equal("AB",string.Concat(page.Glyphs.Select(g=>g.Text)));
    }
    [Fact]
    public void NativeTraceKeepsPositionedTextAdjustmentsAndRepeatedDrawingEvents()
    {
        var page=Assert.Single(PdfPigLayoutReader.ReadWithMissingUnicodeRecovery(Pdf(Font(), showing:"[<0001> -120 <0002> <0001>] TJ"), MaterialKind.Timetable,default,new()));
        Assert.Equal("ABA",string.Concat(page.Glyphs.Select(g=>g.Text)));
        Assert.Equal(new[]{0,1,2},page.Glyphs.Select(g=>g.SourceOrder!.Value));
        Assert.Equal(7.2,page.Glyphs[1].X-page.Glyphs[0].X,6);
    }
    [Theory]
    [InlineData("/Tag BMC", "EMC")]
    [InlineData("/Tag << /ActualText (forged) >> BDC", "EMC")]
    [InlineData("10 10 90 90 re W n", "")]
    [InlineData("", "1 g 10 10 100 120 re f")]
    public void FontRecoveryNeverOverridesReplacementClippingOrLaterOcclusion(string prefix,string suffix)
    {
        var capture=new RecoveryReadCapture();
        Assert.Throws<PdfParseException>(()=>PdfPigLayoutReader.ReadWithMissingUnicodeRecovery(Pdf(Font(),prefix:prefix,suffix:suffix),MaterialKind.Timetable,default,capture));
        Assert.False(capture.Complete);
    }
    [Fact]
    public void MissingUnicodeRecoveryChecksCancellationBeforeNativeDrawing()
    {
        using var stop=new CancellationTokenSource();stop.Cancel();
        Assert.Throws<OperationCanceledException>(()=>PdfPigLayoutReader.ReadWithMissingUnicodeRecovery(Pdf(Font()),MaterialKind.Timetable,stop.Token,new()));
    }
    [Fact]
    public void AUniqueCmapEntryWithoutActualDrawableGlyphIsNotRecovered()
    {
        var font=Font();
        var count=BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4,2));
        for(var i=0;i<count;i++)
        {
            var offset=12+i*16;
            if(Encoding.ASCII.GetString(font,offset,4)!="loca") continue;
            var start=checked((int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(offset+8,4)));
            U16(font,start+6,18); // GID2 has no outline although cmap calls it B.
        }
        Assert.Throws<PdfParseException>(()=>PdfPigLayoutReader.ReadWithMissingUnicodeRecovery(Pdf(font),MaterialKind.Timetable,default,new()));
    }
    [Fact]
    public void SameFontNameAndObjectDoNotEraseTheSelectedResourceIdentity()
    {
        var page=Assert.Single(PdfPigLayoutReader.ReadWithMissingUnicodeRecovery(Pdf(Font(),showing:"<0001> Tj /F2 10 Tf <0002> Tj"),MaterialKind.Timetable,default,new()));
        Assert.Equal(new[]{"F1","F2"},page.Glyphs.Select(g=>g.FontEvidence!.Resource));
        Assert.Equal("AB",string.Concat(page.Glyphs.Select(g=>g.Text)));
    }
    [Theory]
    [InlineData("missing")] [InlineData("extra")] [InlineData("code")] [InlineData("resource")]
    [InlineData("unicode")] [InlineData("origin")] [InlineData("horizontal")]
    [InlineData("vertical")] [InlineData("outline")]
    public void RecoveryRequiresAnExactIndependentDrawingEvent(string changed)
    {
        var engine=new PdfTextEngine(traceForRecovery:true);engine.Operation("BT");engine.Operation("Tm",1,0,0,1,20,100);
        engine.SetFont(new(new(2,new Dictionary<int,string>{{1,"A"}}),new Dictionary<int,double>(),600,800,-200)
            { Resource="F1", RecoveryEvidence=code=>new("F1",new string('a',64),"identity",code,code,1,65) },10);
        engine.Show(new byte[]{0,1});engine.Operation("ET");
        var native=new PdfNativeGlyphTrace("F1",1,"\u0001",false,new(20,100),new(30,100),new(20,110));
        var altered=changed switch
        {
            "code"=>native with { Code=2 }, "resource"=>native with { Resource="F2" },
            "unicode"=>native with { UnicodeKnown=true,Text="B" },
            "origin"=>native with { Origin=new(20.1,100) },
            "horizontal"=>native with { Horizontal=new(31,100) },
            "vertical"=>native with { Vertical=new(20,111) },
            "outline"=>native with { Drawable=false }, _=>native
        };
        IReadOnlyList<PdfNativeGlyphTrace> events=changed switch { "missing"=>[],"extra"=>[native,native],_=>[altered] };
        Assert.Throws<PdfParseException>(()=>engine.FinishRecovery(events));
    }
    [Theory]
    [InlineData(MaterialKind.Timetable)] [InlineData(MaterialKind.Exam)] [InlineData(MaterialKind.ExamReturn)]
    public void StrictFirstCaptureCanBeCompletedWithoutRasterForEachTargetKind(MaterialKind kind)
    {
        var bytes=Pdf(Font());var capture=new RecoveryReadCapture();
        Assert.Throws<PdfParseException>(()=>PdfPigLayoutReader.Read(bytes,kind,default,capture));Assert.False(capture.Complete);
        PdfPigLayoutReader.ImproveMissingUnicodeCapture(bytes,kind,capture,default);
        Assert.True(capture.Complete);
        Assert.Equal("AB",string.Concat(Assert.Single(capture.Pages).Layout!.Glyphs.Select(g=>g.Text)));
        Assert.All(capture.Pages[0].Layout!.Glyphs,g=>Assert.NotNull(g.FontEvidence));
    }
    [Fact]
    public void IncompleteAlternateReaderCannotReplaceAnAlreadyCompletePage()
    {
        var original=new PdfPageLayout(200,200,[new("original",20,20,30,10)],[]);
        var capture=new RecoveryReadCapture();capture.Begin(1);capture.Record(1,Takupoke.Core.Recovery.RecoveryInputState.Complete,original);
        var preserved=capture.Pages[0].Layout;
        PdfPigLayoutReader.ImproveMissingUnicodeCapture(Pdf(Font(),shown:"00010000"),MaterialKind.Timetable,capture,default);
        Assert.Same(preserved,capture.Pages[0].Layout);Assert.False(capture.Complete);
        capture.Finish();
        PdfPigLayoutReader.ImproveMissingUnicodeCapture([],MaterialKind.Timetable,capture,default);
        Assert.Same(preserved,capture.Pages[0].Layout);Assert.True(capture.Complete);
    }
    [Fact]
    public void AnIncompleteRecoveryPageRemainsRasterOnly()
    {
        var capture=new RecoveryReadCapture();
        PdfPigLayoutReader.ImproveMissingUnicodeCapture(Pdf(Font(),shown:"00010000"),MaterialKind.Timetable,capture,default);
        Assert.False(capture.Complete);Assert.Equal(Takupoke.Core.Recovery.RecoveryInputState.RasterOnly,capture.Pages[0].State);
    }
    [Fact]
    public void VisibleOutlineBeyondPdfAdvanceIsIncludedInSourceGeometry()
    {
        var page=Assert.Single(PdfPigLayoutReader.ReadWithMissingUnicodeRecovery(Pdf(Font(outlineWidth:3000)),MaterialKind.Timetable,default,new()));
        Assert.True(page.Glyphs[0].Width>=30);Assert.True(page.Glyphs[1].Width>=30);
    }
    [Fact]
    public void CompositeOutlineStaysUnsupportedUntilItsRecursionIsBounded()
    {
        var font=Font();var count=BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4,2));
        for(var i=0;i<count;i++) {
            var offset=12+i*16;if(Encoding.ASCII.GetString(font,offset,4)!="glyf")continue;
            var start=checked((int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(offset+8,4)))+12;
            Array.Clear(font,start,24);U16(font,start,65535);U16(font,start+6,500);U16(font,start+8,700);
            U16(font,start+10,3);U16(font,start+12,2);
        }
        var capture=new RecoveryReadCapture();
        Assert.Throws<PdfParseException>(()=>PdfPigLayoutReader.ReadWithMissingUnicodeRecovery(Pdf(font),MaterialKind.Timetable,default,capture));
        Assert.False(capture.Complete);
    }
    internal static byte[] Font(int outlineWidth=500)
    {
        // Both supported Unicode maps agree on two distinct glyphs. Outlines
        // are deliberately synthetic; this fixture measures font extraction.
        var cmap = new byte[80]; U16(cmap, 2, 2); U16(cmap, 4, 0); U16(cmap, 6, 4); U32(cmap, 8, 20);
        U16(cmap, 12, 3); U16(cmap, 14, 1); U32(cmap, 16, 48);
        U16(cmap, 20, 12); U32(cmap, 24, 28); U32(cmap, 32, 1); U32(cmap, 36, 65); U32(cmap, 40, 66); U32(cmap, 44, 1);
        U16(cmap, 48, 4); U16(cmap, 50, 32); U16(cmap, 54, 4); U16(cmap, 56, 4); U16(cmap, 58, 1);
        U16(cmap, 62, 66); U16(cmap, 64, 65535); U16(cmap, 68, 65); U16(cmap, 70, 65535);
        U16(cmap, 72, (1 - 65) & 65535); U16(cmap, 74, 1);
        var head = new byte[54]; U32(head, 0, 0x00010000); U32(head, 12, 0x5f0f3cf5); U16(head, 18, 1000);
        U16(head, 40, 500); U16(head, 42, 700);
        var hhea = new byte[36]; U32(hhea, 0, 0x00010000); U16(hhea, 4, 800); U16(hhea, 6, (-200) & 65535);
        U16(hhea, 10, 600); U16(hhea, 34, 4);
        var maxp = new byte[32]; U32(maxp, 0, 0x00010000); U16(maxp, 4, 4); U16(maxp, 6, 4); U16(maxp, 8, 1);
        var hmtx = new byte[16]; for (var index = 0; index < 4; index++) U16(hmtx, index * 4, 600);
        var glyph = new byte[24]; U16(glyph, 0, 1); U16(glyph, 6, outlineWidth); U16(glyph, 8, 700); U16(glyph, 10, 3);
        glyph[14] = 0x31; glyph[15] = 0x21; glyph[16] = 0x11; glyph[17] = 0x21;
        U16(glyph, 18, outlineWidth); U16(glyph, 20, (-outlineWidth) & 65535); U16(glyph, 22, 700);
        var glyf = new byte[72]; glyph.CopyTo(glyf, 12); glyph.CopyTo(glyf, 36);
        var loca = new byte[10]; U16(loca, 2, 6); U16(loca, 4, 18); U16(loca, 6, 30); U16(loca, 8, 36);
        var post = new byte[32]; U32(post, 0, 0x00030000);
        var names = new[] { (1, "InventedExtractionFont"), (2, "Regular"), (6, "InventedExtractionFont") };
        var name = new byte[6 + 12 * names.Length + names.Sum(x => x.Item2.Length * 2)]; U16(name, 2, names.Length); U16(name, 4, 6 + 12 * names.Length);
        var cursor = 6 + 12 * names.Length;
        for (var index = 0; index < names.Length; index++)
        {
            var record = 6 + index * 12; var value = Encoding.BigEndianUnicode.GetBytes(names[index].Item2);
            U16(name, record, 3); U16(name, record + 2, 1); U16(name, record + 4, 0x409); U16(name, record + 6, names[index].Item1);
            U16(name, record + 8, value.Length); U16(name, record + 10, cursor - (6 + 12 * names.Length)); value.CopyTo(name, cursor); cursor += value.Length;
        }
        var tables = new[] { ("cmap", cmap), ("glyf", glyf), ("head", head), ("hhea", hhea), ("hmtx", hmtx), ("loca", loca), ("maxp", maxp), ("name", name), ("post", post) };
        var result = new List<byte>(new byte[12 + tables.Length * 16]); var directory = result.ToArray();
        U32(directory, 0, 0x00010000); U16(directory, 4, tables.Length); U16(directory, 6, 128); U16(directory, 8, 3); U16(directory, 10, 16);
        for (var index = 0; index < tables.Length; index++)
        {
            while (result.Count % 4 != 0) result.Add(0);
            Encoding.ASCII.GetBytes(tables[index].Item1).CopyTo(directory, 12 + index * 16);
            U32(directory, 20 + index * 16, result.Count); U32(directory, 24 + index * 16, tables[index].Item2.Length);
            result.AddRange(tables[index].Item2);
        }
        var output = result.ToArray(); directory.CopyTo(output, 0); return output;
    }
    private static byte[] Pdf(byte[] font, string prefix = "", string shown = "00010002", string? toUnicode = null, byte[]? cidMap = null, int rotation=0, string options="", string matrix="1 0 0 1 20 100", string? showing=null, string suffix="")
    {
        byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);
        byte[] Stream(byte[] value)
        { using var output = new MemoryStream(); output.Write(Ascii($"<< /Length {value.Length} >>\nstream\n")); output.Write(value); output.Write(Ascii("\nendstream")); return output.ToArray(); }
        var objects = new[] {
            Ascii("<< /Type /Catalog /Pages 2 0 R >>"), Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Ascii($"<< /Type /Page /Parent 2 0 R /Rotate {rotation} /MediaBox [0 0 200 200] /Resources << /Font << /F1 4 0 R /F2 4 0 R >> >> /Contents 5 0 R >>"),
            Ascii("<< /Type /Font /Subtype /Type0 /BaseFont /InventedExtractionFont /Encoding /Identity-H /DescendantFonts [6 0 R] " + (toUnicode is null ? "" : "/ToUnicode 9 0 R ") + ">>"),
            Stream(Ascii(prefix + $"BT /F1 10 Tf {options} {matrix} Tm " + (showing ?? $"<{shown}> Tj") + $" ET 10 10 100 120 re S {suffix}")),
            Ascii("<< /Type /Font /Subtype /CIDFontType2 /BaseFont /InventedExtractionFont /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /FontDescriptor 7 0 R /DW 600 " + (cidMap is null ? "" : "/CIDToGIDMap 10 0 R ") + ">>"),
            Ascii("<< /Type /FontDescriptor /FontName /InventedExtractionFont /Flags 32 /FontBBox [0 -200 600 800] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 /FontFile2 8 0 R >>"),
            Stream(font), Stream(Ascii(toUnicode ?? "")), Stream(cidMap ?? []) };
        using var pdf = new MemoryStream(); pdf.Write(Ascii("%PDF-1.7\n")); var offsets = new List<long>();
        for (var index = 0; index < objects.Length; index++) { offsets.Add(pdf.Position); pdf.Write(Ascii($"{index + 1} 0 obj\n")); pdf.Write(objects[index]); pdf.Write(Ascii("\nendobj\n")); }
        var xref = pdf.Position; pdf.Write(Ascii($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n"));
        foreach (var offset in offsets) pdf.Write(Ascii($"{offset:D10} 00000 n \n"));
        pdf.Write(Ascii($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n")); return pdf.ToArray();
    }
    private static void U16(byte[] bytes, int offset, int value) => BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset, 2), checked((ushort)value));
    private static void U32(byte[] bytes, int offset, int value) => BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset, 4), checked((uint)value));
}
