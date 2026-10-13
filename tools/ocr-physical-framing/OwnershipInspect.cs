using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Recovery;

// Read-only diagnosis of the original ownership refusal; no text recognition,
// masking changes, output repairs or recovery/adoption credit.
internal static class OwnershipInspect
{
    internal static object Analyze(RecoveryRaster image, IReadOnlyList<RecoveryBox> crops,
        bool[] ruleMask, CancellationToken token)
    {
        var work = new RecoveryRaster.PixelWork(token);
        var seeds = new short[image.Width * image.Height];
        for (var owner = 0; owner < crops.Count; owner++)
        {
            var b = crops[owner];
            var left = Math.Max(0, (int)Math.Ceiling(b.X - .5)); var top = Math.Max(0, (int)Math.Ceiling(b.Y - .5));
            var right = Math.Min(image.Width - 1, (int)Math.Floor(b.X + b.Width - .5));
            var bottom = Math.Min(image.Height - 1, (int)Math.Floor(b.Y + b.Height - .5));
            for (var y = top; y <= bottom; y++) for (var x = left; x <= right; x++)
            { work.Step(); var index = y * image.Width + x; seeds[index] = seeds[index] == 0 ? (short)(owner + 1) : (short)-1; }
        }
        var visited = new bool[seeds.Length]; var components = new List<object>(); var failures = 0;
        bool Ink(int index)
        {
            var p = index * 4;
            return image.Bgra[p] != 255 || image.Bgra[p + 1] != 255 || image.Bgra[p + 2] != 255;
        }
        for (var start = 0; start < visited.Length; start++)
        {
            work.Step(); if (visited[start] || !Ink(start)) continue;
            var queue = new List<int> { start }; visited[start] = true; var owners = new HashSet<int>();
            var overlapSeed = false; var rulePixels = 0; var textPixels = 0; var unseeded = 0;
            var left = image.Width; var top = image.Height; var right = 0; var bottom = 0;
            var unmaskedSamples = new List<object>();
            for (var cursor = 0; cursor < queue.Count; cursor++)
            {
                work.Step(); var index = queue[cursor]; var x = index % image.Width; var y = index / image.Width;
                left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y);
                if (ruleMask[index]) rulePixels++;
                else
                {
                    textPixels++; var seed = seeds[index]; overlapSeed |= seed == -1;
                    if (seed > 0) owners.Add(seed); else if (seed == 0) unseeded++;
                    if (unmaskedSamples.Count < 24) unmaskedSamples.Add(new { x, y, seed,
                        b = image.Bgra[index * 4], g = image.Bgra[index * 4 + 1], r = image.Bgra[index * 4 + 2] });
                }
                for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    work.Step(); var nx = x + dx; var ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= image.Width || ny >= image.Height) continue;
                    var next = ny * image.Width + nx;
                    if (!visited[next] && Ink(next)) { visited[next] = true; queue.Add(next); }
                }
            }
            var refused = textPixels > 0 && (overlapSeed || owners.Count != 1 || rulePixels > 0);
            if (!refused) continue;
            failures++;
            if (components.Count < 64) components.Add(new { left, top, right, bottom, pixels = queue.Count,
                rulePixels, textPixels, unseeded, owners = owners.Order().ToArray(), overlapSeed, unmaskedSamples });
        }
        return new { failedComponents = failures, first64Failures = components, recognizerCalls = 0,
            scope = "Read-only physical original-ink ownership diagnosis, not changed crop acceptance or OCR quality" };
    }
}
