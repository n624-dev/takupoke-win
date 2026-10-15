using Takupoke.Core.Recovery;

namespace Takupoke.Infrastructure.Recovery;

// Partitions an already complete line at an original all-white vertical band.
// The proposed prefix/tail boundary is only a CTC hypothesis. This helper does
// not establish Unicode, script, recognition accuracy or whole-page coverage.
internal static class OcrWhiteGapPartition
{
    public static RecoveryBox? Tail(RecoveryRaster raster, RecoveryBox line,
        double prefixEnd, double tailBegin, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!raster.Valid || !new RecoveryBox(0, 0, raster.Width, raster.Height).Contains(line) ||
            !double.IsFinite(prefixEnd) || !double.IsFinite(tailBegin) ||
            prefixEnd < line.X || tailBegin > line.X + line.Width || tailBegin < prefixEnd)
            return null;
        var lo = Math.Max((int)Math.Ceiling(prefixEnd), (int)Math.Ceiling(line.X));
        var hi = Math.Min((int)Math.Floor(tailBegin), (int)Math.Floor(line.X + line.Width) - 1);
        var top = (int)Math.Floor(line.Y); var bottom = (int)Math.Ceiling(line.Y + line.Height);
        var bestStart = -1; var bestEnd = -1; var runStart = -1;
        for (var x = lo; x <= hi; x++)
        {
            token.ThrowIfCancellationRequested(); var white = true;
            for (var y = top; y < bottom && white; y++)
            {
                var pixel = (y * raster.Width + x) * 4;
                white = raster.Bgra[pixel] == 255 && raster.Bgra[pixel + 1] == 255 && raster.Bgra[pixel + 2] == 255;
            }
            if (white && runStart < 0) runStart = x;
            if (!white && runStart >= 0) { Consider(runStart, x); runStart = -1; }
        }
        if (runStart >= 0) Consider(runStart, hi + 1);
        if (bestStart < 0) return null;
        var split = (bestStart + bestEnd) / 2;
        var tail = new RecoveryBox(split, line.Y, line.X + line.Width - split, line.Height);
        return split > prefixEnd && tail.Width > 0 && line.Contains(tail) ? tail : null;

        void Consider(int start, int end)
        {
            // Equal widths retain the first (leftmost) band.
            if (end - start > bestEnd - bestStart) { bestStart = start; bestEnd = end; }
        }
    }
}
