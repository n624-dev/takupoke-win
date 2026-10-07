using System.Text.Json;
using System.Security.Cryptography;
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
    private sealed class PhysicalStroke(PdfRule first)
    {
        public List<PdfRule> Lanes { get; } = [first];
        private double _minStart = first.Vertical ? first.Y1 : first.X1, _maxStart = first.Vertical ? first.Y1 : first.X1;
        private double _minEnd = first.Vertical ? first.Y2 : first.X2, _maxEnd = first.Vertical ? first.Y2 : first.X2;
        public bool CanAppend(PdfRule line)
        {
            var start = line.Vertical ? line.Y1 : line.X1; var end = line.Vertical ? line.Y2 : line.X2;
            return Math.Max(_maxStart, start) - Math.Min(_minStart, start) <= 2 &&
                Math.Max(_maxEnd, end) - Math.Min(_minEnd, end) <= 2;
        }
        public bool TouchesEndpointChain(PdfRule line)
        {
            var last = Lanes[^1];
            return line.Vertical ? Math.Abs(last.Y1-line.Y1)<=2 && Math.Abs(last.Y2-line.Y2)<=2
                : Math.Abs(last.X1-line.X1)<=2 && Math.Abs(last.X2-line.X2)<=2;
        }
        public void Append(PdfRule line)
        {
            var start = line.Vertical ? line.Y1 : line.X1; var end = line.Vertical ? line.Y2 : line.X2;
            _minStart = Math.Min(_minStart, start); _maxStart = Math.Max(_maxStart, start);
            _minEnd = Math.Min(_minEnd, end); _maxEnd = Math.Max(_maxEnd, end); Lanes.Add(line);
        }
    }
    // Only Rules can mint this inventory. Physical lanes retain their original
    // merged-border identity; callers cannot attach provenance to guessed lines.
    private sealed class PrintedRuleInventory(PdfRule[] rules, Dictionary<PdfRule, PdfRule[]> lanes,
        RecoveryRaster owner, byte[] digest) : System.Collections.ObjectModel.ReadOnlyCollection<PdfRule>(rules)
    {
        public Dictionary<PdfRule, PdfRule[]> Lanes { get; } = lanes;
        public bool Matches(RecoveryRaster raster, CancellationToken token, PixelWork work)
            => ReferenceEquals(owner, raster) && CryptographicOperations.FixedTimeEquals(digest, raster.PixelDigest(token, work));
    }
    private byte[] PixelDigest(CancellationToken token, PixelWork work)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        const int chunk = 65536;
        for (var offset = 0; offset < Bgra.Length; offset += chunk)
        {
            token.ThrowIfCancellationRequested(); var count = Math.Min(chunk, Bgra.Length - offset);
            work.Step(count / 4); hash.AppendData(Bgra, offset, count);
        }
        return hash.GetHashAndReset();
    }
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
        var inventory = rules as PrintedRuleInventory;
        if (inventory is not null && !inventory.Matches(this, token, work)) inventory = null;
        bool Ink(int x, int y) { var p = (y * Width + x) * 4; return Bgra[p] != 255 || Bgra[p + 1] != 255 || Bgra[p + 2] != 255; }
        bool StrokeRange(bool horizontal, int lane, int first, int last, out int start, out int end)
        {
            start = first; end = last;
            bool At(int p) { work.Step(); return horizontal ? Ink(p, lane) : Ink(lane, p); }
            while (start <= end && !At(start) && start - first < 2) { start++; }
            while (end >= start && !At(end) && last - end < 2) { end--; }
            if (start >= end) return false;
            for (var p = start; p <= end; p++) if (!At(p)) return false;
            return true;
        }
        bool PrintedPerpendicular(PdfRule peer, bool horizontal, int lane)
        {
            var center = horizontal ? peer.X1 : peer.Y1;
            var first = (int)Math.Ceiling(horizontal ? peer.Y1 : peer.X1);
            var last = (int)Math.Floor(horizontal ? peer.Y2 : peer.X2);
            var capacity = horizontal ? Height : Width;
            first = Math.Clamp(first, 0, capacity - 1); last = Math.Clamp(last, 0, capacity - 1);
            if (last - first + 1 < Math.Max(40, capacity / 40) || lane < first || lane > last) return false;
            foreach (var perpendicularLane in new[] { (int)Math.Floor(center), (int)Math.Ceiling(center) }.Distinct())
            {
                if (perpendicularLane < 0 || perpendicularLane >= (horizontal ? Width : Height)) continue;
                work.Step(); // Final intersection lookup below, in addition to the support scan.
                if (StrokeRange(!horizontal, perpendicularLane, first, last, out var start, out var end) &&
                    start <= lane && lane <= end && (horizontal ? Ink(perpendicularLane, lane) : Ink(lane, perpendicularLane))) return true;
            }
            return false;
        }
        bool SupportedEndpoints(PdfRule rule, bool horizontal, int lane, int start, int end)
        {
            var first = horizontal ? rule.X1 : rule.Y1; var last = horizontal ? rule.X2 : rule.Y2;
            var starts = new HashSet<double>(); var ends = new HashSet<double>();
            foreach (var peer in rules)
            {
                work.Step();
                if (horizontal ? !peer.Vertical : !peer.Horizontal) continue;
                var center = horizontal ? peer.X1 : peer.Y1;
                if (Math.Abs(center - first) > 3 && Math.Abs(center - last) > 3) continue;
                if (!PrintedPerpendicular(peer, horizontal, lane)) continue;
                if (Math.Abs(center - first) <= 3) starts.Add(center);
                if (Math.Abs(center - last) <= 3) ends.Add(center);
            }
            // Pixel support is [start,end+1). No tolerance inflates it around
            // a measured perpendicular center, and competing rails fail closed.
            if (starts.Count != 1 || ends.Count != 1 || starts.Single() >= ends.Single()) return false;
            if (start <= starts.Single() && starts.Single() < end + 1 && start <= ends.Single() && ends.Single() < end + 1) return true;
            if (inventory is null || !inventory.Lanes.TryGetValue(rule, out var ownLanes) ||
                !ownLanes.Any(raw => horizontal ? raw.Y1 == lane : raw.X1 == lane)) return false;
            bool OnePhysicalStroke(PdfRule[] raw)
            {
                var axes = raw.Select(line => line.Vertical ? line.X1 : line.Y1).Distinct().Order().ToArray();
                return axes.Zip(axes.Skip(1), (a, b) => b - a).All(gap => gap == 1);
            }
            if (!OnePhysicalStroke(ownLanes)) return false;
            // A rounded center never proves ownership. Both ends must be actual
            // intersections with continuous raw lanes of the unique peer border.
            // Requiring the exact printed endpoints excludes an attached ink tail.
            bool ActualEndpoint(double center, int endpoint)
            {
                var peers = rules.Where(peer => (horizontal ? peer.Vertical && peer.X1 == center : peer.Horizontal && peer.Y1 == center)).ToArray();
                if (peers.Length != 1 || !inventory.Lanes.TryGetValue(peers[0], out var peerLanes) || !OnePhysicalStroke(peerLanes)) return false;
                foreach (var raw in peerLanes)
                {
                    work.Step();
                    var axis = horizontal ? raw.X1 : raw.Y1;
                    var firstRaw = (int)(horizontal ? raw.Y1 : raw.X1); var lastRaw = (int)(horizontal ? raw.Y2 : raw.X2);
                    if (axis != endpoint || lane < firstRaw || lane > lastRaw) continue;
                    if (StrokeRange(!horizontal, endpoint, firstRaw, lastRaw, out var actualStart, out var actualEnd) &&
                        actualStart == firstRaw && actualEnd == lastRaw) return true;
                }
                return false;
            }
            return ActualEndpoint(starts.Single(), start) && ActualEndpoint(ends.Single(), end);
        }
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
                {
                    token.ThrowIfCancellationRequested(); work.Step(last - first + 1L);
                    if (!StrokeRange(true, y, first, last, out var start, out var end)) continue;
                    if ((start != first || end != last) && !SupportedEndpoints(rule, true, y, start, end)) continue;
                    for (var x = start; x <= end; x++) mask[y * Width + x] = true;
                }
            }
            else if (rule.Vertical)
            {
                var first = Math.Clamp((int)Math.Ceiling(Math.Min(rule.Y1, rule.Y2)), 0, Height - 1); var last = Math.Clamp((int)Math.Floor(Math.Max(rule.Y1, rule.Y2)), 0, Height - 1);
                for (var x = Math.Max(0, (int)Math.Floor(rule.X1) - 2); x <= Math.Min(Width - 1, (int)Math.Ceiling(rule.X1) + 2); x++)
                {
                    token.ThrowIfCancellationRequested(); work.Step(last - first + 1L);
                    if (!StrokeRange(false, x, first, last, out var start, out var end)) continue;
                    if ((start != first || end != last) && !SupportedEndpoints(rule, false, x, start, end)) continue;
                    for (var y = start; y <= end; y++) mask[y * Width + x] = true;
                }
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
        // Collapse only consecutive physical lanes with bounded endpoint spread.
        // Integer coordinate buckets can split one stroke at an arbitrary /3
        // boundary. A gap or competing assignment cannot certify one stroke.
        var rawLanes = new Dictionary<PdfRule, PdfRule[]>();
        PdfRule Remember(List<PdfRule> run) { var rule = Merge(run); rawLanes.Add(rule, run.ToArray()); return rule; }
        var strokes = new List<PhysicalStroke>(); var mergeWork = 0; var ambiguousMerge = false;
        foreach (var orientation in lines.GroupBy(line => line.Vertical))
        {
            var previous = new List<PhysicalStroke>(); var previousAxis = double.NegativeInfinity;
            foreach (var lane in orientation.GroupBy(line => line.Vertical ? line.X1 : line.Y1).OrderBy(group => group.Key))
            {
                token.ThrowIfCancellationRequested(); var current = lane.ToArray();
                var matches = current.Select(line => previousAxis + 1 == lane.Key ? previous.Where(stroke =>
                {
                    if (++mergeWork > 1_000_000) throw RecoveryWorkLimits.Exceeded("OCRの罫線比較数が上限を超えています。");
                    if (mergeWork % 128 == 0) token.ThrowIfCancellationRequested();
                    if (!stroke.TouchesEndpointChain(line)) return false;
                    if (!stroke.CanAppend(line)) { ambiguousMerge = true; return false; }
                    return true;
                }).ToArray() : []).ToArray();
                var claims = matches.SelectMany(items => items).GroupBy(stroke => stroke).ToDictionary(group => group.Key, group => group.Count());
                if (ambiguousMerge || matches.Any(items => items.Length > 1) || claims.Values.Any(count => count > 1)) return [];
                var next = new List<PhysicalStroke>();
                for (var index = 0; index < current.Length; index++)
                {
                    if (matches[index].Length == 1 && claims[matches[index][0]] == 1)
                    {
                        var stroke = matches[index][0]; stroke.Append(current[index]); next.Add(stroke);
                    }
                    else { var stroke = new PhysicalStroke(current[index]); strokes.Add(stroke); next.Add(stroke); }
                }
                previous = next; previousAxis = lane.Key;
            }
        }
        // Keep the established enumeration contract for unchanged borders.
        // These buckets affect order only, never membership or pixel proof.
        static (bool Vertical, int Start, int End) OrderKey(PdfRule line) =>
            (line.Vertical, (int)(line.Vertical ? line.Y1 : line.X1)/3, (int)(line.Vertical ? line.Y2 : line.X2)/3);
        var order = lines.GroupBy(OrderKey).Select((group,index) => (group.Key,index)).ToDictionary(item => item.Key,item => item.index);
        var merged = strokes.OrderBy(stroke => stroke.Lanes.Min(line => order[OrderKey(line)]))
            .ThenBy(stroke => stroke.Lanes[0].Vertical ? stroke.Lanes[0].X1 : stroke.Lanes[0].Y1)
            .Select(stroke => Remember(stroke.Lanes)).ToArray();
        // Long isolated character strokes (一 / I) are ink, not table borders.
        // A raster rule must connect to a perpendicular border at both ends.
        var comparisonWork = 0;
        var certified = FilterConnectedRules(merged);
        if (certified.Length > 0) return new PrintedRuleInventory(certified, rawLanes, this, PixelDigest(token, work));
        // Only a failed complete rule graph uses the interior fallback. Already
        // certified stroke endpoints remain unchanged, including thick corners.
        {
            var clipped = new List<PdfRule>();
            foreach (var line in merged)
            {
                token.ThrowIfCancellationRequested();
                var positions = new List<double>();
                foreach (var other in merged)
                {
                    if (++comparisonWork > 1_000_000) throw RecoveryWorkLimits.Exceeded("OCRの罫線比較数が上限を超えています。");
                    if (comparisonWork % 128 == 0) token.ThrowIfCancellationRequested();
                    if (line.Vertical == other.Vertical) continue;
                    var position = line.Vertical ? other.Y1 : other.X1;
                    var start = line.Vertical ? line.Y1 : line.X1;
                    var end = line.Vertical ? line.Y2 : line.X2;
                    var axis = line.Vertical ? line.X1 : line.Y1;
                    var crossingStart = line.Vertical ? other.X1 : other.Y1;
                    var crossingEnd = line.Vertical ? other.X2 : other.Y2;
                    if (position >= start && position <= end && axis >= crossingStart - 3 && axis <= crossingEnd + 3)
                        positions.Add(position);
                }
                if (positions.Count < 2) continue;
                var first = positions.Min(); var last = positions.Max();
                if (last - first < 39) continue;
                clipped.Add(line.Vertical ? new(line.X1, first, line.X2, last) : new(first, line.Y1, last, line.Y2));
            }
            return FilterConnectedRules(clipped.ToArray());
        }
        PdfRule[] FilterConnectedRules(PdfRule[] connectedRules)
        {
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
        }
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
    public IReadOnlyList<double> NativeConfidences { get; private set; } = [];
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
        => ReadCore(image, token, retainUncertain: false);
    // Full bounded capture for the app-owned last-resort route. Confidence is
    // retained unchanged; this output is not authorized for automatic adoption.
    public IReadOnlyList<PdfGlyph> ReadForManualCapture(RecoveryRaster image, CancellationToken token = default)
        => ReadCore(image, token, retainUncertain: true);
    // Research measurement cannot produce usable glyphs or a recovery capture.
    // Geometry, model-shape, numeric and cancellation guards remain mandatory.
    internal void ObserveAllRecognition(RecoveryRaster image, CancellationToken token)
    {
        if (RecognitionObserver is null) throw new InvalidOperationException("A diagnostic observer is required.");
        try
        {
            var discarded = ReadCore(image, token, retainUncertain: false, observationOnly: true);
            if (discarded.Count != 0) throw new InvalidOperationException("Diagnostic recognition cannot return source glyphs.");
        }
        finally { RecognizedBoxes = []; NativeConfidences = []; }
    }
    private IReadOnlyList<PdfGlyph> ReadCore(RecoveryRaster image, CancellationToken token, bool retainUncertain, bool observationOnly = false)
    {
        RecognizedBoxes = []; NativeConfidences = [];
        if (!image.Valid) throw new InvalidDataException("OCR画像のサイズが不正です。");
        var ratio = Math.Min(1, 960d / Math.Max(image.Width, image.Height)); var dw = Math.Max(32, (int)Math.Round(image.Width * ratio / 32) * 32); var dh = Math.Max(32, (int)Math.Round(image.Height * ratio / 32) * 32);
        using var detection = _detector.Run([NamedOnnxValue.CreateFromTensor("x", OcrInputTransform.Detection(image, new(0, 0, image.Width, image.Height), dw, dh, token))]); token.ThrowIfCancellationRequested();
        var map = detection.First().AsTensor<float>(); OcrDetectorProbability.ValidateMapShape(map.Dimensions, dw, dh);
        // Keep the native output untouched. Only the detector's declared
        // probability map is normalized; each buffer stays bounded by 960².
        var probabilities = new float[dw * dh];
        for (var y = 0; y < dh; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < dw; x++)
                probabilities[y * dw + x] = OcrDetectorProbability.Normalize(map[0, 0, y, x]);
        }
        var visited = new bool[dw * dh]; var boxes = new List<RecoveryBox>();
        for (var y = 0; y < dh; y++) for (var x = 0; x < dw; x++)
        {
            if (visited[y * dw + x] || probabilities[y * dw + x] < .3) continue;
            var queue = new Queue<(int X, int Y)>(); queue.Enqueue((x, y)); visited[y * dw + x] = true; var left = x; var top = y; var right = x; var bottom = y; double score = 0; var count = 0;
            while (queue.TryDequeue(out var p)) { count++; score += probabilities[p.Y * dw + p.X]; left = Math.Min(left, p.X); right = Math.Max(right, p.X); top = Math.Min(top, p.Y); bottom = Math.Max(bottom, p.Y); foreach (var (nx, ny) in new[] { (p.X - 1, p.Y), (p.X + 1, p.Y), (p.X, p.Y - 1), (p.X, p.Y + 1) }) if (nx >= 0 && ny >= 0 && nx < dw && ny < dh && !visited[ny * dw + nx] && probabilities[ny * dw + nx] >= .3) { visited[ny * dw + nx] = true; queue.Enqueue((nx, ny)); } }
            token.ThrowIfCancellationRequested(); if (count < 6 || score / count < .6) continue;
            var margin = Math.Max(1, (bottom - top + 1) * .25); var bx = Math.Max(0, (left - margin) * image.Width / dw); var by = Math.Max(0, (top - margin) * image.Height / dh); var ex = Math.Min(image.Width, (right + 1 + margin) * image.Width / dw); var ey = Math.Min(image.Height, (bottom + 1 + margin) * image.Height / dh);
            boxes.Add(new(bx, by, ex - bx, ey - by)); if (boxes.Count > 10000) throw new InvalidDataException("OCR候補数が上限を超えています。");
        }
        // Complete ORIGINAL connected ink support before recognizer input.
        // Coverage and CTC geometry use this same crop, never a coverage-only
        // enlarged rectangle that could conceal omitted disconnected text.
        var recognitionBoxes = OcrCropCompleteness.Complete(image, boxes, token);
        var output = new List<PdfGlyph>(); var confidences = new List<double>(); var order = 0; var line = 0;
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
            if (observationOnly) continue;
            if (pieces.Count == 0 || pieces.Any(p => p.Confidence < .8f && (!retainUncertain || string.IsNullOrWhiteSpace(p.Text))))
            {
                // Numeric diagnostics never contain source strings or images.
                var failure = new InvalidDataException("OCRで判読できない文字があります。空欄には置き換えません。");
                failure.Data["OcrRecognitionPieceCount"] = pieces.Count;
                failure.Data["OcrRecognitionLowWhitespaceCount"] = pieces.Count(p => p.Confidence < .8f && string.IsNullOrWhiteSpace(p.Text));
                failure.Data["OcrRecognitionLowBodyCount"] = pieces.Count(p => p.Confidence < .8f && !string.IsNullOrWhiteSpace(p.Text));
                failure.Data["OcrRecognitionCrop"] = new double[] { box.X, box.Y, box.Width, box.Height };
                failure.Data["OcrRecognitionInput"] = new[] { input.ValidWidth, input.InputWidth, tCount };
                failure.Data["OcrRecognitionWhitespacePieces"] = pieces.Where(p => string.IsNullOrWhiteSpace(p.Text)).Select(p => new { Codes = p.Text.Select(c => (int)c).ToArray(), p.Start, p.End, p.Confidence }).ToArray();
                throw failure;
            }
            // CTC time positions are retained as source geometry, never equally
            // spaced boxes inferred from a generated string.
            foreach (var piece in pieces)
            {
                output.Add(input.SourceGlyph(piece.Text, piece.Start, piece.End, tCount, line, order++));
                confidences.Add(piece.Confidence);
            }
            line++;
        }
        token.ThrowIfCancellationRequested(); RecognizedBoxes = recognitionBoxes; NativeConfidences = confidences.ToArray(); return output;
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
