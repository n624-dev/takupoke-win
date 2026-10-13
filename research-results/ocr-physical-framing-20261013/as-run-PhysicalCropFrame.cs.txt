using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;

// Research only. Adjust INPUT before inference; never clip returned CTC boxes.
// The production connected-component ownership check must have run first.
internal static class PhysicalCropFrame
{
    internal static RecoveryBox Restrict(RecoveryRaster image, RecoveryBox original,
        IReadOnlyList<PdfRule> rules, bool[] ruleMask, CancellationToken token)
    {
        var work = new RecoveryRaster.PixelWork(token);
        int left = image.Width, top = image.Height, right = -1, bottom = -1;
        bool Text(int x, int y)
        {
            var index = y * image.Width + x;
            var p = index * 4;
            return !ruleMask[index] && (image.Bgra[p] != 255 || image.Bgra[p + 1] != 255 || image.Bgra[p + 2] != 255);
        }
        var minX = Math.Max(0, (int)Math.Ceiling(original.X - .5));
        var minY = Math.Max(0, (int)Math.Ceiling(original.Y - .5));
        var maxX = Math.Min(image.Width - 1, (int)Math.Floor(original.X + original.Width - .5));
        var maxY = Math.Min(image.Height - 1, (int)Math.Floor(original.Y + original.Height - .5));
        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
        {
            work.Step(); if (!Text(x, y)) continue;
            left = Math.Min(left, x); top = Math.Min(top, y);
            right = Math.Max(right, x); bottom = Math.Max(bottom, y);
        }
        if (right < left) return original; // No new empty-field assertion.
        PdfBox cell;
        try
        {
            cell = new PdfGrid(new(image.Width, image.Height, [], rules), token)
                .Box((left + right + 1d) / 2, (top + bottom + 1d) / 2);
        }
        catch (PdfParseException e) when (e.Stage != "limit") { return original; }
        // Pixel-centre ownership comes from a physical cell. Do not select a
        // cell using a decoded class, expected role, OCR confidence or oracle.
        if (left + .5 <= cell.Left || right + .5 >= cell.Right ||
            top + .5 <= cell.Top || bottom + .5 >= cell.Bottom)
            return original;
        var bx = Math.Max(original.X, cell.Left + .5);
        var by = Math.Max(original.Y, cell.Top + .5);
        var ex = Math.Min(original.X + original.Width, cell.Right - .5);
        var ey = Math.Min(original.Y + original.Height, cell.Bottom - .5);
        if (ex <= bx || ey <= by) return original;
        var framed = new RecoveryBox(bx, by, ex - bx, ey - by);
        if (framed == original) return original;
        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
        {
            work.Step();
            var included = x + .5 >= bx && x + .5 <= ex && y + .5 >= by && y + .5 <= ey;
            if (!included && Text(x, y)) throw new InvalidDataException("Research crop would remove original text ink.");
        }
        token.ThrowIfCancellationRequested(); return framed;
    }
}
