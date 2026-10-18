using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Parser.Parts;
using UglyToad.PdfPig.Tokens;

// Research-only alternative acquisition. Prove a single opaque, whole-page
// DeviceRGB image from real native PDF operations, then retain its original
// samples rather than OS resampling. Unknown drawing paths are refused.
internal static class OriginalImageRead
{
    internal static void Run(string path)
    {
        path = Path.GetFullPath(path); var root = Path.GetDirectoryName(path)!;
        var manifest = JsonNode.Parse(File.ReadAllBytes(path))!; var receipts = new List<object>();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var token = lifetime.Token;
        foreach (var item in manifest["documents"]!.AsArray())
        {
            var asset = item!["sourcePDF"]!; var relative = asset["file"]!.GetValue<string>();
            if (Path.IsPathRooted(relative) || relative.Contains("..")) throw new InvalidDataException("Owned PDF path failed.");
            var bytes = File.ReadAllBytes(Path.Combine(root, relative));
            if (bytes.Length is < 1 or > 50 * 1024 * 1024 || bytes.Length != asset["bytes"]!.GetValue<int>() ||
                Sha(bytes) != asset["sha256"]!.GetValue<string>()) throw new InvalidDataException("PDF identity failed.");
            var pages = Read(bytes, token); var pageReceipts = new List<object>();
            if (pages.Count != item["pages"]!.AsArray().Count) throw new InvalidDataException("Actual source page count failed.");
            foreach (var (pixels, i) in pages.Select((p, i) => (p, i)))
            {
                var page = item["pages"]!.AsArray()[i]!; var oldHash = page["pixels"]!["sha256"]!.GetValue<string>();
                var name = item["id"]!.GetValue<string>() + "-source-p" + (i + 1) + ".bgra";
                File.WriteAllBytes(Path.Combine(root, name), pixels.Bgra);
                var hash = Sha(pixels.Bgra);
                page["width"] = pixels.Width; page["height"] = pixels.Height;
                page["pixels"] = new JsonObject { ["file"] = name, ["bytes"] = pixels.Bgra.Length, ["sha256"] = hash };
                pageReceipts.Add(new { page = i + 1, pixels.Width, pixels.Height, originalSourceSamplesSHA256 = hash,
                    sourceStreamSHA256 = pixels.StreamSHA256, originalDrawingHash = oldHash, sourceEqualsDrawing = hash == oldHash });
            }
            receipts.Add(new { id = item["id"]!.GetValue<string>(), actualPDFSHA256 = Sha(bytes), pages = pageReceipts });
            CryptographicOperations.ZeroMemory(bytes);
        }
        manifest["sourceScope"] = "Research-only actual PDF opaque whole-page source image acquisition; no OS resampling, consumed drawings where applicable; not production/final qualification";
        File.WriteAllText(Path.Combine(root, "source-original-inputs.json"), manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(root, "source-original-receipt.json"), JsonSerializer.Serialize(new { receipts, modelCalls = 0,
            scope = "Single-image operation/resource/coverage proof only; no recognition or adoption credit" }, new JsonSerializerOptions { WriteIndented = true }));
    }

    internal sealed record SourcePage(int Width, int Height, byte[] Bgra, string StreamSHA256);

    internal static IReadOnlyList<SourcePage> Read(byte[] bytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (bytes.Length is < 1 or > 50 * 1024 * 1024) throw new InvalidDataException("Source bound failed.");
        using var pdf = PdfDocument.Open(bytes, new ParsingOptions { UseLenientParsing = false, UseActualText = false, MaxStackDepth = 64 });
        if (pdf.IsEncrypted || pdf.NumberOfPages is < 1 or > 12) throw new InvalidDataException("PDF source domain failed.");
        var result = new List<SourcePage>();
        for (var n = 1; n <= pdf.NumberOfPages; n++)
        {
            token.ThrowIfCancellationRequested(); var page = pdf.GetPage(n); var media = page.MediaBox.Bounds;
            if (page.Rotation.Value != 0 || page.CropBox.Bounds != media || page.Letters.Count != 0 ||
                !double.IsFinite(media.Width) || !double.IsFinite(media.Height) || media.Width <= 0 || media.Height <= 0 ||
                new[] { "Annots", "Group", "OC", "AA", "Metadata" }.Any(page.Dictionary.Data.ContainsKey))
                throw new InvalidDataException("Unproved page drawing state.");
            if (page.Dictionary.Data.TryGetValue("UserUnit", out var unit) && Resolve<NumericToken>(pdf, unit).Double != 1)
                throw new InvalidDataException("Unproved page scale.");
            var operations = page.Operations;
            if (operations.Count != 4 || !operations.Select(o => o.Operator).SequenceEqual(new[] { "q", "cm", "Do", "Q" }))
                throw new InvalidDataException("A single whole-page source image is not proved.");
            string Serialize(int index)
            {
                using var stream = new MemoryStream(); operations[index].Write(stream);
                return Encoding.ASCII.GetString(stream.ToArray());
            }
            var matrix = Serialize(1).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (matrix.Length != 7 || matrix[^1] != "cm") throw new InvalidDataException("Image transform failed.");
            var values = matrix.Take(6).Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            if (!values.All(double.IsFinite) || !values.SequenceEqual(new[] { media.Width, 0d, 0d, media.Height, media.Left, media.Bottom }))
                throw new InvalidDataException("Whole-page upright image transform failed.");
            var invocation = Serialize(2).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (invocation.Length != 2 || invocation[1] != "Do" || invocation[0].Length is < 2 or > 65 ||
                invocation[0][0] != '/' || !invocation[0][1..].All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
                throw new InvalidDataException("Image resource name failed.");
            var resources = Resources(pdf, page.Dictionary);
            if (resources.Data.TryGetValue("ColorSpace", out var spaces) &&
                Resolve<DictionaryToken>(pdf, spaces).Data.Keys.Any(k => k is "DefaultRGB" or "DefaultGray" or "DefaultCMYK"))
                throw new InvalidDataException("Default source color substitution refused.");
            var xobjects = Resolve<DictionaryToken>(pdf, Get(resources, "XObject"));
            var source = Resolve<StreamToken>(pdf, Get(xobjects, invocation[0][1..])); var d = source.StreamDictionary;
            if (Resolve<NameToken>(pdf, Get(d, "Subtype")).Data != "Image" ||
                Resolve<NameToken>(pdf, Get(d, "ColorSpace")).Data != "DeviceRGB" ||
                Resolve<NumericToken>(pdf, Get(d, "BitsPerComponent")).Int != 8 ||
                new[] { "SMask", "Mask", "ImageMask", "Decode", "DecodeParms", "Alternates", "OC", "Matte" }.Any(d.Data.ContainsKey) ||
                d.Data.TryGetValue("Interpolate", out var interpolate) && Resolve<BooleanToken>(pdf, interpolate).Data)
                throw new InvalidDataException("Opaque unchanged RGB image is not proved.");
            var width = Resolve<NumericToken>(pdf, Get(d, "Width")).Double;
            var height = Resolve<NumericToken>(pdf, Get(d, "Height")).Double;
            if (width != Math.Truncate(width) || height != Math.Truncate(height) || width is < 1 or > 4096 || height is < 1 or > 4096)
                throw new InvalidDataException("Original image dimensions failed.");
            var native = page.GetImages().ToArray();
            if (native.Length != 1 || native[0].IsInlineImage || native[0].BoundingBox != media ||
                native[0].WidthInSamples != width || native[0].HeightInSamples != height ||
                !native[0].RawMemory.Span.SequenceEqual(source.Data.Span))
                throw new InvalidDataException("Independent native image event mismatch.");
            var expected = checked((int)(width * height * 3)); byte[] rgb;
            if (d.Data.TryGetValue("Filter", out var filter))
            {
                if (Resolve<NameToken>(pdf, filter).Data != "FlateDecode") throw new InvalidDataException("Unsupported source encoding.");
                using var input = new MemoryStream(source.Data.ToArray(), false); using var inflater = new ZLibStream(input, CompressionMode.Decompress);
                rgb = new byte[expected]; var offset = 0;
                while (offset < rgb.Length) { token.ThrowIfCancellationRequested(); var count = inflater.Read(rgb, offset, Math.Min(65536, rgb.Length - offset)); if (count == 0) break; offset += count; }
                if (offset != expected || inflater.ReadByte() != -1) throw new InvalidDataException("Bounded source pixel decode failed.");
            }
            else
            {
                if (source.Data.Length != expected) throw new InvalidDataException("Raw source pixels differ from image dimensions.");
                rgb = source.Data.ToArray();
            }
            var bgra = new byte[checked((int)(width * height * 4))];
            for (var p = 0; p < rgb.Length / 3; p++)
            {
                if (p % 4096 == 0) token.ThrowIfCancellationRequested();
                bgra[p * 4] = rgb[p * 3 + 2]; bgra[p * 4 + 1] = rgb[p * 3 + 1]; bgra[p * 4 + 2] = rgb[p * 3]; bgra[p * 4 + 3] = 255;
            }
            CryptographicOperations.ZeroMemory(rgb); result.Add(new((int)width, (int)height, bgra, Sha(source.Data.Span)));
        }
        return result;
    }

    private static DictionaryToken Resources(PdfDocument pdf, DictionaryToken page)
    {
        var seen = new HashSet<DictionaryToken>();
        for (var depth = 0; depth < 64 && seen.Add(page); depth++)
        {
            if (page.Data.TryGetValue("Resources", out var resources)) return Resolve<DictionaryToken>(pdf, resources);
            page = Resolve<DictionaryToken>(pdf, Get(page, "Parent"));
        }
        throw new InvalidDataException("Resource inheritance exceeded.");
    }
    private static IToken? Get(DictionaryToken dictionary, string name) => dictionary.Data.GetValueOrDefault(name);
    private static T Resolve<T>(PdfDocument pdf, IToken? token) where T : class, IToken =>
        DirectObjectFinder.TryGet<T>(token, pdf.Structure.TokenScanner, out var value) ? value : throw new InvalidDataException("Original source token failed.");
    private static string Sha(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
