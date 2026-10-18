using System.Globalization;
using System.Text;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Xunit;

namespace Takupoke.Integration.Tests;
public sealed class PdfFontUseTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void UnusedUnsupportedFontDoesNotDiscardVerifiedVisibleText(bool emptyShow)
    {
        var capture = new RecoveryReadCapture();
        var pages = PdfPigLayoutReader.Read(Pdf("BT /F2 12 Tf " + (emptyShow ? "() Tj " : "") + "ET "), MaterialKind.Timetable, capture: capture);
        Assert.True(capture.Complete);
        Assert.Equal("架空", string.Concat(Assert.Single(pages).Glyphs.Select(g => g.Text)));
        Assert.Equal(4, pages[0].Lines.Count);
    }
    [Fact]
    public void ActualUseOfUnsupportedFontIsRefusedAndNeverCapturedComplete()
    {
        var capture = new RecoveryReadCapture();
        Assert.Throws<PdfParseException>(() => PdfPigLayoutReader.Read(Pdf("BT /F2 12 Tf (X) Tj ET "), MaterialKind.Timetable, capture: capture));
        Assert.False(capture.Complete);
        Assert.DoesNotContain(capture.Pages, p => p.State == RecoveryInputState.Complete);
    }
    [Theory]
    [InlineData("BT /Missing 12 Tf ET ")]
    [InlineData("BT /F2 0 Tf ET ")]
    [InlineData("BT /F2 -1 Tf ET ")]
    public void UnusedSelectionStillValidatesResourceAndSize(string prefix)
    { Assert.Throws<PdfParseException>(() => PdfPigLayoutReader.Read(Pdf(prefix), MaterialKind.Timetable)); }
    [Fact]
    public void CmapCanDescribeUnusedNullButActualNullTextIsRefused()
    {
        var map = PdfUnicodeMap.Read("1 begincodespacerange <00> <ff> endcodespacerange 2 beginbfchar <00> <0000> <41> <67b6> endbfchar"u8.ToArray());
        Assert.Equal("\0", map.Values[0]);
        var page = Assert.Single(PdfPigLayoutReader.Read(Pdf("", includeNull: true), MaterialKind.Timetable));
        Assert.Equal("架空", string.Concat(page.Glyphs.Select(g => g.Text)));
        var capture = new RecoveryReadCapture();
        Assert.Throws<PdfParseException>(() => PdfPigLayoutReader.Read(Pdf("", includeNull: true, showNull: true), MaterialKind.Timetable, capture: capture));
        Assert.False(capture.Complete);
    }
    [Theory]
    [InlineData("000a")] [InlineData("000d")] [InlineData("001b")]
    public void ShownControlCannotBeOmittedAsWhitespace(string control)
    { Assert.Throws<PdfParseException>(() => PdfPigLayoutReader.Read(Pdf("", includeNull: true, showNull: true, control: control), MaterialKind.Timetable)); }
    [Fact]
    public void RestoredFontIsTheActualFontUsedAndUnusedFontLimitRemains()
    {
        var page = Assert.Single(PdfPigLayoutReader.Read(Pdf("BT /F1 10 Tf ET q BT /F2 12 Tf ET Q ", selectValidFont: false), MaterialKind.Timetable));
        Assert.Equal("架空", string.Concat(page.Glyphs.Select(g => g.Text)));
        var prefix = string.Join(' ', Enumerable.Range(0, 129).Select(i => $"BT /Alias{i} 10 Tf ET"));
        Assert.Equal("limit", Assert.Throws<PdfParseException>(() => PdfPigLayoutReader.Read(Pdf(prefix, aliasCount: 129), MaterialKind.Timetable)).Stage);
    }
    [Fact]
    public void InvalidUnusedCmapStructureStillRefusesEveryActualFontUse()
    { Assert.Throws<PdfParseException>(() => PdfPigLayoutReader.Read(Pdf("", includeNull: true, control: "d800"), MaterialKind.Timetable)); }
    [Fact]
    public void CancellationIsCheckedBeforeDeferredFontResolutionEvenForEmptyShow()
    {
        using var cancelled = new CancellationTokenSource(); var resolutions = 0;
        var engine = new PdfTextEngine(cancelled.Token); engine.Operation("BT");
        engine.SelectFont(new Lazy<PdfFont>(() => { resolutions++; throw new InvalidOperationException(); }), 10);
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => engine.Show([]));
        Assert.Equal(0, resolutions);
    }
    private static byte[] Pdf(string prefix, bool includeNull = false, bool showNull = false, string control = "0000", int aliasCount = 0, bool selectValidFont = true)
    {
        var cmap = "1 begincodespacerange <00> <ff> endcodespacerange " + (includeNull ? "3 beginbfchar <00> <" + control + "> " : "2 beginbfchar ") + "<41> <67b6> <42> <7a7a> endbfchar";
        var content = prefix + "BT " + (selectValidFont ? "/F1 10 Tf " : "") + "1 0 0 1 20 100 Tm (AB) Tj " + (showNull ? "<00> Tj " : "") + "ET 10 10 100 120 re S";
        string Stream(string value) => "<< /Length " + Encoding.ASCII.GetByteCount(value) + " >>\nstream\n" + value + "\nendstream";
        var aliases = string.Join(' ', Enumerable.Range(0, aliasCount).Select(i => $"/Alias{i} 8 0 R"));
        var objects = new[] {
            "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Resources << /Font << /F1 4 0 R /F2 8 0 R " + aliases + " >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding /FirstChar 0 /LastChar 66 /Widths [" + string.Join(' ', Enumerable.Repeat("500", 67)) + "] /FontDescriptor 7 0 R /ToUnicode 6 0 R >>",
            Stream(content), Stream(cmap), "<< /Type /FontDescriptor /FontName /Helvetica /Flags 32 /FontBBox [0 -200 1000 800] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>" };
        var pdf = new StringBuilder("%PDF-1.7\n"); var offsets = new List<int> { 0 };
        for (var i = 0; i < objects.Length; i++) { offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString())); pdf.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n"); }
        var xref = Encoding.ASCII.GetByteCount(pdf.ToString()); pdf.Append("xref\n0 ").Append(objects.Length + 1).Append("\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) pdf.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        pdf.Append("trailer\n<< /Size ").Append(objects.Length + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}
