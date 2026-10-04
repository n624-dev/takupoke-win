using System.Security.Cryptography;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;

internal static class TilePreflight
{
    internal static object Run()
    {
        var checks = 0;
        void Check(bool ok, string description) { if (!ok) throw new InvalidDataException("Geometry preflight: " + description); checks++; }
        void Refuses(Action action, string description) { try { action(); throw new InvalidDataException("Expected rejection: " + description); } catch (TileEvidenceException) { checks++; } }
        foreach (var (w, h) in new[] { (1, 1), (640, 900), (704, 704), (705, 705), (2400, 3200) })
        {
            var grid = TileGeometry.Grid(w, h);
            Check(grid.All(t => t.Width <= 960 && t.Height <= 960), "tile dimensions bounded");
            Check(grid.Sum(t => t.Core.Width * t.Core.Height) == (double)w * h, "complete disjoint core area");
            foreach (var (x, y) in new[] { (0, 0), (w - 1, h - 1), (Math.Min(704, w - 1), Math.Min(704, h - 1)), (w / 2, h / 2) })
                Check(grid.Count(t => TileGeometry.Owns(t, new(x - t.X, y - t.Y, .1, .1))) == 1, "exactly one boundary owner");
        }
        var tiles = TileGeometry.Grid(1408, 704); var right = tiles[1]; var local = new RecoveryBox(130, 40, 50, 20);
        var global = TileGeometry.CompleteOwnedBox(right, local, 1408, 704);
        Check(global.X == right.X + local.X && global.Y == 40 && global.Width == 50, "integer offset preserves original boxes");
        var left = tiles[0]; Refuses(() => TileGeometry.CompleteOwnedBox(left, new(500, 20, 331, 20), 1408, 704), "owned region touching internal right border");
        var external = TileGeometry.Grid(640, 640)[0]; Check(TileGeometry.CompleteOwnedBox(external, new(0, 0, 50, 20), 640, 640).X == 0, "true page boundary allowed");
        var glyph = new PdfGlyph("同", global.X + 1, global.Y, 10, global.Height);
        var other = global with { X = global.X + 100 };
        var merged = TileGeometry.Merge([new(1, global, global, [glyph]), new(1, other, other, [glyph with { X = other.X + 1 }])], CancellationToken.None);
        Check(merged.Count == 2 && merged[0].Text == merged[1].Text && merged[0].X != merged[1].X, "same text at different source positions is retained");
        Refuses(() => TileGeometry.Merge([new(1, global, global, [glyph]), new(0, global, global, [glyph])], CancellationToken.None), "overlapping ownership conflict");
        var smallOverlap = global with { X = global.X + global.Width - 1 };
        Refuses(() => TileGeometry.Merge([new(1, global, global, [glyph]), new(0, smallOverlap, smallOverlap, [glyph with { X = smallOverlap.X + 1 }])], CancellationToken.None), "one pixel cross-owner overlap is rejected without silent dedup");
        var nextLineBox = global with { Y = global.Y + 15 }; var firstSupport = global with { Y = global.Y + 2, Height = 5 }; var nextSupport = nextLineBox with { Y = nextLineBox.Y + 2, Height = 5 };
        Check(TileGeometry.Merge([new(1, global, firstSupport, [glyph]), new(0, nextLineBox, nextSupport, [glyph with { Y = nextLineBox.Y }])], CancellationToken.None).Count == 2, "distinct ink-support rows survive overlapping recognition padding");
        Refuses(() => TileGeometry.Merge([new(1, global, global, [])], CancellationToken.None), "empty literal region cannot cover printed evidence");
        var pixels = Enumerable.Repeat((byte)255, 800 * 32 * 4).ToArray(); for (var i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i % 251);
        var raster = new RecoveryRaster(800, 32, pixels); var tile = TileGeometry.Grid(800, 32)[1]; var crop = TileGeometry.Crop(raster, tile, CancellationToken.None);
        for (var y = 0; y < crop.Height; y++) Check(crop.Bgra.AsSpan(y * crop.Width * 4, crop.Width * 4).SequenceEqual(pixels.AsSpan(((tile.Y + y) * 800 + tile.X) * 4, crop.Width * 4)), "pixel-exact crop row");
        CryptographicOperations.ZeroMemory(crop.Bgra); CryptographicOperations.ZeroMemory(pixels);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { TileGeometry.Crop(raster, tile, canceled.Token); throw new InvalidDataException("Cancellation ignored"); } catch (OperationCanceledException) { checks++; }
        var white = Enumerable.Repeat((byte)255, 64 * 64 * 4).ToArray(); white[(32 * 64 + 32) * 4] = 0;
        var ink = new RecoveryRaster(64, 64, white);
        Check(ink.HasUnrecognizedInk([], [], CancellationToken.None), "unowned/unrecognized original pixel prohibits complete evidence");
        Check(!ink.HasUnrecognizedInk([new(31, 31, 3, 3)], [], CancellationToken.None), "original printed pixel covered by original region");
        Check(!ink.InkFree(new(30, 30, 5, 5)), "missing glyph is not blank proof");
        CryptographicOperations.ZeroMemory(white);
        return new { geometryChecks = checks, modelCalls = 0, scope = "Geometry/crop/boundary/conflict/coverage controls only; no OCR quality" };
    }
}
