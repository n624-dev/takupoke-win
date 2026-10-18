using System.Text.Json;
using Takupoke.Core;

namespace Takupoke.Infrastructure.Parsing;

public sealed record GeometryGlyph(int Id, int? SourceOrder, int? SourceLine, PdfBox Bounds);
public sealed record GeometryCell(PdfBox Original, PdfBox Content, IReadOnlyList<IReadOnlyList<int>> Rows);

/// No source strings, names, filenames, hashes or images are serialized.
public sealed class ReturnGeometryRecorder(string day, string sourceClass, int period)
{
    private readonly List<GeometryCell> _cells = [];
    private int _rowCount;
    private readonly List<GeometryGlyph> _glyphs = [];
    private readonly Dictionary<PdfGlyph, int> _ids = [];
    private readonly List<PdfRule> _rules = [];
    internal void Record(PdfPageLayout page, PdfGrid grid, PdfBox original, PdfBox content,
        string cellDay, string cellClass, int cellPeriod, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (cellDay != day || cellClass != sourceClass || cellPeriod != period || content.Top >= content.Bottom) return;
        if (_cells.Count >= 32) throw new PdfParseException("limit", branch:"R1130");
        var rows = grid.TimetableRows(content);
        if (_rowCount + rows.Count > 64) throw new PdfParseException("limit", branch:"R1131");
        _rowCount += rows.Count;
        var ids = rows.Select(row => (IReadOnlyList<int>)row.Select(glyph =>
        {
            token.ThrowIfCancellationRequested();
            if (_ids.TryGetValue(glyph, out var id)) return id;
            if (_glyphs.Count >= 4096) throw new PdfParseException("limit", branch:"R1132");
            id = _glyphs.Count; _ids.Add(glyph, id);
            _glyphs.Add(new(id, glyph.SourceOrder, glyph.SourceLine,
                new(glyph.X, glyph.Y, glyph.X + glyph.Width, glyph.Y + glyph.Height)));
            return id;
        }).ToArray()).ToArray();
        _cells.Add(new(original, content, ids));
        foreach (var line in page.Lines.Where(line =>
            Math.Max(line.X1,line.X2) >= original.Left && Math.Min(line.X1,line.X2) <= original.Right &&
            Math.Max(line.Y1,line.Y2) >= original.Top && Math.Min(line.Y1,line.Y2) <= original.Bottom))
        {
            if (_rules.Contains(line)) continue;
            if (_rules.Count >= 4096) throw new PdfParseException("limit", branch:"R1133");
            _rules.Add(line);
        }
    }
    public string ToJson()
    {
        if (_cells.Count == 0) throw new InvalidDataException("対応する原本のセル位置を確認できませんでした。");
        var result = JsonSerializer.Serialize(new { SchemaVersion = 1, Page = 1, Cells = _cells, Glyphs = _glyphs, Rules = _rules },
            new JsonSerializerOptions { WriteIndented = true });
        if (System.Text.Encoding.UTF8.GetByteCount(result) > 2 * 1024 * 1024) throw new PdfParseException("limit", branch:"R1134");
        return result;
    }
}

public static class PdfReturnGeometryDiagnostic
{
    public static string Create(byte[] bytes, SpecialLesson lesson, CancellationToken token = default)
    {
        if (lesson.Page != 1) throw new InvalidDataException("返却表の1ページ目の授業を選んでください。");
        var recorder = new ReturnGeometryRecorder(lesson.Date, lesson.SourceClassName ?? lesson.ClassName, lesson.Period);
        var pages = PdfPigLayoutReader.ReadForImport(bytes, MaterialKind.ExamReturn, token);
        PdfScheduleParser.Special(pages, MaterialKind.ExamReturn, token, ordinary:true, geometry:recorder);
        token.ThrowIfCancellationRequested(); return recorder.ToJson();
    }
}
