using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class OcrLiteralCodeRetryTests
{
    [Fact]
    public void MisreadLettersCanReachActualDigitObservationWithoutChangingItsScore()
    {
        var expert = Observed("J0", .71f);
        Assert.True(OcrLiteralCodeRetry.Accepts(expert, "JO", null));
        Assert.Equal(.71f, expert.Pieces[1].Confidence);
        Assert.False(OcrLiteralCodeRetry.Accepts(Observed("JO"), "JO", null));
    }

    [Fact]
    public void LengthChangesRequireCompleteMatchingIndependentObservation()
    {
        var expert = Observed("R4_");
        Assert.False(OcrLiteralCodeRetry.Accepts(expert, "R4", null));
        Assert.False(OcrLiteralCodeRetry.Accepts(expert, "R4", Observed("R4")));
        Assert.False(OcrLiteralCodeRetry.Accepts(expert, "R4", Observed("R4_", supported: false)));
        Assert.True(OcrLiteralCodeRetry.Accepts(expert, "R4", Observed("R4_")));
        Assert.False(OcrLiteralCodeRetry.Accepts(Observed("R4"), "R4i", Observed("R4i")));
    }

    [Theory]
    [InlineData("123", "123")]
    [InlineData("R4", "R×4")]
    [InlineData("R 4", "R 4")]
    [InlineData("R", "R4")]
    public void NumericOnlyOrNoncodeInputCannotUseThisExtension(string original, string output)
        => Assert.False(OcrLiteralCodeRetry.Accepts(Observed(output), original, Observed(output)));

    [Fact]
    public void RejectAndPaddingGuardsKeepAllRawObservationsOutOfEvidence()
    {
        Assert.False(OcrLiteralCodeRetry.Accepts(Observed("J0", supported: false), "JO", null));
        Assert.False(OcrLiteralCodeRetry.Accepts(Observed("J0") with { RejectProbability = .051f }, "JO", null));
        Assert.False(OcrLiteralCodeRetry.Accepts(Observed("J0") with { RejectProbability = float.NaN }, "JO", null));
        Assert.False(OcrLiteralCodeRetry.Accepts(Observed("J0") with { Glyphs = [] }, "JO", null));
    }

    [Fact]
    public void NoOriginalWhiteBandKeepsCompleteBaselineAndNeverCallsModel()
    {
        var image = new RecoveryRaster(40, 20, new byte[40 * 20 * 4]);
        PdfGlyph[] primary = [new("室", 0, 0, 10, 20, 0, 0), new("AO", 10, 0, 20, 20, 0, 1)];
        double[] scores = [.99, .97]; var baseline = new OcrAsciiRefinement.Result(primary, scores, 0); var calls = 0;
        var result = OcrLiteralCodeRetry.Refine(image, primary, scores, new Dictionary<int, RecoveryBox> { [0] = new(0, 0, 40, 20) }, [], baseline,
            new OcrInkCropFrame(image, null, default), (_, _, _) => { calls++; return Observed("A0"); }, default);
        Assert.Equal(0, calls); Assert.Equal(primary, result.Glyphs); Assert.Equal(scores, result.Confidences);
    }

    private static OnnxAsciiOcr.Observation Observed(string text, float score = .99f, bool supported = true)
    {
        var pieces = text.Select((c, n) => new OcrRecognitionPiece(c.ToString(), n, n + 1, score)).ToArray();
        var glyphs = pieces.Select((p, n) => new PdfGlyph(p.Text, n * 8, 0, 8, 48, 0, n)).ToArray();
        return new(320, 320, pieces, .01f, supported, supported ? glyphs : []);
    }
}
