using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Recovery;

// New consumed-data research condition: trim only empty/proven-rule INPUT
// margins after original connected ownership, before any recognition occurs.
// One shared work allowance per page; no CTC/output-dependent crop choice.
internal sealed class InkCropFrame(RecoveryRaster image, bool[] mask, CancellationToken token, int originalPixelMargin = 2)
{
    private readonly RecoveryRaster.PixelWork _work = new(token);
    private readonly double _margin = originalPixelMargin is >= 0 and <= 8 ? originalPixelMargin :
        throw new InvalidDataException("Declared original-pixel margin is outside the research bound.");

    internal RecoveryBox Restrict(RecoveryBox original)
    {
        if (!image.Valid || mask.Length != image.Width * image.Height ||
            !new RecoveryBox(0, 0, image.Width, image.Height).Contains(original))
            throw new InvalidDataException("Original crop/mask identity failed.");
        var left = image.Width; var top = image.Height; var right = -1; var bottom = -1;
        var minX = Math.Max(0, (int)Math.Ceiling(original.X - .5));
        var minY = Math.Max(0, (int)Math.Ceiling(original.Y - .5));
        var maxX = Math.Min(image.Width - 1, (int)Math.Floor(original.X + original.Width - .5));
        var maxY = Math.Min(image.Height - 1, (int)Math.Floor(original.Y + original.Height - .5));
        bool Text(int x, int y)
        {
            var index = y * image.Width + x; var p = index * 4;
            return !mask[index] && (image.Bgra[p] != 255 || image.Bgra[p + 1] != 255 || image.Bgra[p + 2] != 255);
        }
        for (var y = minY; y <= maxY; y++) for (var x = minX; x <= maxX; x++)
        {
            _work.Step(); if (!Text(x, y)) continue;
            left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y);
        }
        if (right < left) return original; // Does not assert an empty field.
        var margin = _margin; // Fixed per measurement before inference, never per prediction/font.
        var bx = Math.Max(original.X, left - margin); var by = Math.Max(original.Y, top - margin);
        var ex = Math.Min(original.X + original.Width, right + 1 + margin);
        var ey = Math.Min(original.Y + original.Height, bottom + 1 + margin);
        var framed = new RecoveryBox(bx, by, ex - bx, ey - by);
        for (var y = minY; y <= maxY; y++) for (var x = minX; x <= maxX; x++)
        {
            _work.Step();
            if (!(x + .5 >= bx && x + .5 <= ex && y + .5 >= by && y + .5 <= ey) && Text(x, y))
                throw new InvalidDataException("Input framing would remove original text ink.");
        }
        token.ThrowIfCancellationRequested(); return framed;
    }
}
