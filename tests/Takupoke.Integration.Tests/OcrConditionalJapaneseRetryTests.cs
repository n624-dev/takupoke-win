using Takupoke.Infrastructure.Recovery;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class OcrConditionalJapaneseRetryTests
{
    [Fact]
    public void SelectsOnlyUncertainJapaneseAndKeepsObservedLabelColon()
    {
        var original = Pieces("科目：架1", 3, .6f); var alternate = Pieces("科目:仮1", 3, .83f);
        Assert.Equal(new[] { 3 }, OcrConditionalJapaneseRetry.Replacements(original, alternate, true));
        Assert.Equal(.83f, alternate[3].Confidence); Assert.Equal(.6f, original[3].Confidence);
    }

    [Theory]
    [InlineData("科目：架1", "科目：仮7", .6f, .99f)]
    [InlineData("科目：架1", "科目：仮1", .8f, .99f)]
    [InlineData("科目：架1", "科目：仮1", .6f, .79f)]
    [InlineData("科目：架1", "科目：A1", .6f, .99f)]
    [InlineData("資料：架1", "資料：仮1", .6f, .99f)]
    [InlineData("科目：架1", "科目：仮11", .6f, .99f)]
    public void DisagreementOutsideUncertainJapaneseNeverRewritesBody(string before, string after, float oldP, float nextP)
        => Assert.Empty(OcrConditionalJapaneseRetry.Replacements(Pieces(before, 3, oldP), Pieces(after, 3, nextP), true));

    [Fact]
    public void UnsupportedPaddingAndNonfiniteScoresNeverProduceReplacement()
    {
        var original = Pieces("科目：架1", 3, .6f); var alternate = Pieces("科目：仮1", 3, .99f);
        Assert.Empty(OcrConditionalJapaneseRetry.Replacements(original, alternate, false));
        Assert.Empty(OcrConditionalJapaneseRetry.Replacements(original, Pieces("科目：仮1", 3, float.NaN), true));
        Assert.Empty(OcrConditionalJapaneseRetry.Replacements(Pieces("科目：架1", 3, float.NegativeInfinity), alternate, true));
    }

    [Fact]
    public void PunctuationAndAsciiUncertaintyDoNotInvokeJapaneseExpert()
    {
        Assert.False(OcrConditionalJapaneseRetry.Eligible(Pieces("教室：A1", 3, .4f)));
        Assert.False(OcrConditionalJapaneseRetry.Eligible(Pieces("科目：架1", 2, .4f)));
        Assert.False(OcrConditionalJapaneseRetry.Japanese("辺A"));
    }

    private static OcrRecognitionPiece[] Pieces(string text, int uncertain, float score)
        => text.Select((c, n) => new OcrRecognitionPiece(c.ToString(), n * 2, n * 2 + 1, n == uncertain ? score : .99f)).ToArray();
}
