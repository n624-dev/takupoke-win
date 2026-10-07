using UglyToad.PdfPig;
using UglyToad.PdfPig.Parser.Parts;
using UglyToad.PdfPig.Tokens;

namespace Takupoke.Infrastructure.Recovery;

/// Chooses a native image's sampling grid, without extracting or replacing
/// visible page content. The OS PDF renderer still draws the entire original,
/// including its colour interpretation. Unsupported pages keep the old size.
public static class RecoveryPdfImageResolution
{
    public static IReadOnlyDictionary<int, (int Width, int Height)> Inspect(byte[] bytes, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var output = new Dictionary<int, (int, int)>();
        if (bytes.Length is < 1 or > 50 * 1024 * 1024) return output;
        try
        {
            using var document = PdfDocument.Open(bytes, new ParsingOptions { UseLenientParsing = false, SkipMissingFonts = false, MaxStackDepth = 64 });
            if (document.IsEncrypted || document.NumberOfPages is < 1 or > 12) return output;
            if (new[] { "OutputIntents", "OCProperties" }.Any(document.Structure.Catalog.CatalogDictionary.Data.ContainsKey)) return output;
            for (var number = 1; number <= document.NumberOfPages; number++)
            {
                token.ThrowIfCancellationRequested();
                var page = document.GetPage(number); var bounds = page.MediaBox.Bounds;
                if (page.CropBox.Bounds != bounds || page.Rotation.Value != 0 || page.Letters.Count != 0 || page.Operations.Count != 4) continue;
                if (!page.Operations.Select(op => op.Operator).SequenceEqual(new[] { "q", "cm", "Do", "Q" })) continue;
                // Walk inherited dictionaries conservatively. Visible overlays
                // and altered units are not this narrow full-page-image family.
                var current = page.Dictionary; var supported = true; DictionaryToken? resources = null;
                for (var depth = 0; ; depth++)
                {
                    token.ThrowIfCancellationRequested();
                    if (depth >= 64 || new[] { "Annots", "Group", "OC", "UserUnit" }.Any(current.Data.ContainsKey)) { supported = false; break; }
                    if (resources is null && current.Data.TryGetValue("Resources", out var resourceToken) &&
                        !DirectObjectFinder.TryGet<DictionaryToken>(resourceToken, document.Structure.TokenScanner, out resources)) { supported = false; break; }
                    if (!current.Data.TryGetValue("Parent", out var parent)) break;
                    if (!DirectObjectFinder.TryGet<DictionaryToken>(parent, document.Structure.TokenScanner, out current)) { supported = false; break; }
                }
                if (!supported) continue;
                // GetImages also flattens Form XObjects. A form may paint over
                // its image; only an actual direct Image invocation is eligible.
                using var invocation = new MemoryStream(); page.Operations[2].Write(invocation);
                var invoke = System.Text.Encoding.ASCII.GetString(invocation.ToArray()).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (invoke.Length != 2 || !invoke[0].StartsWith('/') || invoke[1] != "Do" || resources is null ||
                    resources.Data.ContainsKey("ColorSpace") || !resources.Data.TryGetValue("XObject", out var objectsToken) ||
                    !DirectObjectFinder.TryGet<DictionaryToken>(objectsToken, document.Structure.TokenScanner, out var objects) ||
                    !objects.Data.TryGetValue(invoke[0][1..], out var invokedToken) ||
                    !DirectObjectFinder.TryGet<StreamToken>(invokedToken, document.Structure.TokenScanner, out var invoked) ||
                    !invoked.StreamDictionary.Data.TryGetValue("Subtype", out var subtypeToken) ||
                    !DirectObjectFinder.TryGet<NameToken>(subtypeToken, document.Structure.TokenScanner, out var subtype) || subtype.Data != "Image") continue;
                using var serialized = new MemoryStream(); page.Operations[1].Write(serialized);
                var parts = System.Text.Encoding.ASCII.GetString(serialized.ToArray()).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 7 || parts[6] != "cm") continue;
                var matrix = new double[6];
                if (Enumerable.Range(0, 6).Any(i => !double.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out matrix[i]) || !double.IsFinite(matrix[i]))) continue;
                if (matrix[0] != bounds.Width || matrix[1] != 0 || matrix[2] != 0 || matrix[3] != bounds.Height || matrix[4] != bounds.Left || matrix[5] != bounds.Bottom) continue;
                var images = page.GetImages().Take(2).ToArray();
                if (images.Length != 1 || images[0].IsInlineImage || images[0].BoundingBox != bounds) continue;
                var width = images[0].WidthInSamples; var height = images[0].HeightInSamples;
                // Existing memory ceilings and at least two original samples
                // per PDF point. Low-resolution scans are never downsampled.
                if (width is < 640 or > 2400 || height is < 1 or > 3200 || width < bounds.Width * 2 || height < bounds.Height * 2) continue;
                if (Math.Abs((double)width * bounds.Height - (double)height * bounds.Width) > 1e-6) continue;
                output.Add(number, (width, height));
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is not OutOfMemoryException) { output.Clear(); }
        token.ThrowIfCancellationRequested(); return output;
    }
}
