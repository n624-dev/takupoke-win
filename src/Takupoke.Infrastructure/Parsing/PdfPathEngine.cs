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
    private readonly Stack<(PdfMatrix Matrix,PdfBox? Clip)> _stack = new();
    private PdfBox? _clip;
    private PdfBox? _pendingClip;
    internal PdfBox? Clip => _clip;
    internal void IntersectPendingRectangleClip()
    {
        if (_pendingClip is not null || _paths.Count != 1) throw new PdfParseException("P01");
        var path = _paths[0];
        if (path.Count != 5 || path[0] != path[4] || path.Take(4).Distinct().Count() != 4) throw new PdfParseException("P01");
        var box = new PdfBox(path.Min(p=>p.X),path.Min(p=>p.Y),path.Max(p=>p.X),path.Max(p=>p.Y));
        if (path.Any(p=>p.X!=box.Left && p.X!=box.Right || p.Y!=box.Top && p.Y!=box.Bottom) ||
            path.Zip(path.Skip(1)).Any(p=>p.First.X!=p.Second.X && p.First.Y!=p.Second.Y)) throw new PdfParseException("P01");
        _pendingClip = _clip is {} c ? new(Math.Max(c.Left,box.Left),Math.Max(c.Top,box.Top),Math.Min(c.Right,box.Right),Math.Min(c.Bottom,box.Bottom)) : box;
        if (_pendingClip.Value.Right <= _pendingClip.Value.Left || _pendingClip.Value.Bottom <= _pendingClip.Value.Top) throw new PdfParseException("P01");
    }
    private void FinishClip()
    {
        if (_pendingClip is {} clip) { _clip=clip; _pendingClip=null; }
    }
    private readonly List<List<(double X, double Y)>> _paths = [];
    private readonly List<PdfRule> _rules = [];
    internal readonly record struct PaintColor(double Red,double Green,double Blue)
    {
        public double Contrast(PaintColor other) => Math.Max(Math.Abs(Red-other.Red),Math.Max(Math.Abs(Green-other.Green),Math.Abs(Blue-other.Blue)));
    }
    internal sealed record PaintedRule(PdfBox Box,int Operation,PaintColor? BackgroundColor = null);
    private readonly List<PaintedRule> _paintedRuleBoxes = [];
    private int _operationOrder;
    private double _strokePaintRadius = 1.5;
    private PdfBox? _filledRuleBox;
    internal IReadOnlyList<PaintedRule> PaintedRuleBoxes => _paintedRuleBoxes;
    internal void SetOperationOrder(int order) => _operationOrder=order;
    internal void SetStrokePaintRadius(double radius)
    {
        if (!double.IsFinite(radius) || radius <= 0) throw new PdfParseException("P01");
        _strokePaintRadius=radius;
    }
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
    internal bool PendingFillIsRectangles
    {
        get
        {
            foreach (var path in _paths)
            {
                ConsumePaintWork();
                if (path.Count!=5 || path[0]!=path[4] || path.Take(4).Distinct().Count()!=4) return false;
                var l=path.Min(p=>p.X);var r=path.Max(p=>p.X);var t=path.Min(p=>p.Y);var b=path.Max(p=>p.Y);
                if(path.Any(p=>p.X!=l && p.X!=r || p.Y!=t && p.Y!=b) ||
                    path.Zip(path.Skip(1)).Any(p=>p.First.X!=p.Second.X && p.First.Y!=p.Second.Y)) return false;
            }
            return true;
        }
    }
    internal void RecordBackgroundFill(PaintColor colour)
    {
        if (!PendingFillIsRectangles) throw new PdfParseException("P01");
        foreach(var path in _paths)
        {
            ConsumePaintWork();
            var box=new PdfBox(path.Min(p=>p.X),path.Min(p=>p.Y),path.Max(p=>p.X),path.Max(p=>p.Y));
            if (_clip is {} c) box=new(Math.Max(box.Left,c.Left),Math.Max(box.Top,c.Top),Math.Min(box.Right,c.Right),Math.Min(box.Bottom,c.Bottom));
            if (box.Right<=box.Left || box.Bottom<=box.Top) continue;
            _paintedRuleBoxes.Add(new(box,_operationOrder,colour));
            if(_paintedRuleBoxes.Count>100000) throw new PdfParseException("limit");
        }
    }
    public bool PendingStrokeIsRules(bool closeLast)
    {
        bool IsRule((double X, double Y) a, (double X, double Y) b)
        {
            ConsumePaintWork();
            return a.X == b.X && a.Y != b.Y || a.Y == b.Y && a.X != b.X;
        }
        for (var index = 0; index < _paths.Count; index++)
        {
            var path = _paths[index]; ConsumePaintWork();
            if (path.Count < 2) return false;
            for (var point = 1; point < path.Count; point++)
                if (!IsRule(path[point - 1], path[point])) return false;
            if (closeLast && index == _paths.Count - 1 && path[^1] != path[0] && !IsRule(path[^1], path[0])) return false;
        }
        return true;
    }
    private (double X, double Y) Point(double x, double y) { var p = _ctm.Point(x, y); return display.Point(p.X, p.Y); }
    private void Close() { if (_paths.Count > 0 && _paths[^1].Count > 0) _paths[^1].Add(_paths[^1][0]); }
    public void Operation(string operation, double[] numbers)
    {
        token.ThrowIfCancellationRequested();
        if (!Counts.TryGetValue(operation, out var count) || numbers.Length != count || !numbers.All(double.IsFinite)) throw new PdfParseException("P12");
        switch (operation)
        {
            case "q": if (_stack.Count >= 64) throw new PdfParseException("limit"); _stack.Push((_ctm,_clip)); break;
            case "Q": if (!_stack.TryPop(out var restored)) throw new PdfParseException("P12"); (_ctm,_clip)=restored; break;
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
            case "n": _paths.Clear(); FinishClip(); break;
            case "c": case "v": case "y": if (_paths.Count > 0) _paths[^1].Clear(); break;
        }
        if (_paths.Count > 10000 || _paths.Count > 0 && _paths[^1].Count > 10000) throw new PdfParseException("limit");
    }
    private void Line((double X, double Y) a, (double X, double Y) b)
    {
        // Clipped rules are not guessed. This bounded profile reuses only
        // paint fully contained by the effective original rectangle clip.
        if (_clip is {} c && (Math.Min(a.X,b.X)<c.Left || Math.Max(a.X,b.X)>c.Right || Math.Min(a.Y,b.Y)<c.Top || Math.Max(a.Y,b.Y)>c.Bottom))
            throw new PdfParseException("P01");
        var before=_rules.Count;
        if (Math.Abs(a.X - b.X) < 0.2 && Math.Abs(a.Y - b.Y) > 0.1) _rules.Add(new((a.X + b.X) / 2, Math.Min(a.Y, b.Y), (a.X + b.X) / 2, Math.Max(a.Y, b.Y)));
        else if (Math.Abs(a.Y - b.Y) < 0.2 && Math.Abs(a.X - b.X) > 0.1) _rules.Add(new(Math.Min(a.X, b.X), (a.Y + b.Y) / 2, Math.Max(a.X, b.X), (a.Y + b.Y) / 2));
        if(_rules.Count!=before) _paintedRuleBoxes.Add(new(_filledRuleBox ?? new(
            Math.Min(a.X,b.X)-_strokePaintRadius,Math.Min(a.Y,b.Y)-_strokePaintRadius,
            Math.Max(a.X,b.X)+_strokePaintRadius,Math.Max(a.Y,b.Y)+_strokePaintRadius),_operationOrder));
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
                _filledRuleBox=new(left,top,right,bottom);
                if (right - left <= 2.1 && bottom - top > 3) Line(((left + right) / 2, top), ((left + right) / 2, bottom));
                else if (bottom - top <= 2.1 && right - left > 3) Line((left, (top + bottom) / 2), (right, (top + bottom) / 2));
                _filledRuleBox=null;
            }
            if (stroke) for (var index = 1; index < path.Count; index++) { ConsumePaintWork(); Line(path[index - 1], path[index]); }
        }
        _paths.Clear();
        FinishClip();
    }
    public IReadOnlyList<PdfRule> Finish()
    { token.ThrowIfCancellationRequested(); if (_stack.Count > 0 || _pendingClip is not null) throw new PdfParseException("P12"); return _rules.ToArray(); }
}
