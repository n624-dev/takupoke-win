using System.Security.Cryptography;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;

internal sealed class TileEvidenceException(string message) : Exception(message);
internal sealed record RasterTile(int Index, int X, int Y, int Width, int Height, RecoveryBox Core);
internal sealed record OwnedRegion(int Tile, RecoveryBox Box, RecoveryBox Support, IReadOnlyList<PdfGlyph> Glyphs);
internal static class TileGeometry
{
    internal const int CoreSize = 704, Context = 128, BorderGuard = 2;
    internal static IReadOnlyList<RasterTile> Grid(int width, int height)
    {
        if (width is < 1 or > 2400 || height is < 1 or > 3200) throw new InvalidDataException("Raster envelope exceeded.");
        var result = new List<RasterTile>();
        for (var y = 0; y < height; y += CoreSize) for (var x = 0; x < width; x += CoreSize)
        {
            var core = new RecoveryBox(x, y, Math.Min(CoreSize, width - x), Math.Min(CoreSize, height - y));
            var left = Math.Max(0, x - Context); var top = Math.Max(0, y - Context);
            var right = Math.Min(width, x + CoreSize + Context); var bottom = Math.Min(height, y + CoreSize + Context);
            result.Add(new(result.Count, left, top, right - left, bottom - top, core));
        }
        return result;
    }
    internal static RecoveryRaster Crop(RecoveryRaster image, RasterTile tile, CancellationToken token)
    {
        if (!image.Valid || tile.Width is < 1 or > 960 || tile.Height is < 1 or > 960 || tile.X < 0 || tile.Y < 0 || tile.X + tile.Width > image.Width || tile.Y + tile.Height > image.Height) throw new InvalidDataException("Invalid original pixel crop.");
        var pixels = new byte[checked(tile.Width * tile.Height * 4)];
        try
        {
            for (var y = 0; y < tile.Height; y++) { token.ThrowIfCancellationRequested(); Buffer.BlockCopy(image.Bgra, ((tile.Y + y) * image.Width + tile.X) * 4, pixels, y * tile.Width * 4, tile.Width * 4); }
            return new(tile.Width, tile.Height, pixels);
        }
        catch { CryptographicOperations.ZeroMemory(pixels); throw; }
    }
    internal static bool Owns(RasterTile tile, RecoveryBox local)
    {
        var x = tile.X + local.X + local.Width / 2; var y = tile.Y + local.Y + local.Height / 2;
        return x >= tile.Core.X && x < tile.Core.X + tile.Core.Width && y >= tile.Core.Y && y < tile.Core.Y + tile.Core.Height;
    }
    internal static RecoveryBox CompleteOwnedBox(RasterTile tile, RecoveryBox local, int pageWidth, int pageHeight, RecoveryBox? ownershipSupport = null)
    {
        if (!local.Valid || local.X < 0 || local.Y < 0 || local.X + local.Width > tile.Width || local.Y + local.Height > tile.Height || !Owns(tile, ownershipSupport ?? local)) throw new InvalidDataException("Invalid owned detector region.");
        if (tile.X > 0 && local.X <= BorderGuard || tile.Y > 0 && local.Y <= BorderGuard || tile.X + tile.Width < pageWidth && local.X + local.Width >= tile.Width - BorderGuard || tile.Y + tile.Height < pageHeight && local.Y + local.Height >= tile.Height - BorderGuard)
            throw new TileEvidenceException("Owned detector region touches an artificial tile boundary; incomplete evidence.");
        return local with { X = tile.X + local.X, Y = tile.Y + local.Y };
    }
    internal static IReadOnlyList<PdfGlyph> Merge(IReadOnlyList<OwnedRegion> regions, CancellationToken token)
    {
        if (regions.Count > 10000) throw new InvalidDataException("Page region count exceeded.");
        if (regions.Any(r => !r.Support.Valid || r.Support.X < r.Box.X || r.Support.Y < r.Box.Y || r.Support.X + r.Support.Width > r.Box.X + r.Box.Width + .001 || r.Support.Y + r.Support.Height > r.Box.Y + r.Box.Height + .001)) throw new InvalidDataException("Detector support escaped its original padded crop.");
        var sorted = regions.OrderBy(v => v.Box.Y).ThenBy(v => v.Box.X).ThenBy(v => v.Tile).ToArray();
        // Never discard same text from distinct positions. Overlapping ownership
        // proposals are ambiguous evidence, not a string dedup opportunity.
        long comparisons = 0;
        for (var i = 0; i < sorted.Length; i++) for (var j = i + 1; j < sorted.Length && sorted[j].Box.Y < sorted[i].Box.Y + sorted[i].Box.Height; j++)
        {
            token.ThrowIfCancellationRequested(); if (++comparisons > 1000000) throw new InvalidDataException("Region merge work exceeded.");
            var a = sorted[i].Support; var b = sorted[j].Support;
            var overlap = Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X)) * Math.Max(0, Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y));
            if (overlap > 0) throw new TileEvidenceException("Conflicting overlapping owned detector regions.");
        }
        var glyphs = new List<PdfGlyph>(); var line = 0;
        foreach (var region in sorted)
        {
            token.ThrowIfCancellationRequested(); if (region.Glyphs.Count == 0) throw new TileEvidenceException("Owned OCR region contains no literal glyphs.");
            foreach (var g in region.Glyphs)
            {
                if (!new RecoveryBox(g.X, g.Y, g.Width, g.Height).Valid || g.X < region.Box.X || g.Y < region.Box.Y || g.X + g.Width > region.Box.X + region.Box.Width + .101 || g.Y + g.Height > region.Box.Y + region.Box.Height + .101) throw new InvalidDataException("CTC glyph escaped its original owned region.");
                glyphs.Add(g with { SourceLine = line, SourceOrder = glyphs.Count });
                if (glyphs.Count > 100000) throw new InvalidDataException("Page glyph count exceeded.");
            }
            line++;
        }
        return glyphs;
    }
}
