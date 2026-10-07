using Takupoke.Core.Recovery;

namespace Takupoke.Infrastructure.Recovery;

internal static class OcrCropCompleteness
{
    public static IReadOnlyList<RecoveryBox> Complete(RecoveryRaster image, IReadOnlyList<RecoveryBox> crops, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!image.Valid || crops.Count > 10000 || crops.Any(b => !new RecoveryBox(0, 0, image.Width, image.Height).Contains(b)))
            throw new InvalidDataException("OCR候補の原画像範囲が不正です。");
        // The rule scan, physical mask, crop ownership and component searches
        // share ONE existing 64M pixel-work allowance; it is never replenished.
        var work = new RecoveryRaster.PixelWork(token);
        var rules = image.Rules(token, work); var ruleMask = image.RuleMask(rules, token, work);
        var length = image.Width * image.Height;
        var seeds = new short[length]; var componentOwners = new short[length]; var visited = new bool[length];
        var completed = crops.ToArray();
        (int Left, int Top, int Right, int Bottom) Pixels(RecoveryBox box) =>
            (Math.Max(0, (int)Math.Ceiling(box.X - .5)), Math.Max(0, (int)Math.Ceiling(box.Y - .5)),
             Math.Min(image.Width - 1, (int)Math.Floor(box.X + box.Width - .5)), Math.Min(image.Height - 1, (int)Math.Floor(box.Y + box.Height - .5)));
        bool Ink(int index) { var p = index * 4; return image.Bgra[p] != 255 || image.Bgra[p + 1] != 255 || image.Bgra[p + 2] != 255; }
        for (var owner = 0; owner < crops.Count; owner++)
        {
            var p = Pixels(crops[owner]);
            if (p.Left > p.Right || p.Top > p.Bottom) throw new InvalidDataException("OCR候補に画素の根拠がありません。");
            for (var y = p.Top; y <= p.Bottom; y++)
            {
                work.Step(p.Right - p.Left + 1L);
                for (var x = p.Left; x <= p.Right; x++)
                { var i = y * image.Width + x; seeds[i] = seeds[i] == 0 ? (short)(owner + 1) : (short)-1; }
            }
        }
        for (var start = 0; start < length; start++)
        {
            work.Step(); if (visited[start] || !Ink(start)) continue;
            var pixels = new List<int> { start }; visited[start] = true;
            var owner = 0; var conflict = false; var attachedRule = false; var textPixels = 0;
            var left = image.Width; var top = image.Height; var right = 0; var bottom = 0;
            for (var cursor = 0; cursor < pixels.Count; cursor++)
            {
                work.Step(); var index = pixels[cursor]; var x = index % image.Width; var y = index / image.Width;
                left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y);
                attachedRule |= ruleMask[index];
                // Physical rule pixels are not text seeds. Original ink is
                // still traversed BEFORE removing the rule, so a text stroke
                // physically joined to it cannot be mistaken for isolated text.
                if (!ruleMask[index])
                {
                    textPixels++; var seed = seeds[index];
                    if (seed == -1 || seed > 0 && owner > 0 && seed != owner) conflict = true;
                    if (seed > 0) owner = seed;
                }
                for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    work.Step(); var nx = x + dx; var ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= image.Width || ny >= image.Height) continue;
                    var next = ny * image.Width + nx;
                    if (!visited[next] && Ink(next)) { visited[next] = true; pixels.Add(next); }
                }
            }
            if (textPixels == 0) continue; // Only proven physical rule ink.
            if (conflict || owner == 0 || attachedRule)
            {
                var failure=new InvalidDataException("OCR印字の切り出し範囲を一意に確認できません。");
                // Isolated research diagnostics: same component, work budget
                // and refusal. No text, image or alternate ownership is added.
                failure.Data["OcrCropConflict"]=conflict;
                failure.Data["OcrCropOwner"]=owner;
                failure.Data["OcrCropAttachedRule"]=attachedRule;
                failure.Data["OcrCropTextPixels"]=textPixels;
                failure.Data["OcrCropComponentPixels"]=pixels.Count;
                failure.Data["OcrCropComponentBounds"]=new RecoveryBox(left,top,right-left+1,bottom-top+1);
                failure.Data["OcrCropCandidateCount"]=crops.Count;
                failure.Data["OcrCropRuleCount"]=rules.Count;
                throw failure;
            }
            foreach (var index in pixels) { work.Step(); componentOwners[index] = (short)owner; }
            var original = crops[owner - 1];
            if (left + .5 >= original.X && top + .5 >= original.Y && right + .5 <= original.X + original.Width && bottom + .5 <= original.Y + original.Height) continue;
            var current = completed[owner - 1];
            var bx = Math.Min(current.X, left); var by = Math.Min(current.Y, top);
            var ex = Math.Max(current.X + current.Width, right + 1); var ey = Math.Max(current.Y + current.Height, bottom + 1);
            completed[owner - 1] = new(bx, by, ex - bx, ey - by);
        }
        work.Step(length); Array.Clear(seeds);
        // No newly enclosed disconnected ink, borrowed component, physical
        // rule or shared crop-supported pixel, including white overlap.
        for (var owner = 0; owner < completed.Length; owner++)
        {
            var original = crops[owner]; var p = Pixels(completed[owner]);
            for (var y = p.Top; y <= p.Bottom; y++)
            {
                work.Step(p.Right - p.Left + 1L);
                for (var x = p.Left; x <= p.Right; x++)
                {
                    var index = y * image.Width + x;
                    if (seeds[index] != 0) throw new InvalidDataException("OCR候補の画素範囲が重なっています。");
                    seeds[index] = (short)(owner + 1);
                    var added = x + .5 < original.X || x + .5 > original.X + original.Width || y + .5 < original.Y || y + .5 > original.Y + original.Height;
                    if (added && (ruleMask[index] || Ink(index) && componentOwners[index] != owner + 1))
                        throw new InvalidDataException("OCR切り出しの追加範囲に別の印字があります。");
                }
            }
        }
        token.ThrowIfCancellationRequested(); return completed;
    }
}
