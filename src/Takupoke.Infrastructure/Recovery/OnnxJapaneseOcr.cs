using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;

namespace Takupoke.Infrastructure.Recovery;
public sealed record RecoveryRaster(int Width, int Height, byte[] Bgra)
{
    public bool Valid => Width is > 0 and <= 4096 && Height is > 0 and <= 4096 && Bgra.Length == checked(Width * Height * 4);
    public bool InkFree(RecoveryBox box)
    {
        if (!Valid || !box.Valid) return false;
        // Ignore the one-pixel ruled border, but preserve faint text inside it.
        var left = Math.Clamp((int)Math.Ceiling(box.X) + 2, 0, Width); var top = Math.Clamp((int)Math.Ceiling(box.Y) + 2, 0, Height);
        var right = Math.Clamp((int)Math.Floor(box.X + box.Width) - 2, 0, Width); var bottom = Math.Clamp((int)Math.Floor(box.Y + box.Height) - 2, 0, Height);
        if (right <= left || bottom <= top) return false;
        for (var y = top; y < bottom; y++) for (var x = left; x < right; x++) { var p = (y * Width + x) * 4; if (Bgra[p] < 245 || Bgra[p + 1] < 245 || Bgra[p + 2] < 245) return false; }
        return true;
    }
    public bool HasUnrecognizedInk(IReadOnlyList<RecoveryBox> textBoxes, IReadOnlyList<PdfRule> rules)
    {
        if (!Valid) return true;
        var covered = new bool[Width * Height];
        void Mark(int left, int top, int right, int bottom) { for (var y = Math.Max(0, top); y <= Math.Min(Height - 1, bottom); y++) for (var x = Math.Max(0, left); x <= Math.Min(Width - 1, right); x++) covered[y * Width + x] = true; }
        foreach (var box in textBoxes) Mark((int)Math.Floor(box.X) - 1, (int)Math.Floor(box.Y) - 1, (int)Math.Ceiling(box.X + box.Width) + 1, (int)Math.Ceiling(box.Y + box.Height) + 1);
        foreach (var rule in rules) Mark((int)Math.Floor(Math.Min(rule.X1, rule.X2)) - 2, (int)Math.Floor(Math.Min(rule.Y1, rule.Y2)) - 2, (int)Math.Ceiling(Math.Max(rule.X1, rule.X2)) + 2, (int)Math.Ceiling(Math.Max(rule.Y1, rule.Y2)) + 2);
        for (var i = 0; i < covered.Length; i++) if (!covered[i] && (Bgra[i * 4] < 230 || Bgra[i * 4 + 1] < 230 || Bgra[i * 4 + 2] < 230)) return true;
        return false;
    }
    public IReadOnlyList<PdfRule> Rules()
    {
        var lines = new List<PdfRule>();
        bool Dark(int x, int y) { var p = (y * Width + x) * 4; return (Bgra[p] + Bgra[p + 1] + Bgra[p + 2]) / 3 < 160; }
        for (var y = 0; y < Height; y++) { var start = -1; for (var x = 0; x <= Width; x++) { if (x < Width && Dark(x, y)) { if (start < 0) start = x; } else if (start >= 0) { if (x - start >= Math.Max(40, Width / 40)) lines.Add(new(start, y, x - 1, y)); start = -1; } } }
        for (var x = 0; x < Width; x++) { var start = -1; for (var y = 0; y <= Height; y++) { if (y < Height && Dark(x, y)) { if (start < 0) start = y; } else if (start >= 0) { if (y - start >= Math.Max(40, Height / 40)) lines.Add(new(x, start, x, y - 1)); start = -1; } } }
        // Collapse adjacent scanlines from the same stroke to one centerline.
        return lines.GroupBy(l => (l.Vertical, A: (int)(l.Vertical ? l.Y1 : l.X1) / 3, B: (int)(l.Vertical ? l.Y2 : l.X2) / 3))
            .SelectMany(g => { var ordered = g.OrderBy(l => l.Vertical ? l.X1 : l.Y1).ToArray(); var result = new List<PdfRule>(); var run = new List<PdfRule>(); foreach (var line in ordered) { if (run.Count > 0 && (line.Vertical ? line.X1 - run[^1].X1 : line.Y1 - run[^1].Y1) > 2) { result.Add(Merge(run)); run.Clear(); } run.Add(line); } if (run.Count > 0) result.Add(Merge(run)); return result; }).ToArray();
        static PdfRule Merge(List<PdfRule> run) => run[0].Vertical ? new(run.Average(l => l.X1), run.Min(l => l.Y1), run.Average(l => l.X2), run.Max(l => l.Y2)) : new(run.Min(l => l.X1), run.Average(l => l.Y1), run.Max(l => l.X2), run.Average(l => l.Y2));
    }
}
/// Independent, CPU-only Japanese OCR for unpackaged Windows. No telemetry,
/// network transport, package identity or Copilot+ hardware is required.
public sealed class OnnxJapaneseOcr : IDisposable
{
    private readonly InferenceSession _detector, _recognizer;
    private readonly string[] _dictionary;
    public IReadOnlyList<RecoveryBox> RecognizedBoxes { get; private set; } = [];
    public OnnxJapaneseOcr(string detector, string recognizer, string dictionary)
    {
        Environment.SetEnvironmentVariable("ORT_TELEMETRY_DISABLED", "1");
        OrtEnv.Instance().DisableTelemetryEvents();
        using var options = new SessionOptions { IntraOpNumThreads = 2, InterOpNumThreads = 1, GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL, LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
        _detector = new(detector, options); InferenceSession? recognizerSession = null;
        try
        {
            recognizerSession = new(recognizer, options);
            _dictionary = JsonSerializer.Deserialize<string[]>(File.ReadAllBytes(dictionary)) ?? throw new InvalidDataException("OCR辞書がありません。");
            if (_dictionary.Length != 18385 || _dictionary[0] != "" || _dictionary[^1] != " " || _detector.InputMetadata.Count != 1 || recognizerSession.InputMetadata.Count != 1 || _detector.InputMetadata["x"].Dimensions.Length != 4 || recognizerSession.InputMetadata["x"].Dimensions.Length != 4 || recognizerSession.InputMetadata["x"].Dimensions[1] != 3 || recognizerSession.InputMetadata["x"].Dimensions[2] != 48) throw new InvalidDataException("OCRモデルの形状が一致しません。");
            _recognizer = recognizerSession;
        }
        catch { recognizerSession?.Dispose(); _detector.Dispose(); throw; }
    }
    private static DenseTensor<float> Image(RecoveryRaster image, RecoveryBox crop, int width, int height, bool detection)
    {
        var tensor = new DenseTensor<float>([1, 3, height, width]); float[] mean = [.485f, .456f, .406f], std = [.229f, .224f, .225f];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var sx = Math.Clamp((int)(crop.X + (x + .5) * crop.Width / width), 0, image.Width - 1); var sy = Math.Clamp((int)(crop.Y + (y + .5) * crop.Height / height), 0, image.Height - 1);
            for (var c = 0; c < 3; c++) { var value = image.Bgra[(sy * image.Width + sx) * 4 + c]; tensor[0, c, y, x] = detection ? (value / 255f - mean[c]) / std[c] : value / 127.5f - 1; }
        }
        return tensor;
    }
    public IReadOnlyList<PdfGlyph> Read(RecoveryRaster image, CancellationToken token = default)
    {
        RecognizedBoxes = [];
        if (!image.Valid) throw new InvalidDataException("OCR画像のサイズが不正です。");
        var ratio = Math.Min(1, 960d / Math.Max(image.Width, image.Height)); var dw = Math.Max(32, (int)Math.Round(image.Width * ratio / 32) * 32); var dh = Math.Max(32, (int)Math.Round(image.Height * ratio / 32) * 32);
        using var detection = _detector.Run([NamedOnnxValue.CreateFromTensor("x", Image(image, new(0, 0, image.Width, image.Height), dw, dh, true))]); token.ThrowIfCancellationRequested();
        var map = detection.First().AsTensor<float>(); if (map.Dimensions.Length != 4 || map.Dimensions[2] != dh || map.Dimensions[3] != dw) throw new InvalidDataException("OCR検出モデルの出力形状が一致しません。");
        var visited = new bool[dw * dh]; var boxes = new List<RecoveryBox>();
        for (var y = 0; y < dh; y++) for (var x = 0; x < dw; x++)
        {
            if (visited[y * dw + x] || map[0, 0, y, x] < .3) continue;
            var queue = new Queue<(int X, int Y)>(); queue.Enqueue((x, y)); visited[y * dw + x] = true; var left = x; var top = y; var right = x; var bottom = y; double score = 0; var count = 0;
            while (queue.TryDequeue(out var p)) { count++; score += map[0, 0, p.Y, p.X]; left = Math.Min(left, p.X); right = Math.Max(right, p.X); top = Math.Min(top, p.Y); bottom = Math.Max(bottom, p.Y); foreach (var (nx, ny) in new[] { (p.X - 1, p.Y), (p.X + 1, p.Y), (p.X, p.Y - 1), (p.X, p.Y + 1) }) if (nx >= 0 && ny >= 0 && nx < dw && ny < dh && !visited[ny * dw + nx] && map[0, 0, ny, nx] >= .3) { visited[ny * dw + nx] = true; queue.Enqueue((nx, ny)); } }
            token.ThrowIfCancellationRequested(); if (count < 6 || score / count < .6) continue;
            var margin = Math.Max(1, (bottom - top + 1) * .25); var bx = Math.Max(0, (left - margin) * image.Width / dw); var by = Math.Max(0, (top - margin) * image.Height / dh); var ex = Math.Min(image.Width, (right + 1 + margin) * image.Width / dw); var ey = Math.Min(image.Height, (bottom + 1 + margin) * image.Height / dh);
            boxes.Add(new(bx, by, ex - bx, ey - by)); if (boxes.Count > 10000) throw new InvalidDataException("OCR候補数が上限を超えています。");
        }
        var output = new List<PdfGlyph>(); var order = 0; var line = 0;
        foreach (var box in boxes.OrderBy(b => b.Y).ThenBy(b => b.X))
        {
            token.ThrowIfCancellationRequested(); var width = Math.Clamp((int)Math.Ceiling(box.Width / box.Height * 48), 16, 960);
            using var recognition = _recognizer.Run([NamedOnnxValue.CreateFromTensor("x", Image(image, box, width, 48, false))]); var logits = recognition.First().AsTensor<float>();
            if (logits.Dimensions.Length != 3 || logits.Dimensions[2] != 18385) throw new InvalidDataException("OCR認識モデルの出力形状が一致しません。");
            var tCount = logits.Dimensions[1]; var previous = -1; var pieces = new List<(string Text, int Start, int End, float Confidence)>();
            for (var t = 0; t < tCount; t++) { var best = 0; var score = logits[0, t, 0]; for (var c = 1; c < 18385; c++) if (logits[0, t, c] > score) { score = logits[0, t, c]; best = c; } if (best != 0 && best != previous) pieces.Add((_dictionary[best], t, t + 1, score)); else if (best != 0 && pieces.Count > 0) { var last = pieces[^1]; pieces[^1] = last with { End = t + 1, Confidence = Math.Max(last.Confidence, score) }; } previous = best; }
            if (pieces.Count == 0 || pieces.Any(p => p.Confidence < .8f)) throw new InvalidDataException("OCRで判読できない文字があります。空欄には置き換えません。");
            // CTC time positions are retained as source geometry, never equally
            // spaced boxes inferred from a generated string.
            foreach (var piece in pieces.Where(p => !string.IsNullOrWhiteSpace(p.Text))) { var left = box.X + piece.Start * box.Width / tCount; var right = box.X + piece.End * box.Width / tCount; output.Add(new(piece.Text, left, box.Y, Math.Max(.1, right - left), box.Height, line, order++)); }
            line++;
        }
        token.ThrowIfCancellationRequested(); RecognizedBoxes = boxes; return output;
    }
    public void SmokeTest(CancellationToken token)
    {
        var image = new RecoveryRaster(64, 64, Enumerable.Repeat((byte)255, 64 * 64 * 4).ToArray()); Read(image, token);
        using var output = _recognizer.Run([NamedOnnxValue.CreateFromTensor("x", Image(image, new(0, 0, 64, 64), 320, 48, false))]);
        var logits = output.First().AsTensor<float>(); if (logits.Dimensions.Length != 3 || logits.Dimensions[2] != 18385) throw new InvalidDataException("OCRモデルの出力形状が一致しません。"); token.ThrowIfCancellationRequested();
    }
    public void Dispose() { _detector.Dispose(); _recognizer.Dispose(); }
}
