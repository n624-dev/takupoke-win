using System.Text;
using System.Text.RegularExpressions;

namespace Takupoke.Infrastructure.Parsing;

public sealed record PdfFailurePosition(int ClassRow, int Weekday, int Period, int? DetectedLines = null);
public sealed class PdfParseException(string stage, int? page = null, PdfFailurePosition? cell = null)
    : Exception($"PDFの{Label(stage)}を確認できませんでした（{stage}" + (page is null ? "" : $"、{page}ページ")
        + (cell is null ? "" : $"、クラス位置{cell.ClassRow}・曜日{cell.Weekday}・{cell.Period}限" + (cell.DetectedLines is null ? "" : $"・{cell.DetectedLines}行"))
        + "）。正常な解析結果は保持しています。")
{
    public string Stage { get; } = stage;
    public int? Page { get; } = page;
    public PdfFailurePosition? Cell { get; } = cell;
    private static string Label(string stage) => stage switch
    { "raster" => "画像からの文字認識", "unreadable" => "読取可能な原本", "limit" => "解析上限", "P01" => "文字と位置の対応", "P02" => "ページの向き", "P03" => "年度の見出し", "P04" => "資料名・学期・ページ数",
        "P05" => "時限の見出し", "P06" => "表の列の罫線", "P07" => "日付の行の罫線", "P08" => "表のセルの罫線",
        "P10" => "日付の列", "P12" => "埋め込み描画", "P13" => "文字の行と読み順", "P14" => "クラス欄",
        "P15" => "学年欄", "P16" => "クラス行の重複", "P17" => "授業欄の行分け", "P18" => "並記された授業の対応",
        "P19" => "並記された科目の空欄", "P20" => "文字断片の重なり", "P21" => "文字断片の位置", _ => "構造または入力上限" };
}
public sealed record PdfGlyph(string Text, double X, double Y, double Width, double Height, int? SourceLine = null, int? SourceOrder = null)
{ public double Cx => X + Width / 2; public double Cy => Y + Height / 2; }
public sealed record PdfRule(double X1, double Y1, double X2, double Y2)
{ public bool Vertical => Math.Abs(X1 - X2) < 0.2; public bool Horizontal => Math.Abs(Y1 - Y2) < 0.2; }
public readonly record struct PdfBox(double Left, double Top, double Right, double Bottom);
public sealed record PdfPageLayout(double Width, double Height, IReadOnlyList<PdfGlyph> Glyphs, IReadOnlyList<PdfRule> Lines)
{
    public void ValidateViewport(int page)
    {
        if (Glyphs.Any(g => !new[] { g.X, g.Y, g.Width, g.Height }.All(double.IsFinite) || g.X < 0 || g.Y < 0 || g.X + g.Width > Width || g.Y + g.Height > Height)
            || Lines.Any(l => !new[] { l.X1, l.Y1, l.X2, l.Y2 }.All(double.IsFinite) || l.X1 < 0 || l.X2 < 0 || l.Y1 < 0 || l.Y2 < 0 || l.X1 > Width || l.X2 > Width || l.Y1 > Height || l.Y2 > Height))
            throw new PdfParseException("P01", page);
    }
    public void Validate(int page)
    {
        if (!double.IsFinite(Width) || !double.IsFinite(Height) || Width is <= 0 or > 5000 || Height is <= 0 or > 5000
            || Glyphs.Count > 100000 || Lines.Count > 100000
            || Glyphs.Any(g => !new[] { g.X, g.Y, g.Width, g.Height }.All(double.IsFinite) || g.Width < 0 || g.Height < 0 || Encoding.UTF8.GetByteCount(g.Text) > 64)
            || Lines.Any(l => !new[] { l.X1, l.Y1, l.X2, l.Y2 }.All(double.IsFinite))) throw new PdfParseException("limit", page);
        ValidateViewport(page);
        if (Glyphs.Count == 0) throw new PdfParseException("raster", page);
        if (Lines.Count == 0) throw new PdfParseException("P08", page);
    }
}
public sealed class PdfGrid(PdfPageLayout page, CancellationToken token = default)
{
    private long _work;
    private (PdfGlyph Glyph, int Index)[]? _yIndex;
    private readonly Dictionary<PdfBox, double[]?> _baselineCache = [];
    private void Step(long count = 1)
    {
        token.ThrowIfCancellationRequested();
        _work += count;
        if (_work > 64_000_000) throw new PdfParseException("limit");
    }
    private PdfBox? _lessonArea;
    private IReadOnlyList<PdfBox>? _calibrationBoxes;
    public void SetLessonArea(PdfBox area) { _lessonArea = area; _calibrationBoxes = null; _baselineCache.Clear(); }
    public void SetLessonCells(IEnumerable<PdfBox> cells)
    {
        if (_lessonArea is not { } area) throw new PdfParseException("P17");
        var boxes = cells.Distinct().ToArray();
        if (boxes.Any(c => c.Left < area.Left || c.Right > area.Right || c.Top < area.Top || c.Bottom > area.Bottom)) throw new PdfParseException("P17");
        _calibrationBoxes = boxes; _baselineCache.Clear();
    }
    public static string Key(string text) => Regex.Replace(text.Normalize(NormalizationForm.FormKC), @"\s", "");
    public PdfBox Box(double x, double y)
    {
        Step(page.Lines.Count * 4L);
        var vertical = page.Lines.Where(l => l.Vertical && l.Y1 - 0.8 <= y && y <= l.Y2 + 0.8).ToArray();
        var horizontal = page.Lines.Where(l => l.Horizontal && l.X1 - 0.8 <= x && x <= l.X2 + 0.8).ToArray();
        var left = vertical.Where(l => l.X1 < x - 0.5).Select(l => l.X1).DefaultIfEmpty(double.NaN).Max();
        var right = vertical.Where(l => l.X1 > x + 0.5).Select(l => l.X1).DefaultIfEmpty(double.NaN).Min();
        var top = horizontal.Where(l => l.Y1 < y - 0.5).Select(l => l.Y1).DefaultIfEmpty(double.NaN).Max();
        var bottom = horizontal.Where(l => l.Y1 > y + 0.5).Select(l => l.Y1).DefaultIfEmpty(double.NaN).Min();
        if (!new[] { left, right, top, bottom }.All(double.IsFinite)) throw new PdfParseException("P08");
        return new(left, top, right, bottom);
    }
    public IReadOnlyList<PdfGlyph> Glyphs(PdfBox box)
    {
        Step();
        if (_yIndex is null)
        {
            Step(page.Glyphs.Count);
            _yIndex = page.Glyphs.Select((g, i) => (Glyph: g, Index: i)).OrderBy(p => p.Glyph.Cy).ToArray();
            token.ThrowIfCancellationRequested();
        }
        var low = 0; var high = _yIndex.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (_yIndex[middle].Glyph.Cy <= box.Top + .3) low = middle + 1; else high = middle;
        }
        var selected = new List<(PdfGlyph Glyph, int Index)>();
        for (var i = low; i < _yIndex.Length && _yIndex[i].Glyph.Cy < box.Bottom - .3; i++)
        {
            Step(); var entry = _yIndex[i];
            if (box.Left + .3 < entry.Glyph.Cx && entry.Glyph.Cx < box.Right - .3) selected.Add(entry);
        }
        // Retain original input order for equal geometric positions and source rows.
        return selected.OrderBy(p => p.Index).Select(p => p.Glyph).ToArray();
    }
    public static IReadOnlyList<IReadOnlyList<PdfGlyph>> Rows(IEnumerable<PdfGlyph> glyphs)
    {
        var rows = new List<List<PdfGlyph>>();
        foreach (var glyph in glyphs.OrderBy(g => g.Cy))
        {
            if (rows.Count == 0 || Math.Abs(rows[^1][0].Cy - glyph.Cy) > 2) rows.Add([]);
            rows[^1].Add(glyph);
        }
        return rows.Select(r => (IReadOnlyList<PdfGlyph>)r.OrderBy(g => g.Cx).ToArray()).ToArray();
    }
    public static IReadOnlyList<IReadOnlyList<PdfGlyph>> ContentRows(IReadOnlyList<PdfGlyph> glyphs)
    {
        if (!glyphs.Any(g => g.SourceLine is not null || g.SourceOrder is not null)) return Rows(glyphs);
        if (glyphs.Any(g => g.SourceLine is null or < 0 || g.SourceOrder is null or < 0) || glyphs.Select(g => g.SourceOrder).Distinct().Count() != glyphs.Count)
            throw new PdfParseException("P13");
        return glyphs.GroupBy(g => g.SourceLine).Select(group => (IReadOnlyList<PdfGlyph>)group.OrderBy(g => g.SourceOrder).ToArray())
            .OrderBy(row => row.Select(g => g.Cy).Order().ElementAt(row.Count / 2)).ThenBy(row => row[0].SourceOrder).ToArray();
    }
    public IReadOnlyList<string> Text(PdfBox box) => ContentRows(Glyphs(box)).Select(r => string.Concat(r.Select(g => g.Text))).ToArray();
    private sealed record Fragment(IReadOnlyList<PdfGlyph> Glyphs)
    {
        public double Left => Glyphs.Min(g => g.X); public double Right => Glyphs.Max(g => g.X + g.Width);
        public double Top => Glyphs.Min(g => g.Y); public double Bottom => Glyphs.Max(g => g.Y + g.Height);
        public bool Precedes(Fragment next)
        {
            if (Right <= next.Left + 0.1) return true;
            var end = Glyphs[^1]; var start = next.Glyphs[0];
            return Left < next.Left && Right < next.Right && end.SourceOrder < start.SourceOrder && end.X < start.X && end.X + end.Width < start.X + start.Width;
        }
    }
    public IReadOnlyList<string> TimetableText(PdfBox box)
    {
        var input = Glyphs(box);
        if (input.Sum(g => Encoding.UTF8.GetByteCount(g.Text)) > 4096) throw new PdfParseException("limit");
        var rows = ContentRows(input);
        if (!input.Any(g => g.SourceLine is not null)) return rows.Select(r => string.Concat(r.Select(g => g.Text))).ToArray();
        var fragments = rows.Select(r => new Fragment(r)).OrderBy(f => f.Top).ThenBy(f => f.Left);
        var bands = new List<List<Fragment>>();
        foreach (var fragment in fragments)
        {
            var aligned = bands.Where(band => band.All(f => Math.Abs(f.Top - fragment.Top) <= 0.35 && Math.Abs(f.Bottom - fragment.Bottom) <= 0.35)).ToArray();
            if (aligned.Length > 1) throw new PdfParseException("P21");
            if (aligned.Length == 1)
            {
                if (aligned[0].Any(f => !(f.Left < fragment.Left ? f.Precedes(fragment) : fragment.Precedes(f)))) throw new PdfParseException("P20");
                aligned[0].Add(fragment);
            }
            else bands.Add([fragment]);
        }
        return bands.Select(band => string.Concat(band.OrderBy(f => f.Left).SelectMany(f => f.Glyphs).Select(g => g.Text))).ToArray();
    }
    /// Known Strict template roles are calibrated using intact neighboring cells.
    /// A short cell must occupy the same printed subject/teacher/room baselines;
    /// deleting a row never shifts another row into its role.
    public IReadOnlyList<string> LessonFields(PdfBox box)
    {
        Step(); var text = TimetableText(box);
        if (text.Count is 0 or 3) return text;
        if (text.Count > 3) throw new PdfParseException("P17");
        var rows = Rows(Glyphs(box)); if (rows.Count != text.Count) throw new PdfParseException("P17");
        if (_lessonArea is not { } area) throw new PdfParseException("P17");
        if (_calibrationBoxes is null)
        {
            var candidates = new HashSet<PdfBox>();
            Step(page.Glyphs.Count);
            foreach (var glyph in page.Glyphs.Where(g => g.Cx > area.Left && g.Cx < area.Right && g.Cy > area.Top && g.Cy < area.Bottom))
            { try { var candidate = Box(glyph.Cx, glyph.Cy); if (candidate.Left >= area.Left && candidate.Right <= area.Right && candidate.Top >= area.Top && candidate.Bottom <= area.Bottom) candidates.Add(candidate); } catch (PdfParseException error) when (error.Stage != "limit") { } }
            _calibrationBoxes = candidates.ToArray();
        }
        var assignments = new List<string[]>(); double[]? reference = null;
        foreach (var candidate in _calibrationBoxes.Where(c => Math.Abs(c.Bottom - c.Top - box.Bottom + box.Top) < .5))
        {
            Step();
            if (!_baselineCache.TryGetValue(candidate, out var baseline))
            {
                var candidateRows = Rows(Glyphs(candidate));
                baseline = null;
                if (candidateRows.Count == 3)
                {
                    var values = TimetableText(candidate);
                    if (values.Count == 3 && !values.Any(Takupoke.Core.Recovery.RecoveryRoleLabels.HasPrefix))
                        baseline = candidateRows.Select(r => r.Average(g => g.Cy) - candidate.Top).ToArray();
                }
                _baselineCache[candidate] = baseline;
            }
            if (baseline is null) continue;
            if (reference is not null && baseline.Where((value, i) => Math.Abs(reference[i] - value) > .75).Any()) throw new PdfParseException("P17");
            reference ??= baseline;
            var gap = baseline.Zip(baseline.Skip(1)).Min(p => p.Second - p.First); if (gap <= 2) continue;
            var tolerance = Math.Min(.75, gap / 3); var fields = new[] { "", "", "" }; var matched = true;
            foreach (var (row, i) in rows.Select((r, i) => (r, i)))
            {
                var y = row.Average(g => g.Cy) - box.Top; var roles = baseline.Select((v, role) => (v, role)).Where(p => Math.Abs(p.v - y) <= tolerance).ToArray();
                if (roles.Length != 1 || fields[roles[0].role].Length > 0) { matched = false; break; }
                fields[roles[0].role] = text[i];
            }
            if (matched) assignments.Add(fields);
        }
        if (assignments.Count == 0 || assignments[0][0].Length == 0 || assignments.Skip(1).Any(a => !a.SequenceEqual(assignments[0]))) throw new PdfParseException("P17");
        return assignments[0];
    }
    public IReadOnlyList<PdfBox> Slices(PdfBox row, double x)
    {
        Step(page.Lines.Count);
        var edges = new[] { row.Top }.Concat(page.Lines.Where(l => l.Horizontal && l.X1 - 0.5 <= x && x <= l.X2 + 0.5 && row.Top + 1 < l.Y1 && l.Y1 < row.Bottom - 1)
            .Select(l => Math.Round(l.Y1 * 100) / 100).Distinct().Order()).Append(row.Bottom).ToArray();
        return edges.Zip(edges.Skip(1)).Where(p => p.Second - p.First >= 2).Select(p => Box(x, (p.First + p.Second) / 2)).Distinct().ToArray();
    }
}

/// Ephemeral intermediate data, confined to a single reader attempt.
public sealed record RecoveryReadPage(int Page, Takupoke.Core.Recovery.RecoveryInputState State, PdfPageLayout? Layout);
public sealed class RecoveryReadCapture
{
    public IReadOnlyList<RecoveryReadPage> Pages { get; private set; } = [];
    public bool ReaderCompleted { get; private set; }
    public bool Complete => ReaderCompleted && Pages.Count > 0 && Pages.All(p => p.State == Takupoke.Core.Recovery.RecoveryInputState.Complete);
    public void Reset() { Pages = []; ReaderCompleted = false; }
    public void Begin(int pageCount) { if (pageCount is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(pageCount)); Pages = Enumerable.Range(1, pageCount).Select(p => new RecoveryReadPage(p, Takupoke.Core.Recovery.RecoveryInputState.RasterOnly, null)).ToArray(); ReaderCompleted = false; }
    public void Record(int page, Takupoke.Core.Recovery.RecoveryInputState state, PdfPageLayout layout) { if (page < 1 || page > Pages.Count) throw new ArgumentOutOfRangeException(nameof(page)); Pages = Pages.Select(p => p.Page == page ? new RecoveryReadPage(page, state, layout with { Glyphs = layout.Glyphs.ToArray(), Lines = layout.Lines.ToArray() }) : p).ToArray(); }
    public void Finish() => ReaderCompleted = true;
}
