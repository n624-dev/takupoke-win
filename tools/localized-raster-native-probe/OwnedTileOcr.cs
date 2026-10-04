using System.Reflection;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;

// Research adapter: exact production sessions and private Image preprocessing.
// Owner filtering precedes recognition solely to avoid artificial context-edge
// fragments becoming a failure in an unrelated core. Complete owned regions
// still use the original DB thresholds, CTC sequence and .8 confidence guard.
internal sealed class OwnedTileOcr(OnnxJapaneseOcr runtime)
{
    private readonly InferenceSession _detector = (InferenceSession)typeof(OnnxJapaneseOcr).GetField("_detector", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime)!;
    private readonly InferenceSession _recognizer = (InferenceSession)typeof(OnnxJapaneseOcr).GetField("_recognizer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime)!;
    private readonly string[] _dictionary = (string[])typeof(OnnxJapaneseOcr).GetField("_dictionary", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime)!;
    private readonly MethodInfo _image = typeof(OnnxJapaneseOcr).GetMethod("Image", BindingFlags.Static | BindingFlags.NonPublic)!;
    private DenseTensor<float> Image(RecoveryRaster raster, RecoveryBox box, int w, int h, bool detection) => (DenseTensor<float>)_image.Invoke(null, [raster, box, w, h, detection])!;
    internal IReadOnlyList<OwnedRegion> Read(RecoveryRaster raster, RasterTile tile, int pageWidth, int pageHeight, Action<object> evidence, CancellationToken token)
    {
        if (!raster.Valid) throw new InvalidDataException("Invalid tile raster.");
        var ratio = Math.Min(1, 960d / Math.Max(raster.Width, raster.Height)); var dw = Math.Max(32, (int)Math.Round(raster.Width * ratio / 32) * 32); var dh = Math.Max(32, (int)Math.Round(raster.Height * ratio / 32) * 32);
        using var detection = _detector.Run([NamedOnnxValue.CreateFromTensor("x", Image(raster, new(0, 0, raster.Width, raster.Height), dw, dh, true))]); token.ThrowIfCancellationRequested();
        var map = detection.First().AsTensor<float>();
        if (map.Dimensions.Length != 4 || map.Dimensions[0] != 1 || map.Dimensions[1] != 1 || map.Dimensions[2] != dh || map.Dimensions[3] != dw) throw new InvalidDataException("OCR detector output shape mismatch.");
        for (var y = 0; y < dh; y++) { token.ThrowIfCancellationRequested(); for (var x = 0; x < dw; x++) if (!float.IsFinite(map[0, 0, y, x]) || map[0, 0, y, x] is < 0 or > 1) throw new InvalidDataException("OCR detection probability outside original contract."); }
        var visited = new bool[dw * dh]; var boxes = new List<(RecoveryBox Padded, RecoveryBox Support)>();
        for (var y = 0; y < dh; y++) for (var x = 0; x < dw; x++)
        {
            if (x == 0) token.ThrowIfCancellationRequested();
            if (visited[y * dw + x] || map[0, 0, y, x] < .3) continue;
            var queue = new Queue<(int X, int Y)>(); queue.Enqueue((x, y)); visited[y * dw + x] = true;
            var left = x; var top = y; var right = x; var bottom = y; double score = 0; var count = 0;
            while (queue.TryDequeue(out var p))
            {
                if (++count % 4096 == 0) token.ThrowIfCancellationRequested(); score += map[0, 0, p.Y, p.X]; left = Math.Min(left, p.X); right = Math.Max(right, p.X); top = Math.Min(top, p.Y); bottom = Math.Max(bottom, p.Y);
                foreach (var (nx, ny) in new[] { (p.X - 1, p.Y), (p.X + 1, p.Y), (p.X, p.Y - 1), (p.X, p.Y + 1) }) if (nx >= 0 && ny >= 0 && nx < dw && ny < dh && !visited[ny * dw + nx] && map[0, 0, ny, nx] >= .3) { visited[ny * dw + nx] = true; queue.Enqueue((nx, ny)); }
            }
            if (count < 6 || score / count < .6) continue;
            var margin = Math.Max(1, (bottom - top + 1) * .25); var bx = Math.Max(0, (left - margin) * raster.Width / dw); var by = Math.Max(0, (top - margin) * raster.Height / dh); var ex = Math.Min(raster.Width, (right + 1 + margin) * raster.Width / dw); var ey = Math.Min(raster.Height, (bottom + 1 + margin) * raster.Height / dh);
            boxes.Add((new(bx, by, ex - bx, ey - by), new(left * (double)raster.Width / dw, top * (double)raster.Height / dh, (right + 1 - left) * (double)raster.Width / dw, (bottom + 1 - top) * (double)raster.Height / dh))); if (boxes.Count > 10000) throw new InvalidDataException("OCR detector candidate limit exceeded.");
        }
        var regions = new List<OwnedRegion>();
        foreach (var region in boxes.OrderBy(b => b.Padded.Y).ThenBy(b => b.Padded.X))
        {
            token.ThrowIfCancellationRequested(); var box = region.Padded; var support = region.Support; if (!TileGeometry.Owns(tile, support)) continue;
            evidence(new { tile = tile.Index, ownedCandidateBox = box, detectorSupport = support, stage = "ownership-before-recognition" });
            var global = TileGeometry.CompleteOwnedBox(tile, box, pageWidth, pageHeight, support);
            var globalSupport = support with { X = tile.X + support.X, Y = tile.Y + support.Y };
            var width = Math.Clamp((int)Math.Ceiling(box.Width / box.Height * 48), 16, 960);
            using var recognition = _recognizer.Run([NamedOnnxValue.CreateFromTensor("x", Image(raster, box, width, 48, false))]); token.ThrowIfCancellationRequested();
            var logits = recognition.First().AsTensor<float>(); if (logits.Dimensions.Length != 3 || logits.Dimensions[0] != 1 || logits.Dimensions[2] != 18385) throw new InvalidDataException("OCR recognition output shape mismatch.");
            var checkedScores = 0; foreach (var score in logits) { if (++checkedScores % 4096 == 0) token.ThrowIfCancellationRequested(); if (!float.IsFinite(score) || score is < 0 or > 1) throw new InvalidDataException("OCR recognition probability outside original contract."); }
            var tCount = logits.Dimensions[1]; var previous = -1; var pieces = new List<(string Text, int Start, int End, float Confidence)>();
            for (var t = 0; t < tCount; t++)
            {
                token.ThrowIfCancellationRequested(); var best = 0; var score = logits[0, t, 0]; for (var c = 1; c < 18385; c++) if (logits[0, t, c] > score) { score = logits[0, t, c]; best = c; }
                if (best != 0 && best != previous) pieces.Add((_dictionary[best], t, t + 1, score)); else if (best != 0 && pieces.Count > 0) { var last = pieces[^1]; pieces[^1] = last with { End = t + 1, Confidence = Math.Max(last.Confidence, score) }; } previous = best;
            }
            evidence(new { tile = tile.Index, global, globalSupport, actualCtcText = string.Concat(pieces.Select(p => p.Text)), pieces = pieces.Select(p => new { p.Text, p.Start, p.End, p.Confidence }), stage = "actual-owned-recognition-before-confidence-guard" });
            if (pieces.Count == 0 || pieces.Any(p => p.Confidence < .8f)) throw new InvalidDataException("OCRで判読できない文字があります。空欄には置き換えません。");
            var glyphs = pieces.Where(p => !string.IsNullOrWhiteSpace(p.Text)).Select(p => new PdfGlyph(p.Text, global.X + p.Start * box.Width / tCount, global.Y, Math.Max(.1, (p.End - p.Start) * box.Width / tCount), global.Height)).ToArray();
            regions.Add(new(tile.Index, global, globalSupport, glyphs));
        }
        return regions;
    }
}
