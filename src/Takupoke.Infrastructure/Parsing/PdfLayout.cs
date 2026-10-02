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
    { "P01" => "文字と位置の対応", "P02" => "ページの向き", "P03" => "年度の見出し", "P04" => "資料名・学期・ページ数",
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
    public void Validate(int page)
    {
        if (!double.IsFinite(Width) || !double.IsFinite(Height) || Width is <= 0 or > 5000 || Height is <= 0 or > 5000
            || Glyphs.Count is < 1 or > 100000 || Lines.Count is < 1 or > 100000
            || Glyphs.Any(g => !new[] { g.X, g.Y, g.Width, g.Height }.All(double.IsFinite) || g.Width < 0 || g.Height < 0 || Encoding.UTF8.GetByteCount(g.Text) > 64)
            || Lines.Any(l => !new[] { l.X1, l.Y1, l.X2, l.Y2 }.All(double.IsFinite))) throw new PdfParseException("limit", page);
    }
}
public sealed class PdfGrid(PdfPageLayout page)
{
    public static string Key(string text) => Regex.Replace(text.Normalize(NormalizationForm.FormKC), @"\s", "");
    public PdfBox Box(double x, double y)
    {
        var vertical = page.Lines.Where(l => l.Vertical && l.Y1 - 0.8 <= y && y <= l.Y2 + 0.8).ToArray();
        var horizontal = page.Lines.Where(l => l.Horizontal && l.X1 - 0.8 <= x && x <= l.X2 + 0.8).ToArray();
        var left = vertical.Where(l => l.X1 < x - 0.5).Select(l => l.X1).DefaultIfEmpty(double.NaN).Max();
        var right = vertical.Where(l => l.X1 > x + 0.5).Select(l => l.X1).DefaultIfEmpty(double.NaN).Min();
        var top = horizontal.Where(l => l.Y1 < y - 0.5).Select(l => l.Y1).DefaultIfEmpty(double.NaN).Max();
        var bottom = horizontal.Where(l => l.Y1 > y + 0.5).Select(l => l.Y1).DefaultIfEmpty(double.NaN).Min();
        if (!new[] { left, right, top, bottom }.All(double.IsFinite)) throw new PdfParseException("P08");
        return new(left, top, right, bottom);
    }
    public IReadOnlyList<PdfGlyph> Glyphs(PdfBox box) => page.Glyphs.Where(g => box.Left + 0.3 < g.Cx && g.Cx < box.Right - 0.3 && box.Top + 0.3 < g.Cy && g.Cy < box.Bottom - 0.3).ToArray();
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
    public IReadOnlyList<PdfBox> Slices(PdfBox row, double x)
    {
        var edges = new[] { row.Top }.Concat(page.Lines.Where(l => l.Horizontal && l.X1 - 0.5 <= x && x <= l.X2 + 0.5 && row.Top + 1 < l.Y1 && l.Y1 < row.Bottom - 1)
            .Select(l => Math.Round(l.Y1 * 100) / 100).Distinct().Order()).Append(row.Bottom).ToArray();
        return edges.Zip(edges.Skip(1)).Where(p => p.Second - p.First >= 2).Select(p => Box(x, (p.First + p.Second) / 2)).Distinct().ToArray();
    }
}
