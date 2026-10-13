using Microsoft.ML.OnnxRuntime.Tensors;
using Takupoke.Infrastructure.Recovery;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class OcrCtcDecoderTests
{
    private static readonly string[] Alphabet = ["", "甲", "乙", " "];

    [Fact]
    public void RepeatsCollapseButBlankSeparatedCharactersAndPrintedSpaceRemain()
    {
        var scores = new DenseTensor<float>([1, 7, 4]);
        int[] ids = [1, 1, 0, 1, 3, 2, 2];
        float[] values = [.7f, .9f, 1, .85f, .95f, .8f, .6f];
        for (var t = 0; t < ids.Length; t++) scores[0, t, ids[t]] = values[t];
        Assert.Equal(new OcrRecognitionPiece[] {
            new("甲", 0, 2, .9f), new("甲", 3, 4, .85f),
            new(" ", 4, 5, .95f), new("乙", 5, 7, .8f)
        }, OcrCtcDecoder.Decode(scores, Alphabet));
    }

    [Fact]
    public void EqualScoresKeepFirstTokenAndBlankDoesNotCreateEmptyEvidence()
    {
        var scores = new DenseTensor<float>([1, 3, 4]);
        scores[0, 0, 0] = scores[0, 0, 1] = .9f;
        scores[0, 1, 1] = scores[0, 1, 2] = .8f;
        Assert.Equal(new[] { new OcrRecognitionPiece("甲", 1, 2, .8f) }, OcrCtcDecoder.Decode(scores, Alphabet));
        Assert.Empty(OcrCtcDecoder.Decode(new DenseTensor<float>([1, 2, 4]), Alphabet));
    }

    [Theory]
    [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)] [InlineData(-.01f)] [InlineData(1.01f)]
    public void InvalidLosingTokenCannotBeHiddenByAValidWinner(float invalid)
    {
        var scores = new DenseTensor<float>([1, 1, 4]);
        scores[0, 0, 1] = 1; scores[0, 0, 3] = invalid;
        Assert.Throws<InvalidDataException>(() => OcrCtcDecoder.Decode(scores, Alphabet));
    }

    [Theory]
    [InlineData(18385)] [InlineData(18710)]
    public void DifferentPinnedModelAlphabetsKeepTheirLastSpaceToken(int symbols)
    {
        var dictionary = Enumerable.Repeat("架", symbols).ToArray();
        dictionary[0] = ""; dictionary[^1] = " ";
        var scores = new DenseTensor<float>([1, 2, symbols]);
        scores[0, 0, symbols - 1] = .75f; scores[0, 1, symbols - 1] = .85f;
        Assert.Equal(new[] { new OcrRecognitionPiece(" ", 0, 2, .85f) }, OcrCtcDecoder.Decode(scores, dictionary));
        Assert.Throws<InvalidDataException>(() => OcrCtcDecoder.Decode(scores, Alphabet));
    }

    [Fact]
    public void ReversedTensorStridesPreserveLogicalTimeAndSymbolCoordinates()
    {
        var scores = new DenseTensor<float>([1, 3, 4], reverseStride: true);
        scores[0, 0, 1] = .9f; scores[0, 1, 2] = .8f; scores[0, 2, 3] = .7f;
        Assert.Equal(new OcrRecognitionPiece[] {
            new("甲", 0, 1, .9f), new("乙", 1, 2, .8f), new(" ", 2, 3, .7f)
        }, OcrCtcDecoder.Decode(scores, Alphabet));
    }

    [Theory]
    [InlineData(2, 1, 4)] [InlineData(1, 0, 4)] [InlineData(1, 1, 3)]
    public void IncorrectBatchTimeOrAlphabetShapeIsRefused(int batch, int time, int symbols)
        => Assert.Throws<InvalidDataException>(() => OcrCtcDecoder.Decode(new DenseTensor<float>([batch, time, symbols]), Alphabet));

    [Fact]
    public void CancellationIsCheckedBeforeAndAfterDecodingTheFirstRow()
    {
        using var stop = new CancellationTokenSource(); stop.Cancel();
        Assert.Throws<OperationCanceledException>(() => OcrCtcDecoder.Decode(new DenseTensor<float>([1, 1, 4]), Alphabet, stop.Token));
        var scores = new DenseTensor<float>([1, 3, 18385]); scores[0, 0, 1] = 1;
        var dictionary = Enumerable.Repeat("甲", 18385).ToArray(); dictionary[0] = "";
        using var during = new CancellationTokenSource();
        // Cancel synchronously when the first decoded letter is obtained.
        // No timer/thread-pool race with the concurrently running test suite.
        Assert.Throws<OperationCanceledException>(() => OcrCtcDecoder.Decode(scores, new CancelOnLetterDictionary(dictionary, during), during.Token));
        Assert.True(during.IsCancellationRequested);
    }

    private sealed class CancelOnLetterDictionary(string[] values, CancellationTokenSource stop) : IReadOnlyList<string>
    {
        public int Count => values.Length;
        public string this[int index] { get { if (index == 1) stop.Cancel(); return values[index]; } }
        public IEnumerator<string> GetEnumerator() => ((IEnumerable<string>)values).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
