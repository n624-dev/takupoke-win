using System.Text;

namespace Takupoke.Infrastructure.Parsing;

public readonly record struct PdfMatrix(double A = 1, double B = 0, double C = 0, double D = 1, double Tx = 0, double Ty = 0)
{
    public static PdfMatrix Identity => new(1, 0, 0, 1, 0, 0);
    public (double X, double Y) Point(double x, double y) => (A * x + C * y + Tx, B * x + D * y + Ty);
    public PdfMatrix FollowedBy(PdfMatrix next) => new(next.A * A + next.C * B, next.B * A + next.D * B, next.A * C + next.C * D, next.B * C + next.D * D,
        next.A * Tx + next.C * Ty + next.Tx, next.B * Tx + next.D * Ty + next.Ty);
    public PdfMatrix Translate(double x, double y) => this with { Tx = Tx + A * x + C * y, Ty = Ty + B * x + D * y };
    public static PdfMatrix From(double[] numbers) => numbers.Length == 6 && numbers.All(double.IsFinite)
        ? new(numbers[0], numbers[1], numbers[2], numbers[3], numbers[4], numbers[5]) : throw new PdfParseException("P01");
}
public sealed record PdfFont(PdfUnicodeMap Map, IReadOnlyDictionary<int, double> Widths, double DefaultWidth, double Ascent, double Descent)
{
    internal string Resource { get; init; } = "";
    internal Func<int, Takupoke.Core.Recovery.RecoveryFontEvidence>? RecoveryEvidence { get; init; }
    public PdfFont Validate()
    {
        if (Map.CodeBytes is not 1 and not 2 || Map.Values.Count is < 1 or > 65536 || !new[] { DefaultWidth, Ascent, Descent }.All(double.IsFinite)
            || DefaultWidth < 0 || Ascent <= Descent || Widths.Values.Any(w => !double.IsFinite(w) || w < 0)) throw new PdfParseException("P01");
        return this;
    }
}
public sealed class PdfTextEngine(CancellationToken cancellationToken = default, bool traceForRecovery = false)
{
    private sealed record State(PdfMatrix Ctm, PdfFont? Font = null, double Size = 0, double Spacing = 0, double WordSpacing = 0, double Scale = 1, double Leading = 0, double Rise = 0, Lazy<PdfFont>? DeferredFont = null);
    private State _state = new(PdfMatrix.Identity);
    private readonly Stack<State> _stack = new();
    private PdfMatrix _matrix = PdfMatrix.Identity, _lineMatrix = PdfMatrix.Identity;
    private bool _inText;
    private int _line, _order, _operations, _units;
    private readonly StringBuilder _drawn = new();
    private readonly List<PdfGlyph> _glyphs = [];
    private readonly List<PdfNativeGlyphTrace> _drawingTrace = [];
    internal bool HasFontRecovery => _recoveryGlyphs.Any(g => g.FontEvidence is not null);
    // Strict keeps its existing non-whitespace stream. Recovery additionally
    // retains explicitly drawn whitespace; absent geometry makes capture partial.
    private readonly List<PdfGlyph> _recoveryGlyphs = [];
    internal IReadOnlyList<PdfGlyph> RecoveryGlyphs => _recoveryGlyphs;
    internal bool RecoveryComplete { get; private set; } = true;
    private static readonly IReadOnlyDictionary<string, int> Counts = new Dictionary<string, int>
    { ["q"] = 0, ["Q"] = 0, ["cm"] = 6, ["BT"] = 0, ["ET"] = 0, ["Tm"] = 6, ["Td"] = 2, ["TD"] = 2, ["T*"] = 0,
        ["Tc"] = 1, ["Tw"] = 1, ["Tz"] = 1, ["TL"] = 1, ["Ts"] = 1, ["Tr"] = 1 };
    public static bool Supports(string operation) => Counts.ContainsKey(operation);
    public void Operation(string operation, params double[] numbers)
    {
        if (++_operations > 1_000_000) throw new PdfParseException("limit"); cancellationToken.ThrowIfCancellationRequested();
        if (!Counts.TryGetValue(operation, out var count) || numbers.Length != count || !numbers.All(double.IsFinite)) throw new PdfParseException("P01");
        switch (operation)
        {
            case "q": if (_stack.Count >= 64) throw new PdfParseException("limit"); _stack.Push(_state); break;
            case "Q": if (!_stack.TryPop(out var saved)) throw new PdfParseException("P01"); _state = saved; _line++; break;
            case "cm": _state = _state with { Ctm = PdfMatrix.From(numbers).FollowedBy(_state.Ctm) }; _line++; break;
            case "BT": if (_inText) throw new PdfParseException("P01"); _inText = true; _matrix = PdfMatrix.Identity; _lineMatrix = _matrix; _line++; break;
            case "ET": if (!_inText) throw new PdfParseException("P01"); _inText = false; break;
            case "Tm": if (!_inText) throw new PdfParseException("P01"); _matrix = PdfMatrix.From(numbers); _lineMatrix = _matrix; _line++; break;
            case "Td": case "TD": case "T*":
                if (!_inText) throw new PdfParseException("P01");
                if (operation == "TD") _state = _state with { Leading = -numbers[1] };
                _lineMatrix = _lineMatrix.Translate(operation == "T*" ? 0 : numbers[0], operation == "T*" ? -_state.Leading : numbers[1]); _matrix = _lineMatrix; _line++; break;
            case "Tc": _state = _state with { Spacing = numbers[0] }; break;
            case "Tw": _state = _state with { WordSpacing = numbers[0] }; break;
            case "Tz": _state = _state with { Scale = numbers[0] / 100 }; break;
            case "TL": _state = _state with { Leading = numbers[0] }; break;
            case "Ts": _state = _state with { Rise = numbers[0] }; _line++; break;
            case "Tr": if (numbers[0] is not 0 and not 1 and not 2) throw new PdfParseException("P01"); break;
        }
    }
    public void SetFont(PdfFont font, double size)
    { if (!double.IsFinite(size) || size <= 0) throw new PdfParseException("P01"); _state = _state with { Font = font.Validate(), DeferredFont = null, Size = size }; }
    // Selecting an unused font need not supply text mapping. Every nonempty
    // showing operation still resolves and validates that exact selected font.
    internal void SelectFont(Lazy<PdfFont> font, double size)
    { if (!double.IsFinite(size) || size <= 0) throw new PdfParseException("P01"); _state = _state with { Font = null, DeferredFont = font, Size = size }; }
    public void Adjust(double amount)
    { if (!_inText || !double.IsFinite(amount)) throw new PdfParseException("P01"); _matrix = _matrix.Translate(-amount / 1000 * _state.Size * _state.Scale, 0); }
    public void Show(ReadOnlySpan<byte> bytes)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_inText || _state.Font is null && _state.DeferredFont is null || _state.Size <= 0 || _state.Scale <= 0) throw new PdfParseException("P01");
        if (bytes.IsEmpty) return;
        var font = _state.Font ?? _state.DeferredFont!.Value;
        if (bytes.Length % font.Map.CodeBytes != 0 || _order + bytes.Length / font.Map.CodeBytes > 100000) throw new PdfParseException("P01");
        for (var offset = 0; offset < bytes.Length; offset += font.Map.CodeBytes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cid = font.Map.CodeBytes == 1 ? bytes[offset] : bytes[offset] * 256 + bytes[offset + 1];
            if (!font.Map.Values.TryGetValue(cid, out var text) || text.Length == 0 || text.Any(char.IsControl) || _units + text.Length > 100000) throw new PdfParseException("P01");
            var width = font.Widths.GetValueOrDefault(cid, font.DefaultWidth) / 1000 * _state.Size;
            var total = _matrix.FollowedBy(_state.Ctm); var bottom = font.Descent / 1000 * _state.Size + _state.Rise; var top = font.Ascent / 1000 * _state.Size + _state.Rise;
            var points = new[] { total.Point(0, bottom), total.Point(width * _state.Scale, bottom), total.Point(0, top), total.Point(width * _state.Scale, top) };
            if (points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y) || Math.Abs(p.X) >= 10_000_000 || Math.Abs(p.Y) >= 10_000_000)) throw new PdfParseException("P01");
            var x = points.Min(p => p.X); var y = points.Min(p => p.Y); var right = points.Max(p => p.X); var upper = points.Max(p => p.Y);
            var validGeometry = width > 0 && right > x && upper > y;
            var evidence = font.RecoveryEvidence?.Invoke(cid);
            if(traceForRecovery) {
                UglyToad.PdfPig.Core.PdfPoint Point(double px, double py) { var p = total.Point(px, py); return new(p.X, p.Y); }
                _drawingTrace.Add(new(font.Resource, cid, text, evidence is null, Point(0, _state.Rise),
                    Point(_state.Size * _state.Scale, _state.Rise), Point(0, _state.Rise + _state.Size)));
            }
            if (validGeometry)
                _recoveryGlyphs.Add(new(text, x, y, right-x, upper-y, _line, _order) { FontEvidence = evidence });
            else RecoveryComplete = false;
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (!validGeometry) throw new PdfParseException("P01");
                _glyphs.Add(new(text, x, y, right - x, upper - y, _line, _order) { FontEvidence = evidence });
            }
            _drawn.Append(text); _units += text.Length; _order++;
            var word = font.Map.CodeBytes == 1 && cid == 32 ? _state.WordSpacing : 0;
            _matrix = _matrix.Translate((width + _state.Spacing + word) * _state.Scale, 0);
        }
    }
    internal IReadOnlyList<PdfGlyph> FinishRecovery(IReadOnlyList<PdfNativeGlyphTrace> native)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!traceForRecovery || _inText || _stack.Count != 0 || _glyphs.Count == 0 || !RecoveryComplete || native.Count != _drawingTrace.Count)
            throw new PdfParseException("P01");
        static bool Same(UglyToad.PdfPig.Core.PdfPoint a, UglyToad.PdfPig.Core.PdfPoint b) =>
            double.IsFinite(a.X) && double.IsFinite(a.Y) && double.IsFinite(b.X) && double.IsFinite(b.Y)
            && Math.Abs(a.X-b.X) <= 1e-6 && Math.Abs(a.Y-b.Y) <= 1e-6;
        for (var index=0; index<native.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var actual=native[index]; var drawn=_drawingTrace[index];
            if (actual.Resource != drawn.Resource || actual.Code != drawn.Code
                || !drawn.UnicodeKnown && !string.IsNullOrWhiteSpace(drawn.Text) && !actual.Drawable
                || (drawn.UnicodeKnown || actual.UnicodeKnown) && actual.Text != drawn.Text
                || !Same(actual.Origin,drawn.Origin) || !Same(actual.Horizontal,drawn.Horizontal) || !Same(actual.Vertical,drawn.Vertical))
                throw new PdfParseException("P01");
            if(!drawn.UnicodeKnown && actual.InkBounds is {} ink)
            {
                var glyph=_recoveryGlyphs[index];
                var corners=new[]{ink.BottomLeft,ink.TopLeft,ink.TopRight,ink.BottomRight};
                if(corners.Any(p=>!double.IsFinite(p.X)||!double.IsFinite(p.Y)))throw new PdfParseException("P01");
                var left=Math.Min(glyph.X,corners.Min(p=>p.X));var bottom=Math.Min(glyph.Y,corners.Min(p=>p.Y));
                var right=Math.Max(glyph.X+glyph.Width,corners.Max(p=>p.X));var top=Math.Max(glyph.Y+glyph.Height,corners.Max(p=>p.Y));
                _recoveryGlyphs[index]=glyph with { X=left,Y=bottom,Width=right-left,Height=top-bottom };
            }
        }
        return _glyphs.Select(g=>g.FontEvidence is not null?_recoveryGlyphs[g.SourceOrder!.Value]:g).ToArray();
    }
    public IReadOnlyList<PdfGlyph> Finish(string expectedText)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_inText || _stack.Count != 0 || _glyphs.Count == 0 || expectedText.Length > 100000) throw new PdfParseException("P01");
        static Dictionary<int, int> Content(string text) => text.Normalize(NormalizationForm.FormC).EnumerateRunes().Where(r => !Rune.IsWhiteSpace(r))
            .GroupBy(r => r.Value).ToDictionary(g => g.Key, g => g.Count());
        var drawn = Content(_drawn.ToString()); var expected = Content(expectedText);
        if (drawn.Count != expected.Count || drawn.Any(p => expected.GetValueOrDefault(p.Key) != p.Value)) throw new PdfParseException("P01");
        return _glyphs.ToArray();
    }
}
