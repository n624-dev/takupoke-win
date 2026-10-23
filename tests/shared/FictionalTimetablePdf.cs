using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Takupoke.Integration.Tests;

/// <summary>Independent real-font PDF input; no reader output or school data is used.</summary>
public static class FictionalTimetablePdf
{
    public static byte[] Make(string root, string subjectPaint = "0 g", string? backgroundPaint = null,
        bool backgroundAfterText = false, bool extraWhiteCell = false, string? graphicsState = null, string? pageEntries = null, bool mergedFirstPeriods = false, string gridPaint = "0 G", string? extraGridContent = null)
    {
        using var metadata = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "fictional-noto.json")));
        var map = metadata.RootElement.GetProperty("glyphs");
        var ascent = metadata.RootElement.GetProperty("ascent").GetInt32();
        var descent = metadata.RootElement.GetProperty("descent").GetInt32();
        var font = File.ReadAllBytes(Path.Combine(root, "fictional-noto.cff"));
        static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
        string Text(string value, double x, double top, double size)
        {
            var codes = string.Concat(value.Select(c => map.GetProperty(c.ToString()).GetProperty("cid").GetInt32().ToString("X4", CultureInfo.InvariantCulture)));
            return $"BT /F1 {Number(size)} Tf 1 0 0 1 {Number(x)} {Number(500 - top - ascent * size / 1000)} Tm <{codes}> Tj ET\n";
        }
        var content = new StringBuilder();
        void Background() { if (backgroundPaint is not null) content.Append("q ").Append(backgroundPaint).Append(" 162 362 36 46 re f Q\n"); }
        if (!backgroundAfterText) Background();
        content.Append(gridPaint).Append(" 1 w\n");
        foreach (var y in new[] { 60, 90, 140 }) content.Append($"0 {500 - y} m 1760 {500 - y} l S\n");
        foreach (var x in new[] { 0, 80 }.Concat(Enumerable.Range(0, 41).Select(i => 160 + i * 40))) content.Append($"{x} 440 m {x} {(mergedFirstPeriods && x == 200 ? 410 : 360)} l S\n");
        if (extraGridContent is not null) content.Append(extraGridContent).Append('\n');
        content.Append(Text("令和14年度前期時間割", 120, 20, 12));
        for (var i = 0; i < 40; i++) content.Append(Text((i % 8 + 1).ToString(CultureInfo.InvariantCulture), 168 + i * 40, 70, 8));
        content.Append(Text("CN", 100, 110, 8)).Append(Text("1", 20, 110, 8));
        content.Append("q ").Append(subjectPaint).Append('\n').Append(Text("架空科目A", 164, 96, 6)).Append("Q\n");
        if (extraWhiteCell) content.Append("q 1 g\n").Append(Text("架空科目A", 204, 96, 6)).Append("Q\n");
        if (backgroundAfterText) Background();
        var cmap = new StringBuilder("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n/CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> def\n/CMapName /FictionalNoto def\n/CMapType 2 def\n1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
        var glyphs = map.EnumerateObject().ToArray();
        cmap.Append(glyphs.Length).Append(" beginbfchar\n");
        foreach (var glyph in glyphs) cmap.Append('<').Append(glyph.Value.GetProperty("cid").GetInt32().ToString("X4", CultureInfo.InvariantCulture)).Append("> <").Append(((int)glyph.Name[0]).ToString("X4", CultureInfo.InvariantCulture)).Append(">\n");
        cmap.Append("endbfchar\nendcmap\nCMapName currentdict /CMap defineresource pop\nend\nend");
        var widths = string.Join(" ", glyphs.Select(g => g.Value.GetProperty("cid").GetInt32() + " [" + g.Value.GetProperty("width").GetInt32() + "]"));
        static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);
        static byte[] Stream(byte[] bytes, string extra = "")
        {
            using var output = new MemoryStream(); output.Write(Ascii($"<< /Length {bytes.Length} {extra} >>\nstream\n")); output.Write(bytes); output.Write(Ascii("\nendstream")); return output.ToArray();
        }
        var resources = "/Font << /F1 4 0 R >>" + (graphicsState is null ? "" : " /ExtGState << /GS0 << " + graphicsState + " >> >>");
        var objects = new[]
        {
            Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
            Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Ascii("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 2000 500] /Resources << " + resources + " >> /Contents 5 0 R " + pageEntries + " >>"),
            Ascii("<< /Type /Font /Subtype /Type0 /BaseFont /FictionalNotoCJKSubset /Encoding /Identity-H /DescendantFonts [8 0 R] /ToUnicode 6 0 R >>"),
            Stream(Ascii(content.ToString())), Stream(Ascii(cmap.ToString())),
            Ascii($"<< /Type /FontDescriptor /FontName /FictionalNotoCJKSubset /Flags 4 /FontBBox [-1000 -1200 3000 3000] /ItalicAngle 0 /Ascent {ascent} /Descent {descent} /CapHeight 700 /StemV 80 /FontFile3 9 0 R >>"),
            Ascii("<< /Type /Font /Subtype /CIDFontType0 /BaseFont /FictionalNotoCJKSubset /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /FontDescriptor 7 0 R /DW 1000 /W [" + widths + "] >>"),
            Stream(font, "/Subtype /CIDFontType0C")
        };
        using var pdf = new MemoryStream(); pdf.Write(Ascii("%PDF-1.7\n")); var offsets = new List<long>();
        for (var index = 0; index < objects.Length; index++)
        { offsets.Add(pdf.Position); pdf.Write(Ascii($"{index + 1} 0 obj\n")); pdf.Write(objects[index]); pdf.Write(Ascii("\nendobj\n")); }
        var xref = pdf.Position; pdf.Write(Ascii($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n"));
        foreach (var offset in offsets) pdf.Write(Ascii(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n"));
        pdf.Write(Ascii($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n")); return pdf.ToArray();
    }
}
