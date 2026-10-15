using System.Globalization;
using System.Text;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Graphics.Operations;
using UglyToad.PdfPig.Graphics.Operations.General;
using UglyToad.PdfPig.Graphics.Operations.TextShowing;
using UglyToad.PdfPig.Graphics.Operations.TextState;
using UglyToad.PdfPig.Parser.Parts;
using UglyToad.PdfPig.Tokens;

namespace Takupoke.Infrastructure.Parsing;

/// <summary>Opens bytes locally. Ordinary timetables use a separate, strict drawing interpreter and verify its text against PdfPig.</summary>
public static partial class PdfPigLayoutReader
{
    public static IReadOnlyList<PdfPageLayout> Read(byte[] bytes, MaterialKind kind, CancellationToken token = default, RecoveryReadCapture? capture = null)
        => ReadCore(bytes, kind, token, capture, null);

    private static IReadOnlyList<PdfPageLayout> ReadCore(byte[] bytes, MaterialKind kind, CancellationToken token,
        RecoveryReadCapture? capture, FontRecoveryContext? fontRecovery, bool boundedVisibleUnicode = false)
    {
        capture?.Reset();
        if (bytes.Length is < 1 or > 50 * 1024 * 1024 || !bytes.AsSpan(0, Math.Min(bytes.Length, 5)).SequenceEqual("%PDF-"u8) || kind == MaterialKind.Changes) throw new PdfParseException("unreadable");
        try
        {
            token.ThrowIfCancellationRequested();
            using var document = PdfDocument.Open(bytes, new ParsingOptions { UseLenientParsing = false, SkipMissingFonts = false, MaxStackDepth = 64, UseActualText = false });
            if (fontRecovery is not null || boundedVisibleUnicode) document.AddPageFactory<PdfNativeTraceInputs, PdfNativeTraceFactory>();
            if (document.IsEncrypted || document.NumberOfPages < 1) throw new PdfParseException("unreadable");
            if (document.NumberOfPages > 12) throw new PdfParseException("limit");
            capture?.Begin(document.NumberOfPages);
            var output = new List<PdfPageLayout>();
            for (var number = 1; number <= document.NumberOfPages; number++)
            {
                token.ThrowIfCancellationRequested();
                var page = document.GetPage(number); var media = page.MediaBox.Bounds;
                if (page.CropBox.Bounds != media) throw new PdfParseException("P01", number);
                if (page.Rotation.Value is not 0 and not 90 and not 180 and not 270) throw new PdfParseException("P02", number);
                if (page.Operations.Count > 1_000_000 || page.Letters.Count > 100000 || page.Text.Length > 100000) throw new PdfParseException("limit", number);
                if (page.Letters.Count == 0) throw new PdfParseException("raster", number);
                var transform = new PdfDisplayTransform(media.Left, media.Bottom, media.Width, media.Height, page.Rotation.Value);
                var paths = new PdfPathEngine(transform, token);
                var resources = Resources(document, page.Dictionary);
                var colourSpaces = Get(resources, "ColorSpace") is { } colours ? Resolve<DictionaryToken>(document, colours) : null;
                var visibility = new VisibilityState(
                    colourSpaces?.Data.ContainsKey("DefaultGray") != true,
                    colourSpaces?.Data.ContainsKey("DefaultRGB") != true,
                    colourSpaces?.Data.ContainsKey("DefaultCMYK") != true, boundedVisibleUnicode);
                var text = kind == MaterialKind.Timetable || fontRecovery is not null || boundedVisibleUnicode
                    ? new PdfTextEngine(token,traceForRecovery:fontRecovery is not null || boundedVisibleUnicode) : null;
                var textPaint = new Dictionary<int,double>();
                var textColours = new Dictionary<int,PdfPathEngine.PaintColor>();
                var textOperations = new Dictionary<int,int>();var operationOrder=0;
                var bareInk = new Dictionary<int,PdfBox>();
                var textClips = new Dictionary<int,PdfBox?>();
                var markedDepth = 0;
                IReadOnlyList<PdfGlyph>? paintedInk = null;
                var fonts = new Dictionary<string, Lazy<PdfFont>>();
                foreach (var operation in page.Operations)
                {
                    token.ThrowIfCancellationRequested();
                    if (boundedVisibleUnicode) paths.SetOperationOrder(++operationOrder);
                    var name = operation.Operator;
                    visibility.Operation(operation, number, paths);
                    if (name == "Do") throw new PdfParseException("P12", number);
                    if (name == "BDC")
                    {
                        if (!boundedVisibleUnicode || operation is not UglyToad.PdfPig.Graphics.Operations.MarkedContent.BeginMarkedContentWithProperties marked ||
                            !SafeMarkedProperties(marked.Name,marked.PropertyDictionaryName,marked.Properties) || ++markedDepth>64) throw new PdfParseException("P01",number);
                    }
                    if (name == "BMC")
                    {
                        // Plain Artifact marking changes no characters or visibility.
                        using var stream = new MemoryStream(); operation.Write(stream);
                        var words = Encoding.ASCII.GetString(stream.ToArray()).Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
                        if (!boundedVisibleUnicode || words is not ["/Artifact", "BMC"] || ++markedDepth>64) throw new PdfParseException("P01",number);
                    }
                    if (name == "EMC" && boundedVisibleUnicode && --markedDepth<0) throw new PdfParseException("P01",number);
                    if (name is "W" or "W*")
                    {
                        if (!boundedVisibleUnicode) throw new PdfParseException("P01",number);
                        paths.IntersectPendingRectangleClip();
                    }
                    if (PdfPathEngine.Supports(name) || text is not null && PdfTextEngine.Supports(name))
                    {
                        var numbers = Numbers(operation);
                        if (boundedVisibleUnicode && name is "S" or "s" or "B" or "B*" or "b" or "b*")
                            paths.SetStrokePaintRadius(visibility.StrokePaintRadius(number));
                        if (PdfPathEngine.Supports(name))
                        {
                            if (boundedVisibleUnicode && (visibility.FillWhite || visibility.SupportedBackground && !paths.PendingFillIsThinRules) && name is "f" or "F" or "f*" or "B" or "B*" or "b" or "b*") paths.RecordBackgroundFill(visibility.FillColor);
                            var paint = (visibility.FillWhite || boundedVisibleUnicode && visibility.SupportedBackground && !paths.PendingFillIsThinRules) ? name switch { "f" or "F" or "f*" => "n", "B" or "B*" => "S", "b" or "b*" => "s", _ => name } : name;
                            paths.Operation(paint, numbers);
                        }
                        if (text is not null && PdfTextEngine.Supports(name)) text.Operation(name, numbers);
                    }
                    // Visibility-affecting graphics state applies to every document kind.
                    if (name == "gs")
                    {
                        using var serialized = new MemoryStream(); operation.Write(serialized);
                        var items = Encoding.ASCII.GetString(serialized.ToArray()).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                        if (items.Length != 2 || !items[0].StartsWith('/')) throw new PdfParseException("P01", number);
                        var state = Resource(document, resources, "ExtGState", items[0][1..]);
                        if (boundedVisibleUnicode && state.Data.Keys.Any(k => k is not ("Type" or "Font" or "SMask" or "TR" or "TR2" or "BM" or "ca" or "CA" or "LW" or "D" or "LC" or "LJ" or "ML")))
                            throw new PdfParseException("P01", number);
                        if (boundedVisibleUnicode && (Number(document,state,"ML",10) is < 1 or > 10 ||
                            Number(document,state,"LC",0) is not (0 or 1 or 2) || Number(document,state,"LJ",0) is not (0 or 1 or 2))) throw new PdfParseException("P01", number);
                        if (boundedVisibleUnicode) visibility.SetStrokeStyle(
                            state.Data.ContainsKey("LJ") ? (int)Number(document,state,"LJ") : null,
                            state.Data.ContainsKey("LC") ? (int)Number(document,state,"LC") : null,
                            state.Data.ContainsKey("ML") ? Number(document,state,"ML") : null);
                        if (new[] { "Font", "SMask", "TR", "TR2" }.Any(state.Data.ContainsKey) || state.Data.ContainsKey("BM") && Name(document, state, "BM") != "Normal" || Number(document, state, "ca", 1) != 1 || Number(document, state, "CA", 1) != 1) throw new PdfParseException("P01", number);
                        if (state.Data.ContainsKey("LW")) visibility.SetLineWidth(Number(document, state, "LW"), number);
                        if (state.Data.ContainsKey("D"))
                        {
                            var dash = Resolve<ArrayToken>(document, Get(state, "D"));
                            if (dash.Length != 2 || Resolve<ArrayToken>(document, dash[0]).Length != 0 || !double.IsFinite(Resolve<NumericToken>(document, dash[1]).Data))
                                throw new PdfParseException("P01", number);
                        }
                    }
                    if (text is null) continue;
                    var beforeText = text.RecoveryGlyphs.Count;
                    switch (operation)
                    {
                        case SetFontAndSize font:
                            if (!fonts.TryGetValue(font.Font.Data, out var decoded))
                            {
                                if (fonts.Count >= 128) throw new PdfParseException("limit", number);
                                var dictionary = Resource(document, resources, "Font", font.Font.Data);
                                decoded = new Lazy<PdfFont>(() => ReadFont(document, dictionary, token, fontRecovery, font.Font.Data));
                                fonts.Add(font.Font.Data, decoded);
                            }
                            text.SelectFont(decoded, font.Size); break;
                        case ShowText show: text.Show(Bytes(show.Text, show.Bytes)); break;
                        case ShowTextsWithPositioning show:
                            if (show.Array.Count > 100000) throw new PdfParseException("limit", number);
                            foreach (var item in show.Array)
                                switch (item)
                                { case NumericToken adjust: text.Adjust(adjust.Data); break; case StringToken s: text.Show(s.GetBytes()); break; case HexToken h: text.Show(h.Bytes); break; default: throw new PdfParseException("P01", number); }
                            break;
                        case MoveToNextLineShowText show: text.Operation("T*"); text.Show(Bytes(show.Text, show.Bytes)); break;
                        case MoveToNextLineShowTextWithSpacing show:
                            text.Operation("Tw", show.WordSpacing); text.Operation("Tc", show.CharacterSpacing); text.Operation("T*"); text.Show(Bytes(show.Text, show.Bytes)); break;
                    }
                    if (boundedVisibleUnicode)
                        for (var index = beforeText; index < text.RecoveryGlyphs.Count; index++)
                        {
                            textPaint.Add(text.RecoveryGlyphs[index].SourceOrder!.Value, visibility.TextPaintMargin(number));
                            textColours.Add(text.RecoveryGlyphs[index].SourceOrder!.Value,visibility.FillColor);
                            textOperations.Add(text.RecoveryGlyphs[index].SourceOrder!.Value,operationOrder);
                            textClips.Add(text.RecoveryGlyphs[index].SourceOrder!.Value,paths.Clip);
                        }

                }
                if (boundedVisibleUnicode && markedDepth!=0) throw new PdfParseException("P01",number);
                IReadOnlyList<PdfGlyph> rawGlyphs;
                if (text is { HasFontRecovery: true } || boundedVisibleUnicode)
                {
                    // First bounded drawing pass above has already rejected forms,
                    // clipping, hidden text, replacements and unsupported paint.
                    var input = document.GetPage<PdfNativeTraceInputs>(number);
                    input.Factory.ResourceStore.LoadResourceDictionary(resources);
                    try
                    {
                        var native = new PdfNativeTraceProcessor(number, input, token, text!.RecoveryGlyphs.Where(g=>g.FontEvidence is not null).Select(g=>g.FontEvidence!.Resource).ToHashSet(),
                            includeKnownInk: boundedVisibleUnicode).Process(number, input.Operations);
                        // Initial native page rotation/origin is undone before
                        // comparing effective drawing bases in raw PDF space.
                        UglyToad.PdfPig.Core.PdfPoint Raw(UglyToad.PdfPig.Core.PdfPoint p) => page.Rotation.Value switch
                        {
                            90 => new(media.Width-p.Y+media.Left,p.X+media.Bottom),
                            180 => new(media.Width-p.X+media.Left,media.Height-p.Y+media.Bottom),
                            270 => new(p.Y+media.Left,media.Height-p.X+media.Bottom),
                            _ => new(p.X+media.Left,p.Y+media.Bottom)
                        };
                        var observed = native.Select(g => g with
                            { Origin=Raw(g.Origin), Horizontal=Raw(g.Horizontal), Vertical=Raw(g.Vertical),
                                InkBounds=g.InkBounds is {} ink?new UglyToad.PdfPig.Core.PdfRectangle(Raw(ink.TopLeft),Raw(ink.TopRight),Raw(ink.BottomLeft),Raw(ink.BottomRight)):null }).ToArray();
                        rawGlyphs = text.FinishRecovery(observed);
                        if (boundedVisibleUnicode)
                        {
                            var ink = new List<PdfGlyph>(); var enlarged = new List<PdfGlyph>();
                            foreach (var glyph in rawGlyphs)
                            {
                                token.ThrowIfCancellationRequested();
                                var order = glyph.SourceOrder!.Value;
                                var trace = observed[order];
                                if (!trace.UnicodeKnown || !trace.Drawable || trace.InkBounds is not { Width: > 0, Height: > 0 } bounds || !textPaint.TryGetValue(order,out var margin))
                                    throw new PdfParseException("P01", number);
                                var left = bounds.Left-margin; var bottom = bounds.Bottom-margin;
                                var right = bounds.Right+margin; var top = bounds.Top+margin;
                                if (!new[] { left,bottom,right,top }.All(double.IsFinite)) throw new PdfParseException("P01",number);
                                var original=transform.Glyph(glyph with { X=bounds.Left,Y=bounds.Bottom,Width=bounds.Width,Height=bounds.Height });
                                var bare=new PdfBox(original.X,original.Y,original.X+original.Width,original.Y+original.Height);
                                var painted = transform.Glyph(glyph with { X=left,Y=bottom,Width=right-left,Height=top-bottom });
                                var clipped=false;
                                if (textClips[order] is {} clip)
                                {
                                    var visible=new PdfBox(Math.Max(bare.Left,clip.Left),Math.Max(bare.Top,clip.Top),Math.Min(bare.Right,clip.Right),Math.Min(bare.Bottom,clip.Bottom));
                                    if (visible.Right<=visible.Left || visible.Bottom<=visible.Top) throw new PdfParseException("P01",number);
                                    clipped=visible!=bare;bare=visible;
                                    var px=Math.Max(painted.X,clip.Left);var py=Math.Max(painted.Y,clip.Top);
                                    painted=painted with { X=px,Y=py,Width=Math.Min(painted.X+painted.Width,clip.Right)-px,Height=Math.Min(painted.Y+painted.Height,clip.Bottom)-py };
                                }
                                bareInk.Add(order,bare);
                                ink.Add(painted);
                                enlarged.Add(glyph with {RequiresVisualReview=clipped});
                            }
                            rawGlyphs=enlarged; paintedInk=ink;
                        }
                    }
                    finally { input.Factory.ResourceStore.UnloadResourceDictionary(); }
                }
                else rawGlyphs = text is not null ? text.Finish(page.Text) : [];
                IReadOnlyList<PdfGlyph> glyphs = text is not null ? rawGlyphs.Select(transform.Glyph).ToArray() : SpecialGlyphs(page, transform, token);
                var physicalInk=paintedInk?.ToDictionary(g=>g.SourceOrder!.Value,g=>new PdfBox(g.X,g.Y,g.X+g.Width,g.Y+g.Height));
                if(physicalInk is not null)glyphs=glyphs.Select(g=>g with {InkBounds=physicalInk.TryGetValue(g.SourceOrder!.Value,out var box) ? box : null}).ToArray();
                var expandedGlyphs = boundedVisibleUnicode ? rawGlyphs.ToDictionary(g => g.SourceOrder!.Value) : null;
                var recoveryGlyphs = text is not null ? text.RecoveryGlyphs.Select(g => transform.Glyph(
                    expandedGlyphs?.GetValueOrDefault(g.SourceOrder!.Value) ?? g)).Select(g=>g with {InkBounds=physicalInk is not null && physicalInk.TryGetValue(g.SourceOrder!.Value,out var box) ? box : null}).ToArray() : glyphs;
                capture?.Record(number, RecoveryInputState.Partial, new PdfPageLayout(transform.Width, transform.Height, recoveryGlyphs, []));
                var layout = new PdfPageLayout(transform.Width, transform.Height, glyphs, paths.Finish());
                layout.ValidateViewport(number);
                (PdfBox Rule,PdfBox Ink,int? Order)? conflict=null;
                var requiresReview = new HashSet<int>();
                var hidden = new HashSet<int>();
                if (paintedInk is not null ? PaintedRulesOverlapInk(paths.PaintedRuleBoxes,paintedInk,bareInk,textOperations,textColours,token,requiresReview,hidden,out conflict) : RulesOverlapText(layout.Lines,glyphs,token))
                {
                    var error=new PdfParseException("P01", number);
                    if(conflict is {} c) { error.Data["VisibleRuleBox"]=c.Rule;error.Data["VisibleInkBox"]=c.Ink;error.Data["VisibleSourceOrder"]=c.Order;
                        error.Data["VisibleTextPaintMargin"]=textPaint.GetValueOrDefault(c.Order??-1); }
                    throw error;
                }
                if (hidden.Count>0)
                {
                    bool Visible(PdfGlyph g) => g.SourceOrder is not int order || !hidden.Contains(order);
                    recoveryGlyphs=recoveryGlyphs.Where(Visible).ToArray();
                    glyphs=glyphs.Where(Visible).ToArray();layout=layout with { Glyphs=layout.Glyphs.Where(Visible).ToArray() };
                }
                if (requiresReview.Count > 0)
                {
                    PdfGlyph Mark(PdfGlyph g) => g with { RequiresVisualReview = g.RequiresVisualReview || g.SourceOrder is int order && requiresReview.Contains(order) };
                    recoveryGlyphs = recoveryGlyphs.Select(Mark).ToArray();
                    layout = layout with { Glyphs = layout.Glyphs.Select(Mark).ToArray() };
                }
                capture?.Record(number, glyphs.Count == 0 ? RecoveryInputState.RasterOnly : text is { RecoveryComplete: false } ? RecoveryInputState.Partial : RecoveryInputState.Complete,
                    layout with { Glyphs = recoveryGlyphs }, boundedVisibleUnicode);
                layout.Validate(number); output.Add(layout);
            }
            capture?.Finish(); return output;
        }
        catch (PdfParseException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch { throw new PdfParseException("unreadable"); }
    }
    private static byte[] Bytes(string? literal, ReadOnlyMemory<byte> bytes)
    { if (literal is null) return bytes.ToArray(); if (literal.Any(c => c > 255)) throw new PdfParseException("P01"); return Encoding.Latin1.GetBytes(literal); }
    private static bool RulesOverlapText(IReadOnlyList<PdfRule> rules, IReadOnlyList<PdfGlyph> glyphs, CancellationToken token)
    {
        // Search only rules crossing each glyph's coordinate interval, rather
        // than comparing every glyph with every rule on large pages.
        var vertical = rules.Where(r => r.Vertical).OrderBy(r => r.X1).ToArray();
        var horizontal = rules.Where(r => r.Horizontal).OrderBy(r => r.Y1).ToArray();
        var work = 0;
        static int LowerBound(PdfRule[] items, double minimum, bool isVertical)
        {
            var low = 0; var high = items.Length;
            while (low < high)
            { var middle = low + (high - low) / 2; if ((isVertical ? items[middle].X1 : items[middle].Y1) < minimum) low = middle + 1; else high = middle; }
            return low;
        }
        foreach (var glyph in glyphs)
        {
            token.ThrowIfCancellationRequested();
            foreach (var isVertical in new[] { true, false })
            {
                var items = isVertical ? vertical : horizontal;
                var minimum = (isVertical ? glyph.X : glyph.Y) - 1.5;
                var maximum = (isVertical ? glyph.X + glyph.Width : glyph.Y + glyph.Height) + 1.5;
                for (var index = LowerBound(items, minimum, isVertical); index < items.Length; index++)
                {
                    var rule = items[index]; if ((isVertical ? rule.X1 : rule.Y1) >= maximum) break;
                    if (++work > 1_000_000) throw new PdfParseException("limit");
                    if (work % 128 == 0) token.ThrowIfCancellationRequested();
                    if (isVertical ? rule.Y1 - 1.5 < glyph.Y + glyph.Height && glyph.Y < rule.Y2 + 1.5
                        : rule.X1 - 1.5 < glyph.X + glyph.Width && glyph.X < rule.X2 + 1.5) return true;
                }
            }
        }
        return false;
    }
    // Reuse only opaque supported device colours on the white page. Unknown
    // colour-space substitutions, near-white text and later paint stay refused.
    private sealed class VisibilityState(bool deviceGray, bool deviceRgb, bool deviceCmyk, bool boundedVisibleUnicode = false)
    {
        private readonly record struct State(bool FillVisible, bool FillWhite, bool StrokeVisible, int TextMode, double LineWidth, PdfMatrix Ctm,
            int LineJoin=0,int LineCap=0,double MiterLimit=10,double FillDarkness=1,bool SupportedBackground=false,PdfPathEngine.PaintColor FillColor=default);
        private State _state = new(deviceGray, false, deviceGray, 0, 1, PdfMatrix.Identity);
        private readonly Stack<State> _stack = new();
        private bool _shown;
        private bool _painted;
        public bool FillWhite => _state.FillWhite;
        public bool SupportedBackground => _state.SupportedBackground;
        public PdfPathEngine.PaintColor FillColor => _state.FillColor;
        public void SetStrokeStyle(int? join=null,int? cap=null,double? miter=null) =>
            _state=_state with { LineJoin=join??_state.LineJoin,LineCap=cap??_state.LineCap,MiterLimit=miter??_state.MiterLimit };
        public double StrokePaintRadius(int page)
        {
            var origin=_state.Ctm.Point(0,0);var x=_state.Ctm.Point(1,0);var y=_state.Ctm.Point(0,1);
            var xx=x.X-origin.X;var xy=x.Y-origin.Y;var yx=y.X-origin.X;var yy=y.Y-origin.Y;
            var scale=Math.Sqrt(Math.Max(xx*xx+xy*xy,yx*yx+yy*yy));
            var radius=_state.LineWidth*scale/2;
            if (!double.IsFinite(radius) || radius<=0) throw new PdfParseException("P01",page);
            return radius;
        }
        public double TextPaintMargin(int page)
        {
            if (_state.TextMode == 0) return 0;
            var origin=_state.Ctm.Point(0,0);var x=_state.Ctm.Point(1,0);var y=_state.Ctm.Point(0,1);
            var xx=x.X-origin.X;var xy=x.Y-origin.Y;var yx=y.X-origin.X;var yy=y.Y-origin.Y;
            var a=xx*xx+xy*xy;var b=yx*yx+yy*yy;var tolerance=Math.Max(a,b)*1e-12;
            var width=_state.LineWidth*Math.Sqrt(Math.Max(a,b));
            if (!boundedVisibleUnicode || _state.TextMode!=2 || !double.IsFinite(width) || width is <=0 or >.5 ||
                Math.Abs(a-b)>tolerance || Math.Abs(xx*yx+xy*yy)>tolerance) throw new PdfParseException("P01",page);
            var join=_state.LineJoin==0?_state.MiterLimit:1;
            var cap=_state.LineCap==2?Math.Sqrt(2):1;
            return width/2*Math.Max(join,cap);
        }
        private void CheckStroke(int page, PdfPathEngine paths, bool closeLast)
        {
            var origin = _state.Ctm.Point(0, 0); var x = _state.Ctm.Point(1, 0); var y = _state.Ctm.Point(0, 1);
            var xx = x.X - origin.X; var xy = x.Y - origin.Y; var yx = y.X - origin.X; var yy = y.Y - origin.Y;
            var xLength = xx * xx + xy * xy; var yLength = yx * yx + yy * yy; var scale = Math.Sqrt(Math.Max(xLength, yLength));
            // Under a similarity transform, right-angle grid joins remain
            // right angles and their painted miter fits the collision margin.
            // Shear/nonuniform scaling can hide a much longer original miter.
            var tolerance = Math.Max(xLength, yLength) * 1e-12;
            if (!_state.StrokeVisible || !double.IsFinite(scale) || scale <= 0 || Math.Abs(xLength - yLength) > tolerance || Math.Abs(xx * yx + xy * yy) > tolerance
                || _state.LineWidth * scale > 2 || !paths.PendingStrokeIsRules(closeLast)) throw new PdfParseException("P01", page);
        }
        public void SetLineWidth(double width, int page)
        {
            if (!double.IsFinite(width) || width < 0) throw new PdfParseException("P01", page);
            _state = _state with { LineWidth = width };
        }
        public void Operation(IGraphicsStateOperation operation, int page, PdfPathEngine paths)
        {
            var name = operation.Operator;
            bool SupportedDevice() => name.ToLowerInvariant() switch
            { "g" => deviceGray, "rg" => deviceRgb, "k" => deviceCmyk, _ => false };
            bool Visible(double[] components) => SupportedDevice() && (name.ToLowerInvariant() switch
            {
                // Require at least ten percent source contrast in a channel.
                // This is a conservative colour bound, not OCR confidence.
                "g" => components.Length == 1 && components[0] <= .9,
                "rg" => components.Length == 3 && components.Min() <= .9,
                // DeviceCMYK conversion is renderer-dependent. Keep the former
                // exact-black subset until independent native colour evidence.
                "k" => components.Length == 4 && components.Take(3).All(v => v == 0) && components[3] == 1,
                _ => false
            });
            bool White(double[] components) => SupportedDevice() && (name switch
            {
                "g" => components.Length == 1 && components[0] == 1,
                "rg" => components.Length == 3 && components.All(v => v == 1),
                "k" => components.Length == 4 && components.All(v => v == 0),
                _ => false
            });
            switch (name)
            {
                case "d":
                    // A dash pattern can paint no part of an extracted segment.
                    if (operation is not SetLineDashPattern dash || dash.Pattern.Array.Count != 0) throw new PdfParseException("P01", page);
                    break;
                case "q":
                    if (_stack.Count >= 64) throw new PdfParseException("limit", page);
                    _stack.Push(_state); break;
                case "Q":
                    if (!_stack.TryPop(out _state)) throw new PdfParseException("P01", page); break;
                case "cm":
                    _state = _state with { Ctm = PdfMatrix.From(Numbers(operation)).FollowedBy(_state.Ctm) }; break;
                case "g": case "rg": case "k":
                    var colour = Numbers(operation);
                    if (colour.Any(v => v is < 0 or > 1)) throw new PdfParseException("P01", page);
                    _state = _state with { FillVisible = Visible(colour), FillWhite = White(colour),
                        FillDarkness = name=="k" ? colour[3] : 1-colour.Min(),
                        FillColor = name=="g" ? new(colour[0],colour[0],colour[0]) : name=="rg" ? new(colour[0],colour[1],colour[2]) : new(1-colour[3],1-colour[3],1-colour[3]),
                        SupportedBackground = name=="g" && deviceGray || name=="rg" && deviceRgb }; break;
                case "G": case "RG": case "K":
                    var strokeColour = Numbers(operation);
                    if (strokeColour.Any(v => v is < 0 or > 1)) throw new PdfParseException("P01", page);
                    _state = _state with { StrokeVisible = Visible(strokeColour) }; break;
                case "CS": case "cs": case "SC": case "SCN": case "sc": case "scn":
                case "sh": case "BI": case "ID": case "EI":
                case "c": case "v": case "y":
                    throw new PdfParseException("P01", page);
                case "w":
                    var width = Numbers(operation);
                    if (width.Length != 1 || width[0] < 0) throw new PdfParseException("P01", page);
                    SetLineWidth(width[0], page); break;
                case "Tr":
                    var mode = Numbers(operation);
                    // Extracted glyph bounds do not include a painted outline.
                    if (mode.Length != 1 || mode[0] != 0 && !(boundedVisibleUnicode && mode[0] == 2)) throw new PdfParseException("P01", page);
                    _state = _state with { TextMode = (int)mode[0] }; break;
                case "Tj": case "TJ": case "'": case "\"":
                    if (_state.TextMode is 0 or 2 && !_state.FillVisible || _state.TextMode is 1 or 2 && !_state.StrokeVisible)
                        throw new PdfParseException("P01", page);
                    _shown = true; break;
                case "M":
                    if (boundedVisibleUnicode && (Numbers(operation) is not [var miter] || miter is < 1 or > 10)) throw new PdfParseException("P01",page);
                    if (boundedVisibleUnicode) SetStrokeStyle(miter:Numbers(operation)[0]);
                    break;
                case "j": case "J":
                    if (boundedVisibleUnicode && (Numbers(operation) is not [var style] || style is not (0 or 1 or 2))) throw new PdfParseException("P01",page);
                    if (boundedVisibleUnicode)
                    { if(name=="j")SetStrokeStyle(join:(int)Numbers(operation)[0]);else SetStrokeStyle(cap:(int)Numbers(operation)[0]); }
                    break;
                case "f": case "F": case "f*": case "B": case "B*": case "b": case "b*":
                    if (!(boundedVisibleUnicode && (_state.SupportedBackground || _state.FillWhite) && paths.PendingFillIsRectangles) &&
                        (_shown && !(boundedVisibleUnicode && _state.FillVisible && paths.PendingFillIsThinRules) ||
                        _state.FillWhite && _painted || !_state.FillWhite && !(_state.FillVisible && paths.PendingFillIsThinRules)))
                    {
                        var failure=new PdfParseException("P01",page);
                        failure.Data["NativeFillState"]=new { _state.FillWhite,_state.FillVisible,_state.FillDarkness,_state.SupportedBackground,
                            ThinRules=paths.PendingFillIsThinRules,Rectangles=paths.PendingFillIsRectangles,Shown=_shown };
                        throw failure;
                    }
                    if (name is "B" or "B*" or "b" or "b*") { CheckStroke(page, paths, name is "b" or "b*"); _painted = true; }
                    else if (!_state.FillWhite) _painted = true;
                    break;
                case "S": case "s":
                    CheckStroke(page, paths, name == "s"); _painted = true; break;
            }
        }
    }
    private static double[] Numbers(IGraphicsStateOperation operation)
    {
        using var output = new MemoryStream(); operation.Write(output);
        if (output.Length > 1024) throw new PdfParseException("limit");
        var parts = Encoding.ASCII.GetString(output.ToArray()).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts[^1] != operation.Operator) throw new PdfParseException("P01");
        return parts[..^1].Select(part => double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : throw new PdfParseException("P01")).ToArray();
    }
    private static IReadOnlyList<PdfGlyph> SpecialGlyphs(Page page, PdfDisplayTransform display, CancellationToken token)
    {
        var output = new List<PdfGlyph>(); var crop = page.CropBox.Bounds; var order = 0;
        // PdfPig has already translated/rotated letters to the crop box. Undo that transform before applying the media-box display transform.
        (double X, double Y) Raw(double x, double y) => page.Rotation.Value switch
        { 90 => (crop.Width - y + crop.Left, x + crop.Bottom), 180 => (crop.Width - x + crop.Left, crop.Height - y + crop.Bottom),
            270 => (y + crop.Left, crop.Height - x + crop.Bottom), _ => (x + crop.Left, y + crop.Bottom) };
        foreach (var letter in page.Letters)
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(letter.Value)) { order++; continue; }
            if ((int)letter.RenderingMode is < 0 or > 2 || letter.Value.Contains('\uFFFD') || letter.Value.Any(char.IsControl)) throw new PdfParseException("P01", page.Number);
            var rect = letter.GlyphRectangleLoose;
            var corners = new[] { Raw(rect.Left, rect.Bottom), Raw(rect.Left, rect.Top), Raw(rect.Right, rect.Bottom), Raw(rect.Right, rect.Top) };
            var points = corners.Select(p => display.Point(p.X, p.Y)).ToArray();
            var x = points.Min(p => p.X); var y = points.Min(p => p.Y); var w = points.Max(p => p.X) - x; var h = points.Max(p => p.Y) - y;
            if (!new[] { x, y, w, h }.All(double.IsFinite) || w <= 0 || h <= 0) throw new PdfParseException("P01", page.Number);
            output.Add(new(letter.Value, x, y, w, h, letter.TextSequence, order++));
        }
        return output;
    }
    private static T Resolve<T>(PdfDocument document, IToken? token) where T : class, IToken => DirectObjectFinder.TryGet<T>(token, document.Structure.TokenScanner, out var value) ? value : throw new PdfParseException("P01");
    private static IToken? Get(DictionaryToken dictionary, string name) => dictionary.Data.GetValueOrDefault(name);
    private static double Number(PdfDocument document, DictionaryToken dict, string key, double? fallback = null)
    {
        var value = Get(dict, key);
        if (value is null && fallback is not null) return fallback.Value;
        var number = Resolve<NumericToken>(document, value).Data;
        return double.IsFinite(number) ? number : throw new PdfParseException("P01");
    }
    private static string Name(PdfDocument document, DictionaryToken dict, string key) => Resolve<NameToken>(document, Get(dict, key)).Data;
    private static DictionaryToken Resources(PdfDocument document, DictionaryToken page)
    {
        var current = page;
        for (var depth = 0; depth < 64; depth++)
        {
            if (Get(current, "Resources") is { } resources) return Resolve<DictionaryToken>(document, resources);
            if (Get(current, "Parent") is not { } parent) break;
            current = Resolve<DictionaryToken>(document, parent);
        }
        throw new PdfParseException("P01");
    }
    private static DictionaryToken Resource(PdfDocument document, DictionaryToken resources, string kind, string name) =>
        Resolve<DictionaryToken>(document, Get(Resolve<DictionaryToken>(document, Get(resources, kind)), name));
    private static PdfFont ReadFont(PdfDocument document, DictionaryToken font, CancellationToken token,
        FontRecoveryContext? recovery = null, string resource = "")
    {
        PdfUnicodeMap mapping;
        Func<int, RecoveryFontEvidence>? fontEvidence = null;
        if (recovery is not null && !font.Data.ContainsKey("ToUnicode"))
            (mapping, fontEvidence) = MissingUnicodeMap(document, font, resource, recovery, token);
        else
        {
        var stream = Resolve<StreamToken>(document, Get(font, "ToUnicode"));
        var data = stream.Data;
        var filters = document.Structure.FilterProvider.GetFilters(stream.StreamDictionary, document.Structure.TokenScanner);
        if (filters.Count > 8 || data.Length > 2_000_000 || stream.StreamDictionary.Data.ContainsKey("UseCMap")) throw new PdfParseException("P01");
        for (var index = 0; index < filters.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            if (!filters[index].IsSupported) throw new PdfParseException("P01");
            data = filters[index].Decode(data, stream.StreamDictionary, document.Structure.FilterProvider, index);
            if (data.Length > 2_000_000) throw new PdfParseException("limit");
        }
        mapping = PdfUnicodeMap.Read(data.ToArray(), token);
        }
        var metrics = font;
        var composite = Name(document, font, "Subtype") == "Type0";
        if (composite)
        {
            var descendants = Resolve<ArrayToken>(document, Get(font, "DescendantFonts"));
            if (Name(document, font, "Encoding") != "Identity-H" || mapping.CodeBytes != 2 || descendants.Data.Count != 1) throw new PdfParseException("P01");
            metrics = Resolve<DictionaryToken>(document, descendants.Data[0]);
            if (Name(document, metrics, "Subtype") is not "CIDFontType0" and not "CIDFontType2") throw new PdfParseException("P01");
        }
        else if (Name(document, font, "Subtype") is not "TrueType" and not "Type1" || mapping.CodeBytes != 1) throw new PdfParseException("P01");
        var descriptor = Resolve<DictionaryToken>(document, Get(metrics, "FontDescriptor")); var widths = new Dictionary<int, double>();
        int Integer(IToken value)
        { var n = Resolve<NumericToken>(document, value).Data; return n == Math.Round(n) && n is >= 0 and <= 65535 ? (int)n : throw new PdfParseException("P01"); }
        void Put(int code, double value) { if (code is < 0 or > 65535 || !double.IsFinite(value) || value < 0 || !widths.TryAdd(code, value)) throw new PdfParseException("P01"); }
        if (composite)
        {
            if (Get(metrics, "W") is { } widthToken)
            {
                var array = Resolve<ArrayToken>(document, widthToken).Data;
                if (array.Count > 200000) throw new PdfParseException("limit");
                var cursor = 0;
                while (cursor < array.Count)
                {
                    token.ThrowIfCancellationRequested(); var first = Integer(array[cursor++]);
                    if (cursor >= array.Count) throw new PdfParseException("P01");
                    if (DirectObjectFinder.TryGet<ArrayToken>(array[cursor], document.Structure.TokenScanner, out var list))
                    {
                        if (list.Data.Count is < 1 || list.Data.Count > 65536 - first) throw new PdfParseException("P01");
                        for (var offset = 0; offset < list.Data.Count; offset++) Put(first + offset, Resolve<NumericToken>(document, list.Data[offset]).Data);
                        cursor++;
                    }
                    else
                    {
                        var last = Integer(array[cursor++]);
                        if (last < first || cursor >= array.Count) throw new PdfParseException("P01");
                        var width = Resolve<NumericToken>(document, array[cursor++]).Data;
                        for (var code = first; code <= last; code++) Put(code, width);
                    }
                }
            }
        }
        else
        {
            var first = Number(document, font, "FirstChar"); var last = Number(document, font, "LastChar");
            var array = Resolve<ArrayToken>(document, Get(font, "Widths")).Data;
            if (first != Math.Round(first) || last != Math.Round(last) || first < 0 || last > 255 || last < first || array.Count != last - first + 1) throw new PdfParseException("P01");
            for (var index = 0; index < array.Count; index++) Put((int)first + index, Resolve<NumericToken>(document, array[index]).Data);
            if (mapping.Values.Keys.Any(key => !widths.ContainsKey(key))) throw new PdfParseException("P01");
        }
        return new PdfFont(mapping, widths, composite ? Number(document, metrics, "DW", 1000) : 0, Number(document, descriptor, "Ascent"), Number(document, descriptor, "Descent"))
            { Resource = resource, RecoveryEvidence = fontEvidence }.Validate();
    }

}
