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
public sealed record PdfPathPaint(IReadOnlyList<IReadOnlyList<(double X, double Y)>> Paths, bool Fill, bool Stroke, double StrokeScale, bool Curved);
public sealed class PdfPathEngine(PdfDisplayTransform display, CancellationToken token = default, Action<PdfPathPaint>? painted = null, bool rectangularClips = false)
{
    private PdfMatrix _ctm = PdfMatrix.Identity;
    private readonly Stack<(PdfMatrix Matrix, PdfBox? Clip, double Width, int TextMode, int Join, double Miter)> _stack = new();
    private double _lineWidth = 1;
    private int _textMode;
    private int _lineJoin;
    private double _miterLimit = 10;
    private PdfBox? _clip, _pendingClip;
    private readonly List<List<(double X, double Y)>> _paths = [];
    private readonly List<PdfRule> _rules = [];
    private int _paintWork;
    private bool _curved;
    private void ConsumePaintWork()
    {
        if (++_paintWork > 1_000_000) throw new PdfParseException("limit");
        if (_paintWork % 128 == 0) token.ThrowIfCancellationRequested();
    }
    private static readonly IReadOnlyDictionary<string, int> Counts = new Dictionary<string, int>
    { ["j"] = 1, ["M"] = 1, ["w"] = 1, ["Tr"] = 1, ["W"] = 0, ["W*"] = 0, ["q"] = 0, ["Q"] = 0, ["cm"] = 6, ["m"] = 2, ["l"] = 2, ["re"] = 4, ["h"] = 0, ["S"] = 0, ["s"] = 0,
        ["f"] = 0, ["F"] = 0, ["f*"] = 0, ["B"] = 0, ["B*"] = 0, ["b"] = 0, ["b*"] = 0, ["n"] = 0, ["c"] = 6, ["v"] = 4, ["y"] = 4 };
    public static bool Supports(string operation) => Counts.ContainsKey(operation);
    private (double X, double Y) Point(double x, double y) { var p = _ctm.Point(x, y); return display.Point(p.X, p.Y); }
    private void Close() { if (_paths.Count > 0 && _paths[^1].Count > 0) _paths[^1].Add(_paths[^1][0]); }
    public void Operation(string operation, double[] numbers)
    {
        token.ThrowIfCancellationRequested();
        if (!Counts.TryGetValue(operation, out var count) || numbers.Length != count || !numbers.All(double.IsFinite)) throw new PdfParseException("P12");
        switch (operation)
        {
            case "q": if (_stack.Count >= 64) throw new PdfParseException("limit"); _stack.Push((_ctm,_clip,_lineWidth,_textMode,_lineJoin,_miterLimit)); break;
            case "Q": if (!_stack.TryPop(out var saved)) throw new PdfParseException("P12"); _ctm=saved.Matrix; _clip=saved.Clip; _lineWidth=saved.Width; _textMode=saved.TextMode; _lineJoin=saved.Join; _miterLimit=saved.Miter; break;
            case "j": if (numbers[0] != Math.Truncate(numbers[0]) || numbers[0] is < 0 or > 2) throw new PdfParseException("P01"); _lineJoin=(int)numbers[0]; break;
            case "M": if (numbers[0]<1) throw new PdfParseException("P01"); _miterLimit=numbers[0]; break;
            case "w": if (numbers[0]<0) throw new PdfParseException("P01"); _lineWidth=numbers[0]; break;
            case "Tr": if (numbers[0] != Math.Truncate(numbers[0]) || numbers[0] is < 0 or > 2) throw new PdfParseException("P01"); _textMode=(int)numbers[0]; break;
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
            case "W": case "W*":
                if (!rectangularClips || _curved || _paths.Count != 1) throw new PdfParseException("P01");
                var points=_paths[0]; var corners=points.Distinct().ToArray();
                if (corners.Length != 4 || points.Count is not 4 and not 5 || points.Count==5 && points[0]!=points[^1]) throw new PdfParseException("P01");
                var bounds=new PdfBox(corners.Min(p=>p.X),corners.Min(p=>p.Y),corners.Max(p=>p.X),corners.Max(p=>p.Y));
                if (!(bounds.Right>bounds.Left && bounds.Bottom>bounds.Top)
                    || corners.Any(p=>p.X!=bounds.Left && p.X!=bounds.Right || p.Y!=bounds.Top && p.Y!=bounds.Bottom)
                    || points.Zip(points.Skip(1).Append(points[0])).Any(p=>p.First.X!=p.Second.X && p.First.Y!=p.Second.Y)) throw new PdfParseException("P01");
                _pendingClip=_clip is { } old ? new(Math.Max(old.Left,bounds.Left),Math.Max(old.Top,bounds.Top),Math.Min(old.Right,bounds.Right),Math.Min(old.Bottom,bounds.Bottom)) : bounds;
                break;
            case "n": ApplyClip(); _paths.Clear(); _curved = false; break;
            case "c": case "v": case "y": _curved = true; if (_paths.Count > 0) _paths[^1].Clear(); break;
        }
        if (_paths.Count > 10000 || _paths.Count > 0 && _paths[^1].Count > 10000) throw new PdfParseException("limit");
    }
    private void ApplyClip() { if (_pendingClip is { } clip) { _clip=clip; _pendingClip=null; } }
    private double StrokeScale()
    {
        var sum=_ctm.A*_ctm.A+_ctm.B*_ctm.B+_ctm.C*_ctm.C+_ctm.D*_ctm.D;
        var determinant=_ctm.A*_ctm.D-_ctm.B*_ctm.C;
        var scale=Math.Sqrt((sum+Math.Sqrt(Math.Max(0,sum*sum-4*determinant*determinant)))/2);
        if (!double.IsFinite(scale)) throw new PdfParseException("P01");
        return scale;
    }
    private double ClipStrokeRadius()
    {
        // Device-pixel hairlines have no reliable user-space bound at arbitrary
        // downscales. They remain supported outside explicit clipping regions.
        if (_lineWidth == 0) throw new PdfParseException("P01");
        var radius=_lineWidth*StrokeScale()/2*(_lineJoin == 0 ? _miterLimit : 1);
        if (!double.IsFinite(radius)) throw new PdfParseException("P01");
        return radius;
    }
    public void RequireUnclipped(PdfGlyph glyph, bool sameInkFilledOutline = false)
    {
        if (_clip is not { } clip) return;
        // Only the reader's live paint state may certify an opaque same-ink
        // fill-plus-outline. Its complete fill must still fit inside the clip.
        var radius=_textMode == 0 || _textMode == 2 && _lineWidth > 0 && sameInkFilledOutline ? 0 : ClipStrokeRadius();
        if (!double.IsFinite(radius) || glyph.X-radius < clip.Left-0.001 || glyph.Y-radius < clip.Top-0.001
            || glyph.X+glyph.Width+radius > clip.Right+0.001 || glyph.Y+glyph.Height+radius > clip.Bottom+0.001) throw new PdfParseException("P01");
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
        var strokeRadius=stroke && _clip is not null ? ClipStrokeRadius() : 0;
        if (!double.IsFinite(strokeRadius) || _clip is { } clip && _paths.SelectMany(p=>p).Any(p=>
            p.X-strokeRadius<clip.Left-0.001 || p.X+strokeRadius>clip.Right+0.001 ||
            p.Y-strokeRadius<clip.Top-0.001 || p.Y+strokeRadius>clip.Bottom+0.001)) throw new PdfParseException("P01");
        if (painted is not null)
        {
            var scale = StrokeScale();
            painted(new(_paths.Select(path => (IReadOnlyList<(double X, double Y)>)path).ToArray(), fill, stroke, scale, _curved));
        }
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
        ApplyClip();
        _paths.Clear();
        _curved = false;
    }
    public IReadOnlyList<PdfRule> Finish()
    { token.ThrowIfCancellationRequested(); if (_stack.Count > 0) throw new PdfParseException("P12"); return _rules.ToArray(); }
}
