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
public static class PdfPigLayoutReader
{
    public static IReadOnlyList<PdfPageLayout> Read(byte[] bytes, MaterialKind kind, CancellationToken token = default, RecoveryReadCapture? capture = null)
    {
        capture?.Reset();
        if (bytes.Length is < 1 or > 50 * 1024 * 1024 || !bytes.AsSpan(0, Math.Min(bytes.Length, 5)).SequenceEqual("%PDF-"u8) || kind == MaterialKind.Changes) throw new PdfParseException("unreadable");
        try
        {
            token.ThrowIfCancellationRequested();
            using var document = PdfDocument.Open(bytes, new ParsingOptions { UseLenientParsing = false, SkipMissingFonts = false, MaxStackDepth = 64, UseActualText = false });
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
                var visibility = new VisibilityState();
                var text = kind == MaterialKind.Timetable ? new PdfTextEngine(token) : null;
                var resources = Resources(document, page.Dictionary); var fonts = new Dictionary<string, PdfFont>();
                foreach (var operation in page.Operations)
                {
                    token.ThrowIfCancellationRequested();
                    var name = operation.Operator;
                    visibility.Operation(operation, number, paths);
                    if (name == "Do") throw new PdfParseException("P12", number);
                    if (name is "BDC" or "BMC" or "W" or "W*") throw new PdfParseException("P01", number);
                    if (PdfPathEngine.Supports(name) || text is not null && PdfTextEngine.Supports(name))
                    {
                        var numbers = Numbers(operation);
                        if (PdfPathEngine.Supports(name))
                        {
                            var paint = visibility.FillWhite ? name switch { "f" or "F" or "f*" => "n", "B" or "B*" => "S", "b" or "b*" => "s", _ => name } : name;
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
                    switch (operation)
                    {
                        case SetFontAndSize font:
                            if (!fonts.TryGetValue(font.Font.Data, out var decoded))
                            { if (fonts.Count >= 128) throw new PdfParseException("limit", number); decoded = ReadFont(document, Resource(document, resources, "Font", font.Font.Data), token); fonts.Add(font.Font.Data, decoded); }
                            text.SetFont(decoded, font.Size); break;
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

                }
                IReadOnlyList<PdfGlyph> glyphs = text is not null ? text.Finish(page.Text).Select(transform.Glyph).ToArray() : SpecialGlyphs(page, transform, token);
                capture?.Record(number, RecoveryInputState.Partial, new PdfPageLayout(transform.Width, transform.Height, glyphs, []));
                var layout = new PdfPageLayout(transform.Width, transform.Height, glyphs, paths.Finish());
                layout.ValidateViewport(number);
                if (RulesOverlapText(layout.Lines, glyphs, token))
                    throw new PdfParseException("P01", number);
                capture?.Record(number, glyphs.Count == 0 ? RecoveryInputState.RasterOnly : RecoveryInputState.Complete, layout);
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
    // Only reuse the supported black-text drawing subset. Unsupported colours
    // or later paint can leave extractable text which is absent from the page.
    private sealed class VisibilityState
    {
        private readonly record struct State(bool FillBlack, bool FillWhite, bool StrokeBlack, int TextMode, double LineWidth, PdfMatrix Ctm);
        private State _state = new(true, false, true, 0, 1, PdfMatrix.Identity);
        private readonly Stack<State> _stack = new();
        private bool _shown;
        private bool _painted;
        public bool FillWhite => _state.FillWhite;
        private void CheckStroke(int page, PdfPathEngine paths, bool closeLast)
        {
            var origin = _state.Ctm.Point(0, 0); var x = _state.Ctm.Point(1, 0); var y = _state.Ctm.Point(0, 1);
            var scale = Math.Max(Math.Sqrt(Math.Pow(x.X - origin.X, 2) + Math.Pow(x.Y - origin.Y, 2)), Math.Sqrt(Math.Pow(y.X - origin.X, 2) + Math.Pow(y.Y - origin.Y, 2)));
            if (!_state.StrokeBlack || !double.IsFinite(scale) || _state.LineWidth * scale > 2 || !paths.PendingStrokeIsRules(closeLast)) throw new PdfParseException("P01", page);
        }
        public void SetLineWidth(double width, int page)
        {
            if (!double.IsFinite(width) || width < 0) throw new PdfParseException("P01", page);
            _state = _state with { LineWidth = width };
        }
        public void Operation(IGraphicsStateOperation operation, int page, PdfPathEngine paths)
        {
            var name = operation.Operator;
            bool Black(double[] components) => name.ToLowerInvariant() switch
            {
                "g" => components.Length == 1 && components[0] == 0,
                "rg" => components.Length == 3 && components.All(v => v == 0),
                "k" => components.Length == 4 && components.Take(3).All(v => v == 0) && components[3] == 1,
                _ => false
            };
            bool White(double[] components) => name switch
            {
                "g" => components.Length == 1 && components[0] == 1,
                "rg" => components.Length == 3 && components.All(v => v == 1),
                "k" => components.Length == 4 && components.All(v => v == 0),
                _ => false
            };
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
                    _state = _state with { FillBlack = Black(colour), FillWhite = White(colour) }; break;
                case "G": case "RG": case "K":
                    _state = _state with { StrokeBlack = Black(Numbers(operation)) }; break;
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
                    if (mode.Length != 1 || mode[0] != 0) throw new PdfParseException("P01", page);
                    _state = _state with { TextMode = (int)mode[0] }; break;
                case "Tj": case "TJ": case "'": case "\"":
                    if (_state.TextMode is 0 or 2 && !_state.FillBlack || _state.TextMode is 1 or 2 && !_state.StrokeBlack)
                        throw new PdfParseException("P01", page);
                    _shown = true; break;
                case "f": case "F": case "f*": case "B": case "B*": case "b": case "b*":
                    if (_shown || _state.FillWhite && _painted || !_state.FillWhite && !(_state.FillBlack && paths.PendingFillIsThinRules)) throw new PdfParseException("P01", page);
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
    private static PdfFont ReadFont(PdfDocument document, DictionaryToken font, CancellationToken token)
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
        var mapping = PdfUnicodeMap.Read(data.ToArray(), token); var metrics = font;
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
        return new PdfFont(mapping, widths, composite ? Number(document, metrics, "DW", 1000) : 0, Number(document, descriptor, "Ascent"), Number(document, descriptor, "Descent")).Validate();
    }
}
