using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class OcrRoleLabelRetryTests
{
    [Theory]
    [InlineData("科", "目", ":", true)]
    [InlineData("担", "当", "：", true)]
    [InlineData("教", "室", ":", true)]
    [InlineData("架", "空", ":", false)]
    [InlineData("科", "目", ";", false)]
    public void RetryRequiresAnAlreadyObservedRoleLabel(string first, string second, string separator, bool eligible)
    {
        var source = Pieces(first, second, separator, .6f);
        Assert.Equal(eligible, OcrRoleLabelRetry.Eligible(source));
        Assert.False(OcrRoleLabelRetry.Eligible(Pieces(first, second, separator, .9f)));
    }

    [Theory]
    [InlineData("科", "目", ":", .9f, true)]
    [InlineData("科", "目", "：", .9f, true)]
    [InlineData("教", "室", ":", .9f, false)]
    [InlineData("科", "目", ";", .9f, false)]
    [InlineData("科", "目", ":", .7f, false)]
    public void NewNativeObservationMustMatchEveryLabelLetter(string first, string second, string separator, float confidence, bool matches)
    {
        var original = Pieces("科", "目", "：", .6f);
        var retry = Pieces(first, second, separator, confidence).Take(3).ToArray();
        Assert.Equal(matches, OcrRoleLabelRetry.Matches(original, retry));
        Assert.Equal(.6f, original[2].Confidence);
        Assert.Equal("：", original[2].Text);
        Assert.False(OcrRoleLabelRetry.Matches(original, retry.Append(new("X", 8, 9, .99f)).ToArray()));
    }

    [Fact]
    public void PrefixBoundaryNeedsACompletelyWhiteOriginalColumn()
    {
        var bytes = Enumerable.Repeat((byte)255, 80 * 20 * 4).ToArray();
        // Two separated dots and a following glyph. Both dots remain in crop.
        Ink(25, 4); Ink(25, 12); Ink(35, 7);
        var raster = new RecoveryRaster(80, 20, bytes);
        PdfGlyph[] glyphs = [new("科", 3, 0, 2, 20), new("目", 14, 0, 2, 20),
            new("：", 26, 0, 2, 20), new("X", 36, 0, 2, 20)];
        var original = new RecoveryBox(0, 0, 80, 20);
        var crop = OcrRoleLabelRetry.PrefixCrop(raster, original, glyphs)!;
        Assert.True(crop.Valid); Assert.Equal(original.Height, crop.Height);
        Assert.True(crop.Contains(new(25, 4, 1, 9)));
        Assert.True(crop.X + crop.Width <= 35);
        // Even faint coloured ink spanning all proposed columns forbids a cut.
        for (var x = 27; x <= 37; x++) { var p = (19 * 80 + x) * 4; bytes[p] = 254; }
        Assert.Null(OcrRoleLabelRetry.PrefixCrop(raster, original, glyphs));
        void Ink(int x, int y) { var p = (y * 80 + x) * 4; bytes[p] = bytes[p + 1] = bytes[p + 2] = 0; }
    }

    private static OcrRecognitionPiece[] Pieces(string first, string second, string separator, float confidence)
        => [new(first, 1, 2, .99f), new(second, 3, 4, .99f), new(separator, 5, 6, confidence), new("X", 8, 9, .99f)];
}
