using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Recovery;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class OcrWhiteGapPartitionTests
{
    [Fact]
    public void UsesWidestOriginalWhiteBandAndPreservesWholeRightExtent()
    {
        var raster = Image(); Ink(raster, 9, 3); Ink(raster, 14, 7);
        var before = raster.Bgra.ToArray(); var line = new RecoveryBox(2.5, 1.5, 20, 8);
        var tail = OcrWhiteGapPartition.Tail(raster, line, 5.25, 18);
        Assert.Equal(new RecoveryBox(12, 1.5, 10.5, 8), tail);
        Assert.Equal(before, raster.Bgra);
        Assert.Equal(line.X + line.Width, tail!.X + tail.Width);
    }

    [Fact]
    public void NonwhiteAtEitherEdgeOfFractionalHeightBlocksCut()
    {
        var raster = Image();
        for (var x = 8; x <= 10; x++) Ink(raster, x, x % 2 == 0 ? 1 : 9);
        Assert.Null(OcrWhiteGapPartition.Tail(raster, new(2.5, 1.5, 20, 8), 7.25, 10));
    }

    [Fact]
    public void ColoredPixelCannotBeTreatedAsWhiteAndEqualBandsUseLeftmost()
    {
        var raster = Image(); raster.Bgra[(3 * raster.Width + 10) * 4 + 1] = 254;
        Assert.Equal(8, OcrWhiteGapPartition.Tail(raster, new(0, 0, 24, 12), 6, 14)!.X);
    }

    [Theory]
    [InlineData(double.NaN, 10)]
    [InlineData(10, double.PositiveInfinity)]
    [InlineData(12, 10)]
    [InlineData(-1, 10)]
    [InlineData(10, 30)]
    public void InvalidBoundaryDoesNotCreateCrop(double prefix, double tail)
        => Assert.Null(OcrWhiteGapPartition.Tail(Image(), new(0, 0, 24, 12), prefix, tail));

    [Fact]
    public void InvalidSourceBoundsAndCancellationAreRetained()
    {
        Assert.Null(OcrWhiteGapPartition.Tail(Image(), new(-1, 0, 25, 12), 6, 14));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            OcrWhiteGapPartition.Tail(Image(), new(0, 0, 24, 12), 6, 14, cancellation.Token));
    }

    private static RecoveryRaster Image() => new(24, 12, Enumerable.Repeat((byte)255, 24 * 12 * 4).ToArray());
    private static void Ink(RecoveryRaster raster, int x, int y) => raster.Bgra[(y * raster.Width + x) * 4] = 0;
}
