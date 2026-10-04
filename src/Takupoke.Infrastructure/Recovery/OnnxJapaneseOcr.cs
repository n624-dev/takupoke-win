using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;

namespace Takupoke.Infrastructure.Recovery;
internal static class RecoveryWorkLimits
{
    private const string Key = "RecoveryWorkLimitExceeded";
    internal static InvalidDataException Exceeded(string message)
    {
        var error = new InvalidDataException(message); error.Data[Key] = true; return error;
    }
    internal static bool IsExceeded(InvalidDataException error) => error.Data.Contains(Key);
}
public sealed record RecoveryRaster(int Width, int Height, byte[] Bgra)
{
    public bool Valid => Width is > 0 and <= 4096 && Height is > 0 and <= 4096 && Bgra.Length == checked(Width * Height * 4);
    internal sealed class PixelWork(CancellationToken token)
    {
        private long _pixels;
        public void Step(long amount = 1)
        {
            _pixels += amount;
            if (_pixels > 64_000_000) throw RecoveryWorkLimits.Exceeded("OCRの画素処理数が上限を超えています。");
            if (amount > 1 || _pixels % 128 == 0) token.ThrowIfCancellationRequested();
        }
    }
    public bool InkFree(RecoveryBox box, bool[]? ruleMask = null, CancellationToken token = default)
        => InkFree(box, ruleMask, token, new PixelWork(token));
    public Func<RecoveryBox, bool> InkFreeScanner(bool[]? ruleMask = null, CancellationToken token = default)
    {
        var work = new PixelWork(token);
        return box => InkFree(box, ruleMask, token, work);
    }
    private bool InkFree(RecoveryBox box, bool[]? ruleMask, CancellationToken token, PixelWork work)
    {
        token.ThrowIfCancellationRequested();
        if (!Valid || !box.Valid || box.X + box.Width > Width || box.Y + box.Height > Height || ruleMask is not null && ruleMask.Length != Width * Height) return false;
        var left = Math.Clamp((int)Math.Ceiling(box.X - .5), 0, Width); var top = Math.Clamp((int)Math.Ceiling(box.Y - .5), 0, Height);
        var right = Math.Clamp((int)Math.Ceiling(box.X + box.Width - .5), 0, Width); var bottom = Math.Clamp((int)Math.Ceiling(box.Y + box.Height - .5), 0, Height);
        if (right <= left || bottom <= top) return false;
        for (var y = top; y < bottom; y++) { token.ThrowIfCancellationRequested(); for (var x = left; x < right; x++) { work.Step(); var index = y * Width + x; if (ruleMask?[index] == true) continue; var p = index * 4; if (Bgra[p] != 255 || Bgra[p + 1] != 255 || Bgra[p + 2] != 255) return false; } }
        return true;
    }
    public bool[] RuleMask(IReadOnlyList<PdfRule> rules, CancellationToken token = default)
        => RuleMask(rules, token, new PixelWork(token));
    internal bool[] RuleMask(IReadOnlyList<PdfRule> rules, CancellationToken token, PixelWork work)
    {
        token.ThrowIfCancellationRequested();
        if (!Valid) throw new InvalidDataException("OCR画像のサイズが不正です。");
        var mask = new bool[Width * Height];
        bool Ink(int x, int y) { var p = (y * Width + x) * 4; return Bgra[p] != 255 || Bgra[p + 1] != 255 || Bgra[p + 2] != 255; }
        // A neighborhood around a centerline is not evidence of a stroke.
        // Mask only physical rows/columns continuously printed along the rule;
        // a missed glyph beside it must remain uncovered, even one pixel away.
        foreach (var rule in rules)
        {
            token.ThrowIfCancellationRequested(); work.Step();
            if (!new[] { rule.X1, rule.Y1, rule.X2, rule.Y2 }.All(double.IsFinite)) throw new InvalidDataException("OCR罫線の位置が不正です。");
            if (rule.Horizontal)
            {
                var first = Math.Clamp((int)Math.Ceiling(Math.Min(rule.X1, rule.X2)), 0, Width - 1); var last = Math.Clamp((int)Math.Floor(Math.Max(rule.X1, rule.X2)), 0, Width - 1);
                for (var y = Math.Max(0, (int)Math.Floor(rule.Y1) - 2); y <= Math.Min(Height - 1, (int)Math.Ceiling(rule.Y1) + 2); y++)
                { token.ThrowIfCancellationRequested(); work.Step(2L * (last - first + 1)); var continuous = first < last; for (var x = first; x <= last && continuous; x++) continuous = Ink(x, y); if (continuous) for (var x = first; x <= last; x++) mask[y * Width + x] = true; }
            }
            else if (rule.Vertical)
            {
                var first = Math.Clamp((int)Math.Ceiling(Math.Min(rule.Y1, rule.Y2)), 0, Height - 1); var last = Math.Clamp((int)Math.Floor(Math.Max(rule.Y1, rule.Y2)), 0, Height - 1);
                for (var x = Math.Max(0, (int)Math.Floor(rule.X1) - 2); x <= Math.Min(Width - 1, (int)Math.Ceiling(rule.X1) + 2); x++)
                { token.ThrowIfCancellationRequested(); work.Step(2L * (last - first + 1)); var continuous = first < last; for (var y = first; y <= last && continuous; y++) continuous = Ink(x, y); if (continuous) for (var y = first; y <= last; y++) mask[y * Width + x] = true; }
            }
        }
        return mask;
    }
    public bool HasUnrecognizedInk(IReadOnlyList<RecoveryBox> textBoxes, IReadOnlyList<PdfRule> rules, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!Valid) return true;
        if (textBoxes.Count > 10000 || textBoxes.Any(b => !b.Valid || b.X + b.Width > Width || b.Y + b.Height > Height)) throw new InvalidDataException("OCR文字の位置を確認できません。");
        var work = new PixelWork(token); var covered = RuleMask(rules, token, work);
        void Mark(int left, int top, int right, int bottom) { for (var y = Math.Max(0, top); y <= Math.Min(Height - 1, bottom); y++) { token.ThrowIfCancellationRequested(); work.Step(Math.Max(0, Math.Min(Width - 1, right) - Math.Max(0, left) + 1)); for (var x = Math.Max(0, left); x <= Math.Min(Width - 1, right); x++) covered[y * Width + x] = true; } }
        foreach (var box in textBoxes) Mark((int)Math.Floor(box.X) - 1, (int)Math.Floor(box.Y) - 1, (int)Math.Ceiling(box.X + box.Width) + 1, (int)Math.Ceiling(box.Y + box.Height) + 1);
        for (var i = 0; i < covered.Length; i++) { work.Step(); if (i % 4096 == 0) token.ThrowIfCancellationRequested(); if (!covered[i] && (Bgra[i * 4] != 255 || Bgra[i * 4 + 1] != 255 || Bgra[i * 4 + 2] != 255)) return true; }
        return false;
    }
    public IReadOnlyList<PdfRule> Rules(CancellationToken token = default) => Rules(token, new PixelWork(token));
    internal IReadOnlyList<PdfRule> Rules(CancellationToken token, PixelWork work)
    {
        token.ThrowIfCancellationRequested();
        if (!Valid) throw new InvalidDataException("OCR画像のサイズが不正です。");
        var lines = new List<PdfRule>();
        bool Dark(int x, int y) { work.Step(); var p = (y * Width + x) * 4; return (Bgra[p] + Bgra[p + 1] + Bgra[p + 2]) / 3 < 160; }
        for (var y = 0; y < Height; y++) { token.ThrowIfCancellationRequested(); var start = -1; for (var x = 0; x <= Width; x++) { if (x < Width && Dark(x, y)) { if (start < 0) start = x; } else if (start >= 0) { if (x - start >= Math.Max(40, Width / 40)) lines.Add(new(start, y, x - 1, y)); start = -1; } } }
        for (var x = 0; x < Width; x++) { token.ThrowIfCancellationRequested(); var start = -1; for (var y = 0; y <= Height; y++) { if (y < Height && Dark(x, y)) { if (start < 0) start = y; } else if (start >= 0) { if (y - start >= Math.Max(40, Height / 40)) lines.Add(new(x, start, x, y - 1)); start = -1; } } }
        // Collapse adjacent scanlines from the same stroke to one centerline.
        var merged = lines.GroupBy(l => (l.Vertical, A: (int)(l.Vertical ? l.Y1 : l.X1) / 3, B: (int)(l.Vertical ? l.Y2 : l.X2) / 3))
            .SelectMany(g => { var ordered = g.OrderBy(l => l.Vertical ? l.X1 : l.Y1).ToArray(); var result = new List<PdfRule>(); var run = new List<PdfRule>(); foreach (var line in ordered) { if (run.Count > 0 && (line.Vertical ? line.X1 - run[^1].X1 : line.Y1 - run[^1].Y1) > 2) { result.Add(Merge(run)); run.Clear(); } run.Add(line); } if (run.Count > 0) result.Add(Merge(run)); return result; }).ToArray();
        // Long isolated character strokes (一 / I) are ink, not table borders.
        // A raster rule must connect to a perpendicular border at both ends.
        var connectedRules = merged; var comparisonWork = 0;
        for (var pass = 0; pass < 64; pass++)
        {
            token.ThrowIfCancellationRequested();
            var vertical = connectedRules.Where(l => l.Vertical).GroupBy(l => (int)Math.Round(l.X1 / 3)).ToDictionary(g => g.Key, g => g.ToArray());
            var horizontal = connectedRules.Where(l => l.Horizontal).GroupBy(l => (int)Math.Round(l.Y1 / 3)).ToDictionary(g => g.Key, g => g.ToArray());
            bool Connected(PdfRule line, double x, double y)
            {
                token.ThrowIfCancellationRequested();
                bool Matches(PdfRule other)
                {
                    if (++comparisonWork > 1_000_000) throw RecoveryWorkLimits.Exceeded("OCRの罫線比較数が上限を超えています。");
                    if (comparisonWork % 128 == 0) token.ThrowIfCancellationRequested();
                    return line.Vertical ? Math.Abs(other.Y1 - y) <= 3 && other.X1 - 3 <= x && x <= other.X2 + 3
                        : Math.Abs(other.X1 - x) <= 3 && other.Y1 - 3 <= y && y <= other.Y2 + 3;
                }
                var index = line.Vertical ? horizontal : vertical; var key = (int)Math.Round((line.Vertical ? y : x) / 3);
                for (var bucket = key - 1; bucket <= key + 1; bucket++)
                    if (index.TryGetValue(bucket, out var candidates) && candidates.Any(Matches)) return true;
                return false;
            }
            var next = connectedRules.Where(l => Connected(l, l.X1, l.Y1) && Connected(l, l.X2, l.Y2)).ToArray();
            if (next.Length == connectedRules.Length) return next;
            connectedRules = next;
        }
        // An unstable candidate graph cannot establish a table boundary.
        return [];
        static PdfRule Merge(List<PdfRule> run) => run[0].Vertical ? new(run.Average(l => l.X1), run.Min(l => l.Y1), run.Average(l => l.X2), run.Max(l => l.Y2)) : new(run.Min(l => l.X1), run.Average(l => l.Y1), run.Max(l => l.X2), run.Average(l => l.Y2));
    }
}
internal sealed record OcrRecognitionPiece(string Text, int Start, int End, float Confidence);
internal sealed record OcrRecognitionObservation(RecoveryBox DetectedCrop, RecoveryBox RecognitionCrop,
    int ValidWidth, int InputWidth, int TimeCount, IReadOnlyList<OcrRecognitionPiece> Pieces)
{
    public bool ZeroPieces => Pieces.Count == 0;
    public bool BelowConfidence => Pieces.Any(p => p.Confidence < .8f);
}
/// Independent, CPU-only Japanese OCR for unpackaged Windows. No telemetry,
/// network transport, package identity or Copilot+ hardware is required.
public sealed class OnnxJapaneseOcr : IDisposable
{
    private readonly InferenceSession _detector, _recognizer;
    private readonly string[] _dictionary;
    public IReadOnlyList<RecoveryBox> RecognizedBoxes { get; private set; } = [];
    // Opt-in local diagnostics only. No allocation/logging when unset; an
    // observer never supplies recognition values or bypasses safety guards.
    internal Action<OcrRecognitionObservation>? RecognitionObserver { get; set; }
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
    public IReadOnlyList<PdfGlyph> Read(RecoveryRaster image, CancellationToken token = default)
    {
        RecognizedBoxes = [];
        if (!image.Valid) throw new InvalidDataException("OCR画像のサイズが不正です。");
        var ratio = Math.Min(1, 960d / Math.Max(image.Width, image.Height)); var dw = Math.Max(32, (int)Math.Round(image.Width * ratio / 32) * 32); var dh = Math.Max(32, (int)Math.Round(image.Height * ratio / 32) * 32);
        using var detection = _detector.Run([NamedOnnxValue.CreateFromTensor("x", OcrInputTransform.Detection(image, new(0, 0, image.Width, image.Height), dw, dh, token))]); token.ThrowIfCancellationRequested();
        var map = detection.First().AsTensor<float>(); if (map.Dimensions.Length != 4 || map.Dimensions[2] != dh || map.Dimensions[3] != dw) throw new InvalidDataException("OCR検出モデルの出力形状が一致しません。");
        for (var y = 0; y < dh; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < dw; x++) if (!float.IsFinite(map[0, 0, y, x]) || map[0, 0, y, x] is < 0 or > 1)
                throw new InvalidDataException("OCR検出の確信度が不正です。");
        }
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
        // Complete ORIGINAL connected ink support before recognizer input.
        // Coverage and CTC geometry use this same crop, never a coverage-only
        // enlarged rectangle that could conceal omitted disconnected text.
        var recognitionBoxes = OcrCropCompleteness.Complete(image, boxes, token);
        var output = new List<PdfGlyph>(); var order = 0; var line = 0;
        foreach (var index in Enumerable.Range(0, recognitionBoxes.Count).OrderBy(i => recognitionBoxes[i].Y).ThenBy(i => recognitionBoxes[i].X))
        {
            var box = recognitionBoxes[index];
            token.ThrowIfCancellationRequested(); var input = OcrInputTransform.Recognition(image, box, token);
            VerifyRecognitionWidth(input.InputWidth);
            using var recognition = _recognizer.Run([NamedOnnxValue.CreateFromTensor("x", input.Tensor)]); token.ThrowIfCancellationRequested(); var logits = recognition.First().AsTensor<float>();
            if (logits.Dimensions.Length != 3 || logits.Dimensions[0] != 1 || logits.Dimensions[1] <= 0 || logits.Dimensions[2] != 18385) throw new InvalidDataException("OCR認識モデルの出力形状が一致しません。");
            var checkedScores = 0;
            foreach (var score in logits)
            {
                if (++checkedScores % 4096 == 0) token.ThrowIfCancellationRequested();
                if (!float.IsFinite(score) || score is < 0 or > 1) throw new InvalidDataException("OCR認識の確信度が不正です。");
            }
            var tCount = logits.Dimensions[1]; var previous = -1; var pieces = new List<(string Text, int Start, int End, float Confidence)>();
            for (var t = 0; t < tCount; t++) { var best = 0; var score = logits[0, t, 0]; for (var c = 1; c < 18385; c++) if (logits[0, t, c] > score) { score = logits[0, t, c]; best = c; } if (best != 0 && best != previous) pieces.Add((_dictionary[best], t, t + 1, score)); else if (best != 0 && pieces.Count > 0) { var last = pieces[^1]; pieces[^1] = last with { End = t + 1, Confidence = Math.Max(last.Confidence, score) }; } previous = best; }
            // Retain already-computed pieces before the existing refusal. No
            // extra model call, alternate decoding or confidence adjustment.
            RecognitionObserver?.Invoke(new(boxes[index], box, input.ValidWidth, input.InputWidth, tCount,
                Array.AsReadOnly(pieces.Select(p => new OcrRecognitionPiece(p.Text, p.Start, p.End, p.Confidence)).ToArray())));
            if (pieces.Count == 0 || pieces.Any(p => p.Confidence < .8f)) throw new InvalidDataException("OCRで判読できない文字があります。空欄には置き換えません。");
            // CTC time positions are retained as source geometry, never equally
            // spaced boxes inferred from a generated string.
            foreach (var piece in pieces)
            {
                var source = input.SourceBox(piece.Start, piece.End, tCount);
                if (!string.IsNullOrWhiteSpace(piece.Text)) output.Add(new(piece.Text, source.X, source.Y, source.Width, source.Height, line, order++));
            }
            line++;
        }
        token.ThrowIfCancellationRequested(); RecognizedBoxes = recognitionBoxes; return output;
    }
    public void SmokeTest(CancellationToken token)
    {
        var image = new RecoveryRaster(64, 64, Enumerable.Repeat((byte)255, 64 * 64 * 4).ToArray()); Read(image, token);
        var input = OcrInputTransform.Recognition(image, new(0, 0, 64, 64), token); VerifyRecognitionWidth(input.InputWidth);
        using var output = _recognizer.Run([NamedOnnxValue.CreateFromTensor("x", input.Tensor)]);
        var logits = output.First().AsTensor<float>(); if (logits.Dimensions.Length != 3 || logits.Dimensions[0] != 1 || logits.Dimensions[1] <= 0 || logits.Dimensions[2] != 18385) throw new InvalidDataException("OCRモデルの出力形状が一致しません。"); token.ThrowIfCancellationRequested();
    }
    private void VerifyRecognitionWidth(int width)
    {
        var declared = _recognizer.InputMetadata["x"].Dimensions[3];
        if (declared > 0 && declared != width) throw new InvalidDataException("OCR認識モデルの入力幅が一致しません。");
    }
    public void Dispose() { _detector.Dispose(); _recognizer.Dispose(); }
}
