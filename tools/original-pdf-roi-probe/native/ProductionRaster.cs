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
        internal long ObservedPixels => _pixels;
        internal static Action<long>? GlobalWorkObserver;
        public void Step(long amount = 1)
        {
            GlobalWorkObserver?.Invoke(amount);
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
            return starts.Count == 1 && ends.Count == 1 && start <= starts.Single() && starts.Single() < end + 1 &&
                start <= ends.Single() && ends.Single() < end + 1 && starts.Single() < ends.Single();
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
    public IReadOnlyList<PdfRule> Rules(CancellationToken token = default, bool retainClosedInterior = false) => Rules(token, new PixelWork(token), retainClosedInterior);
    internal IReadOnlyList<PdfRule> Rules(CancellationToken token, PixelWork work, bool retainClosedInterior = false)
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
        if (retainClosedInterior)
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
                if (last - first < 40) continue;
                clipped.Add(line.Vertical ? new(line.X1, first, line.X2, last) : new(first, line.Y1, last, line.Y2));
            }
            connectedRules = clipped.ToArray();
        }
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
