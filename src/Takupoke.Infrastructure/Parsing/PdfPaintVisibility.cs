namespace Takupoke.Infrastructure.Parsing;

/// <summary>Proves ordinary text contrast over opaque device-colour rectangles.
/// Uncertain paint, partial backgrounds and later overpainting reject the page;
/// no extracted letters are silently removed or replaced.</summary>
public sealed class PdfPaintVisibility(CancellationToken token = default)
{
    private readonly record struct Colour(double R, double G, double B)
    {
        public bool Differs(Colour other) => Math.Max(Math.Abs(R - other.R), Math.Max(Math.Abs(G - other.G), Math.Abs(B - other.B))) > 1.0 / 255;
    }
    private readonly record struct Bounds(double Left, double Top, double Right, double Bottom)
    {
        public bool Intersects(Bounds b) => Left < b.Right && Right > b.Left && Top < b.Bottom && Bottom > b.Top;
        public bool Contains(Bounds b) => Left <= b.Left && Right >= b.Right && Top <= b.Top && Bottom >= b.Bottom;
    }
    private sealed record State(Colour Fill, Colour Stroke, int Mode = 0, double Width = 1);
    private sealed record Paint(Bounds Bounds, Colour? Colour, int Order);
    private sealed record Text(Bounds Bounds, State State, int Order);
    private State _state = new(new(0, 0, 0), new(0, 0, 0));
    private readonly Stack<State> _stack = new();
    private readonly List<Paint> _paint = [];
    private readonly List<Text> _text = [];
    private int _order, _work;
    private static readonly IReadOnlyDictionary<string, int> Counts = new Dictionary<string, int>
    { ["q"] = 0, ["Q"] = 0, ["g"] = 1, ["G"] = 1, ["rg"] = 3, ["RG"] = 3, ["k"] = 4, ["K"] = 4, ["Tr"] = 1, ["w"] = 1 };
    public static bool Supports(string operation) => Counts.ContainsKey(operation);
    public static bool Unverified(string operation) => operation is "cs" or "CS" or "sc" or "SC" or "scn" or "SCN" or "sh" or "BI" or "ID" or "EI";
    private void Work()
    {
        if (++_work > 10_000_000) throw new PdfParseException("limit");
        if (_work % 128 == 0) token.ThrowIfCancellationRequested();
    }
    public void Operation(string operation, double[] values)
    {
        token.ThrowIfCancellationRequested();
        if (!Counts.TryGetValue(operation, out var count) || values.Length != count || !values.All(double.IsFinite)) throw new PdfParseException("P01");
        if (operation is "g" or "G" or "rg" or "RG" or "k" or "K")
        {
            if (values.Any(value => value is < 0 or > 1)) throw new PdfParseException("P01");
            var colour = values.Length switch
            {
                1 => new Colour(values[0], values[0], values[0]),
                3 => new Colour(values[0], values[1], values[2]),
                _ => new Colour(1 - Math.Min(1, values[0] + values[3]), 1 - Math.Min(1, values[1] + values[3]), 1 - Math.Min(1, values[2] + values[3]))
            };
            _state = operation is "g" or "rg" or "k" ? _state with { Fill = colour } : _state with { Stroke = colour };
            return;
        }
        switch (operation)
        {
            case "q": if (_stack.Count >= 64) throw new PdfParseException("limit"); _stack.Push(_state); break;
            case "Q": if (!_stack.TryPop(out var saved)) throw new PdfParseException("P01"); _state = saved; break;
            case "Tr": if (values[0] is not 0 and not 1 and not 2) throw new PdfParseException("P01"); _state = _state with { Mode = (int)values[0] }; break;
            case "w": if (values[0] < 0) throw new PdfParseException("P01"); _state = _state with { Width = values[0] }; break;
        }
    }
    public void Glyph(PdfGlyph glyph)
    {
        if (_text.Count >= 100000) throw new PdfParseException("limit");
        _text.Add(new(new(glyph.X, glyph.Y, glyph.X + glyph.Width, glyph.Y + glyph.Height), _state, _order++));
    }
    public void Paths(PdfPathPaint paint)
    {
        token.ThrowIfCancellationRequested();
        if (paint.Curved || !double.IsFinite(paint.StrokeScale)) throw new PdfParseException("P01");
        var rectangles = new List<Bounds>();
        foreach (var path in paint.Paths)
        {
            Work();
            if (path.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y))) throw new PdfParseException("P01");
            if (paint.Fill && path.Count >= 3)
            {
                var corners = path.Distinct().ToArray();
                var left = path.Min(p => p.X); var right = path.Max(p => p.X); var top = path.Min(p => p.Y); var bottom = path.Max(p => p.Y);
                if (right > left && bottom > top)
                {
                    // Overlapping subpaths can create holes under the fill rule.
                    // A bounding box is not proof of an opaque rectangle.
                    if (path.Count is not 4 and not 5 || path.Count == 5 && path[0] != path[^1]
                        || corners.Length != 4 || corners.Any(p => p.X != left && p.X != right || p.Y != top && p.Y != bottom)) throw new PdfParseException("P01");
                    for (var index = 0; index < path.Count; index++)
                    {
                        var next = path[(index + 1) % path.Count];
                        if (path[index].X != next.X && path[index].Y != next.Y) throw new PdfParseException("P01");
                    }
                    var rectangle = new Bounds(left, top, right, bottom);
                    foreach (var previous in rectangles) { Work(); if (previous.Intersects(rectangle)) throw new PdfParseException("P01"); }
                    rectangles.Add(rectangle);
                }
            }
        }
        foreach (var rectangle in rectangles) Add(new(rectangle, _state.Fill, _order++));
        if (!paint.Stroke) return;
        var radius = Math.Max(1, _state.Width) * paint.StrokeScale / 2;
        foreach (var path in paint.Paths)
            for (var index = 1; index < path.Count; index++)
            {
                Work(); var a = path[index - 1]; var b = path[index];
                if (a.X != b.X && a.Y != b.Y) throw new PdfParseException("P01");
                Add(new(new(Math.Min(a.X, b.X) - radius, Math.Min(a.Y, b.Y) - radius,
                    Math.Max(a.X, b.X) + radius, Math.Max(a.Y, b.Y) + radius), null, _order++));
            }
    }
    private void Add(Paint paint)
    {
        if (_paint.Count >= 100000) throw new PdfParseException("limit");
        _paint.Add(paint);
    }
    public void Validate()
    {
        token.ThrowIfCancellationRequested();
        if (_stack.Count != 0) throw new PdfParseException("P01");
        foreach (var text in _text)
        {
            var background = new Colour(1, 1, 1);
            for (var index = _paint.Count - 1; index >= 0; index--)
            {
                Work(); var paint = _paint[index];
                if (!paint.Bounds.Intersects(text.Bounds)) continue;
                if (paint.Order > text.Order || paint.Colour is not { } colour || !paint.Bounds.Contains(text.Bounds)) throw new PdfParseException("P01");
                background = colour; break;
            }
            var visible = text.State.Mode switch
            {
                0 => text.State.Fill.Differs(background),
                1 => text.State.Stroke.Differs(background),
                // A same-colour stroke can cover a contrasting thin fill.
                // Without glyph-outline rasterisation, require the outline.
                _ => text.State.Stroke.Differs(background)
            };
            if (!visible) throw new PdfParseException("P01");
        }
    }
}
