using System.Globalization;
using System.Text;
using Takupoke.Core;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Graphics.Operations;
using UglyToad.PdfPig.Graphics.Operations.TextShowing;
using UglyToad.PdfPig.Graphics.Operations.TextState;
using UglyToad.PdfPig.Parser.Parts;
using UglyToad.PdfPig.Tokens;

namespace Takupoke.Infrastructure.Parsing;

/// <summary>Opens bytes locally. Ordinary timetables use a separate, strict drawing interpreter and verify its text against PdfPig.</summary>
public static class PdfPigLayoutReader
{
    public static IReadOnlyList<PdfPageLayout> Read(byte[] bytes, MaterialKind kind, CancellationToken token = default)
    {
        if (bytes.Length is < 1 or > 50 * 1024 * 1024 || !bytes.AsSpan(0, Math.Min(bytes.Length, 5)).SequenceEqual("%PDF-"u8) || kind == MaterialKind.Changes) throw new PdfParseException("P04");
        try
        {
            token.ThrowIfCancellationRequested();
            using var document = PdfDocument.Open(bytes, new ParsingOptions { UseLenientParsing = false, SkipMissingFonts = false, MaxStackDepth = 64, UseActualText = false });
            if (document.IsEncrypted || document.NumberOfPages is < 1 or > 12) throw new PdfParseException("P04");
            var output = new List<PdfPageLayout>();
            for (var number = 1; number <= document.NumberOfPages; number++)
            {
                token.ThrowIfCancellationRequested();
                var page = document.GetPage(number); var media = page.MediaBox.Bounds;
                if (page.Rotation.Value is not 0 and not 90 and not 180 and not 270) throw new PdfParseException("P02", number);
                if (page.Operations.Count > 1_000_000 || page.Letters.Count is < 1 or > 100000 || page.Text.Length > 100000) throw new PdfParseException("limit", number);
                var transform = new PdfDisplayTransform(media.Left, media.Bottom, media.Width, media.Height, page.Rotation.Value);
                var paths = new PdfPathEngine(transform, token);
                var text = kind == MaterialKind.Timetable ? new PdfTextEngine(token) : null;
                var resources = Resources(document, page.Dictionary); var fonts = new Dictionary<string, PdfFont>();
                foreach (var operation in page.Operations)
                {
                    token.ThrowIfCancellationRequested();
                    var name = operation.Operator;
                    if (name == "Do") throw new PdfParseException("P12", number);
                    if (text is not null && name is "BDC" or "BMC" or "W" or "W*") throw new PdfParseException("P01", number);
                    if (PdfPathEngine.Supports(name) || text is not null && PdfTextEngine.Supports(name))
                    {
                        var numbers = Numbers(operation);
                        if (PdfPathEngine.Supports(name)) paths.Operation(name, numbers);
                        if (text is not null && PdfTextEngine.Supports(name)) text.Operation(name, numbers);
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
                    if (name == "gs")
                    {
                        using var serialized = new MemoryStream(); operation.Write(serialized);
                        var items = Encoding.ASCII.GetString(serialized.ToArray()).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                        if (items.Length != 2 || !items[0].StartsWith('/')) throw new PdfParseException("P01", number);
                        var state = Resource(document, resources, "ExtGState", items[0][1..]);
                        if (new[] { "Font", "SMask", "TR", "TR2" }.Any(state.Data.ContainsKey) || Number(document, state, "ca", 1) != 1 || Number(document, state, "CA", 1) != 1) throw new PdfParseException("P01", number);
                    }
                }
                IReadOnlyList<PdfGlyph> glyphs = text is not null ? text.Finish(page.Text).Select(transform.Glyph).ToArray() : SpecialGlyphs(page, transform, token);
                var layout = new PdfPageLayout(transform.Width, transform.Height, glyphs, paths.Finish()); layout.Validate(number); output.Add(layout);
            }
            return output;
        }
        catch (PdfParseException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch { throw new PdfParseException("P01"); }
    }
    private static byte[] Bytes(string? literal, ReadOnlyMemory<byte> bytes)
    { if (literal is null) return bytes.ToArray(); if (literal.Any(c => c > 255)) throw new PdfParseException("P01"); return Encoding.Latin1.GetBytes(literal); }
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
