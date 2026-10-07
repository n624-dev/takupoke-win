using System.IO.Compression;
using System.Security.Cryptography;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Recovery;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class OcrCropCompletenessTests
{
    [Fact]
    public void OpenOuterBorderDoesNotEraseClosedInteriorCellsOrHideItsTail()
    {
        var image = White(900, 600); var before = image.Bgra.ToArray();
        foreach (var y in new[] { 20, 100, 180 }) for (var x = 20; x < 900; x++) Ink(image, x, y);
        foreach (var x in new[] { 20, 120, 220 }) for (var y = 20; y <= 180; y++) Ink(image, x, y);
        before = image.Bgra.ToArray();
        var rules = image.Rules();
        Assert.Equal(6, rules.Count); Assert.Equal(before, image.Bgra);
        Assert.All(rules.Where(r => r.Horizontal), r => { Assert.Equal(20, r.X1); Assert.Equal(220, r.X2); });
        Assert.False(image.RuleMask(rules)[100 * image.Width + 800]);
        Assert.True(image.HasUnrecognizedInk([], rules));
    }
    [Fact]
    public void IsolatedLongGlyphAndHShapeDoNotBecomeClosedInteriorRules()
    {
        var image = White(200, 200);
        for (var y = 20; y <= 180; y++) { Ink(image, 20, y); Ink(image, 180, y); }
        for (var x = 20; x <= 180; x++) Ink(image, x, 100);
        Assert.Empty(image.Rules()); Assert.True(image.HasUnrecognizedInk([], []));
    }
    [Fact]
    public void ConnectedClippedStrokeExpandsOnlyToItsMinimalUnion()
    {
        var image = White(32, 16);
        for (var x = 4; x <= 10; x++) Ink(image, x, 6);
        var original = new RecoveryBox(6, 5, 3, 3);
        var before = image.Bgra.ToArray();

        var completed = OcrCropCompleteness.Complete(image, [original], default);

        Assert.Equal(new RecoveryBox(4, 5, 7, 3), Assert.Single(completed));
        Assert.Equal(before, image.Bgra);
        Assert.False(image.HasUnrecognizedInk(completed, []));
    }

    [Fact]
    public void DiagonalStrokeRequiresEightNeighborConnectivity()
    {
        var image = White(16, 16);
        for (var i = 3; i <= 6; i++) Ink(image, i, i);
        var original = new RecoveryBox(5, 5, 2, 2);
        Assert.True(image.HasUnrecognizedInk([original], []));

        var completed = OcrCropCompleteness.Complete(image, [original], default);

        Assert.Equal(new RecoveryBox(3, 3, 4, 4), Assert.Single(completed));
        Assert.False(image.HasUnrecognizedInk(completed, []));
    }

    [Fact]
    public void NearbyDisconnectedUnownedFragmentIsNotBorrowed()
    {
        var image = White(16, 16);
        for (var x = 5; x <= 7; x++) Ink(image, x, 6);
        Ink(image, 3, 6); // One white pixel separates this fragment from the stroke.
        var original = new RecoveryBox(6, 5, 2, 3);

        Assert.Throws<InvalidDataException>(() => OcrCropCompleteness.Complete(image, [original], default));
        Assert.Equal(new RecoveryBox(6, 5, 2, 3), original);
    }

    [Fact]
    public void ExpandedBoundingRectangleCannotHideDisconnectedUnownedFragment()
    {
        var image = White(24, 24);
        DrawL(image);
        Ink(image, 9, 9); // No original crop owns this disconnected ink inside the L's hull.

        Assert.Throws<InvalidDataException>(() =>
            OcrCropCompleteness.Complete(image, [new(3, 9, 3, 3)], default));
    }

    [Fact]
    public void ExpandedBoundingRectangleCannotEncloseAnotherOwnedFragment()
    {
        var image = White(24, 24);
        DrawL(image);
        Ink(image, 9, 9); // Disconnected, separately owned ink inside the L's hull.
        RecoveryBox[] originals = [new(3, 9, 3, 3), new(8, 8, 3, 3)];

        Assert.Throws<InvalidDataException>(() => OcrCropCompleteness.Complete(image, originals, default));
    }

    [Fact]
    public void OneConnectedComponentCannotBelongToTwoOriginalCrops()
    {
        var image = White(24, 16);
        for (var x = 4; x <= 12; x++) Ink(image, x, 6);
        RecoveryBox[] originals = [new(3, 5, 4, 3), new(10, 5, 4, 3)];

        Assert.Throws<InvalidDataException>(() => OcrCropCompleteness.Complete(image, originals, default));
    }

    [Fact]
    public void StrokePhysicallyJoinedToProvenRectangularRuleIsRejected()
    {
        var image = RuledRectangle();
        for (var y = 6; y <= 12; y++) Ink(image, 15, y);
        var rules = image.Rules();
        Assert.Equal(4, rules.Count);
        Assert.Contains(rules, rule => rule.Horizontal);
        Assert.Contains(rules, rule => rule.Vertical);
        Assert.True(image.RuleMask(rules)[5 * image.Width + 15]);

        Assert.Throws<InvalidDataException>(() => OcrCropCompleteness.Complete(image, [new(14, 8, 3, 3)], default));
    }

    [Fact]
    public void ProvenPhysicalRulePixelsAloneDoNotRequireTextCrop()
    {
        var image = RuledRectangle();
        Assert.Equal(4, image.Rules().Count);

        Assert.Empty(OcrCropCompleteness.Complete(image, [], default));
    }

    [Fact]
    public void OriginalWhiteCropOverlapIsRejected()
    {
        var image = White(24, 24);

        Assert.Throws<InvalidDataException>(() => OcrCropCompleteness.Complete(image, [new(2, 2, 5, 5), new(5, 5, 5, 5)], default));
    }

    [Fact]
    public void ExpansionCannotOverlapEvenWhiteSupportOfAnotherCrop()
    {
        var image = White(24, 24);
        DrawL(image);
        RecoveryBox[] originals = [new(3, 9, 3, 3), new(8, 8, 3, 3)];

        Assert.Throws<InvalidDataException>(() => OcrCropCompleteness.Complete(image, originals, default));
    }

    [Fact]
    public void FullyContainedInkKeepsOriginalFractionalCropExactly()
    {
        var image = White(16, 16);
        Ink(image, 3, 3); Ink(image, 4, 3);
        var original = new RecoveryBox(2.2, 2.2, 5, 5);

        Assert.Equal(original, Assert.Single(OcrCropCompleteness.Complete(image, [original], default)));
    }

    [Fact]
    public void ConnectedInkAtImageEdgeCompletesWithinOriginalImageBounds()
    {
        var image = White(16, 16);
        for (var x = 0; x <= 4; x++) Ink(image, x, 0);

        Assert.Equal(new RecoveryBox(0, 0, 5, 1),
            Assert.Single(OcrCropCompleteness.Complete(image, [new(2, 0, 3, 1)], default)));
    }

    [Theory]
    [InlineData(-1, 0, 2, 2)]
    [InlineData(0, -1, 2, 2)]
    [InlineData(15, 0, 2, 2)]
    [InlineData(0, 15, 2, 2)]
    [InlineData(0, 0, 0, 2)]
    [InlineData(double.NaN, 0, 2, 2)]
    public void InvalidOrOutOfImageCropDoesNotReturnCompletedBounds(double x, double y, double width, double height)
    {
        Assert.Throws<InvalidDataException>(() => OcrCropCompleteness.Complete(White(16, 16), [new(x, y, width, height)], default));
    }

    [Fact]
    public void SubpixelCropWithoutAnyPixelCentreIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => OcrCropCompleteness.Complete(White(16, 16), [new(5.1, 5.1, .1, .1)], default));
    }

    [Fact]
    public void MalformedOriginalRasterIsRejected()
    {
        var malformed = new RecoveryRaster(16, 16, new byte[16 * 16 * 4 - 1]);
        Assert.Throws<InvalidDataException>(() => OcrCropCompleteness.Complete(malformed, [], default));
    }

    [Fact]
    public void CancelledOperationDoesNotReturnCompletedCrop()
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => OcrCropCompleteness.Complete(White(16, 16), [new(2, 2, 3, 3)], cancel.Token));
    }

    [Fact]
    public void CropScansShareExistingPixelWorkAllowanceWithOtherStages()
    {
        // Only a 40KB raster: 6400 supported rectangles request 64M pixel
        // visits, leaving no allowance for the other work in this operation.
        // Test the bounded-work classification, not generic overlap refusal.
        var image = White(100, 100);
        var crops = Enumerable.Repeat(new RecoveryBox(0, 0, 100, 100), 6400).ToArray();

        var error = Assert.Throws<InvalidDataException>(() => OcrCropCompleteness.Complete(image, crops, default));

        Assert.True(RecoveryWorkLimits.IsExceeded(error));
    }

    [Fact]
    public void OriginalFictionalYearPixelsCompleteRecordedClippingWithoutChangingImage()
    {
        // Exact top-left128x32 integer extract from original fictional
        // independent-Timetable-literal-乙/page-1.bgra (1025x206).
        // Full SHA256 verified against preprocess-development-v2 inputs.json:
        // 61a64c898758f186500fd6772c584a3e6abfecb54b7a5d05e02c0df872c73f62.
        // No OCR/model call or helper-generated expected geometry. Original
        // recorded detector crop and source-pixel union are fixed independently.
        const string compressed = "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";
        using var packed = new MemoryStream(Convert.FromBase64String(compressed));
        using var gzip = new GZipStream(packed, CompressionMode.Decompress);
        using var output = new MemoryStream(); gzip.CopyTo(output);
        var bytes = output.ToArray();
        Assert.Equal(128 * 32 * 4, bytes.Length);
        Assert.Equal("397c42b40f3f5a595181462f4bce8261cf5bc607da4fcd8376e717b59b73ffa3", Convert.ToHexStringLower(SHA256.HashData(bytes)));
        var image = new RecoveryRaster(128, 32, bytes);
        var original = new RecoveryBox(8.0078125, 11.265625, 111.04166666666667, 16.09375);
        var unchanged = bytes.ToArray();
        Assert.True(image.HasUnrecognizedInk([original], []));

        var completed = OcrCropCompleteness.Complete(image, [original], default);

        Assert.Equal(new RecoveryBox(5, 10, 115, 18), Assert.Single(completed));
        Assert.False(image.HasUnrecognizedInk(completed, []));
        Assert.Equal(unchanged, image.Bgra);
    }

    private static RecoveryRaster White(int width, int height)
        => new(width, height, Enumerable.Repeat((byte)255, width * height * 4).ToArray());

    private static void Ink(RecoveryRaster image, int x, int y)
        => Array.Clear(image.Bgra, (y * image.Width + x) * 4, 3);

    private static void DrawL(RecoveryRaster image)
    {
        for (var x = 4; x <= 12; x++) Ink(image, x, 4);
        for (var y = 5; y <= 12; y++) Ink(image, 4, y);
    }

    private static RecoveryRaster RuledRectangle()
    {
        var image = White(80, 80);
        for (var x = 5; x <= 74; x++) { Ink(image, x, 5); Ink(image, x, 74); }
        for (var y = 6; y < 74; y++) { Ink(image, 5, y); Ink(image, 74, y); }
        return image;
    }
}
