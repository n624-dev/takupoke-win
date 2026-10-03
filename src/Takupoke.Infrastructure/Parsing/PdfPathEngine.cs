namespace Takupoke.Infrastructure.Parsing;

public sealed class PdfDisplayTransform(double left, double bottom, double width, double height, int rotation)
{
    public double Width => rotation is 90 or 270 ? height : width;
    public double Height => rotation is 90 or 270 ? width : height;
    public (double X, double Y) Point(double sourceX, double sourceY)
    {
        var x = sourceX - left; var y = sourceY - bottom;
        return rotation switch { 90 => (y, x), 180 => (width - x, y), 270 => (height - y, width - x), 0 => (x, height - y), _ => throw new PdfParseException("P02") };
    }
    public PdfGlyph Glyph(PdfGlyph glyph)
    {
        var points = new[] { Point(glyph.X, glyph.Y), Point(glyph.X + glyph.Width, glyph.Y), Point(glyph.X, glyph.Y + glyph.Height), Point(glyph.X + glyph.Width, glyph.Y + glyph.Height) };
        var x = points.Min(p => p.X); var y = points.Min(p => p.Y);
        return glyph with { X = x, Y = y, Width = points.Max(p => p.X) - x, Height = points.Max(p => p.Y) - y };
    }
}
public sealed class PdfPathEngine(PdfDisplayTransform display, CancellationToken token = default)
{
    private PdfMatrix _ctm = PdfMatrix.Identity;
    private readonly Stack<PdfMatrix> _stack = new();
    private readonly List<List<(double X, double Y)>> _paths = [];
    private readonly List<PdfRule> _rules = [];
    private int _paintWork;
    private void ConsumePaintWork()
    {
        if (++_paintWork > 1_000_000) throw new PdfParseException("limit");
        if (_paintWork % 128 == 0) token.ThrowIfCancellationRequested();
    }
    private static readonly IReadOnlyDictionary<string, int> Counts = new Dictionary<string, int>
    { ["q"] = 0, ["Q"] = 0, ["cm"] = 6, ["m"] = 2, ["l"] = 2, ["re"] = 4, ["h"] = 0, ["S"] = 0, ["s"] = 0,
        ["f"] = 0, ["F"] = 0, ["f*"] = 0, ["B"] = 0, ["B*"] = 0, ["b"] = 0, ["b*"] = 0, ["n"] = 0, ["c"] = 6, ["v"] = 4, ["y"] = 4 };
    public static bool Supports(string operation) => Counts.ContainsKey(operation);
    public bool PendingFillIsThinRules
    {
        get
        {
            foreach (var path in _paths)
            {
                ConsumePaintWork();
                if (path.Count != 5 || path[0] != path[4] || path.Take(4).Distinct().Count() != 4) return false;
                var left = path.Min(p => p.X); var right = path.Max(p => p.X);
                var top = path.Min(p => p.Y); var bottom = path.Max(p => p.Y);
                if (path.Any(p => p.X != left && p.X != right || p.Y != top && p.Y != bottom)) return false;
                if (!((right - left <= 2.1 && bottom - top > 3) || (bottom - top <= 2.1 && right - left > 3))) return false;
                for (var index = 1; index < path.Count; index++)
                    if (path[index].X != path[index - 1].X && path[index].Y != path[index - 1].Y) return false;
            }
            return true;
        }
    }
    private (double X, double Y) Point(double x, double y) { var p = _ctm.Point(x, y); return display.Point(p.X, p.Y); }
    private void Close() { if (_paths.Count > 0 && _paths[^1].Count > 0) _paths[^1].Add(_paths[^1][0]); }
    public void Operation(string operation, double[] numbers)
    {
        token.ThrowIfCancellationRequested();
        if (!Counts.TryGetValue(operation, out var count) || numbers.Length != count || !numbers.All(double.IsFinite)) throw new PdfParseException("P12");
        switch (operation)
        {
            case "q": if (_stack.Count >= 64) throw new PdfParseException("limit"); _stack.Push(_ctm); break;
            case "Q": if (!_stack.TryPop(out _ctm)) throw new PdfParseException("P12"); break;
            case "cm": _ctm = PdfMatrix.From(numbers).FollowedBy(_ctm); break;
            case "m": _paths.Add([Point(numbers[0], numbers[1])]); break;
            case "l": if (_paths.Count > 0) _paths[^1].Add(Point(numbers[0], numbers[1])); break;
            case "re":
                var x = numbers[0]; var y = numbers[1]; var w = numbers[2]; var h = numbers[3];
                _paths.Add([Point(x, y), Point(x + w, y), Point(x + w, y + h), Point(x, y + h), Point(x, y)]); break;
            case "h": Close(); break;
            case "s": Close(); Paint(false, true); break;
            case "S": Paint(false, true); break;
            case "f": case "F": case "f*": Paint(true, false); break;
            case "B": case "B*": Paint(true, true); break;
            case "b": case "b*": Close(); Paint(true, true); break;
            case "n": _paths.Clear(); break;
            case "c": case "v": case "y": if (_paths.Count > 0) _paths[^1].Clear(); break;
        }
        if (_paths.Count > 10000 || _paths.Count > 0 && _paths[^1].Count > 10000) throw new PdfParseException("limit");
    }
    private void Line((double X, double Y) a, (double X, double Y) b)
    {
        if (Math.Abs(a.X - b.X) < 0.2 && Math.Abs(a.Y - b.Y) > 0.1) _rules.Add(new((a.X + b.X) / 2, Math.Min(a.Y, b.Y), (a.X + b.X) / 2, Math.Max(a.Y, b.Y)));
        else if (Math.Abs(a.Y - b.Y) < 0.2 && Math.Abs(a.X - b.X) > 0.1) _rules.Add(new(Math.Min(a.X, b.X), (a.Y + b.Y) / 2, Math.Max(a.X, b.X), (a.Y + b.Y) / 2));
        if (_rules.Count > 100000) throw new PdfParseException("limit");
    }
    private void Paint(bool fill, bool stroke)
    {
        token.ThrowIfCancellationRequested();
        foreach (var path in _paths)
        {
            ConsumePaintWork();
            if (path.Count < 2) continue;
            if (fill)
            {
                var left = path[0].X; var right = left; var top = path[0].Y; var bottom = top;
                foreach (var point in path)
                { ConsumePaintWork(); left = Math.Min(left, point.X); right = Math.Max(right, point.X); top = Math.Min(top, point.Y); bottom = Math.Max(bottom, point.Y); }
                if (right - left <= 2.1 && bottom - top > 3) Line(((left + right) / 2, top), ((left + right) / 2, bottom));
                else if (bottom - top <= 2.1 && right - left > 3) Line((left, (top + bottom) / 2), (right, (top + bottom) / 2));
            }
            if (stroke) for (var index = 1; index < path.Count; index++) { ConsumePaintWork(); Line(path[index - 1], path[index]); }
        }
        _paths.Clear();
    }
    public IReadOnlyList<PdfRule> Finish()
    { token.ThrowIfCancellationRequested(); if (_stack.Count > 0) throw new PdfParseException("P12"); return _rules.ToArray(); }
}
