using System.Text.Json;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Recovery;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class OnnxAsciiOcrTests
{
    [Fact]
    public void NativeOutputsRemainLiteralAndLowConfidenceIsNotRaised()
    {
        using var fixture = new Fixture(); using var runtime = fixture.Open();
        var result = runtime.Read(Image(), new(0, 0, 320, 48), 7, 12);
        Assert.Equal("AI ", result.Text); Assert.True(result.PaddingSupported);
        Assert.Equal(320, result.ValidWidth); Assert.Equal(320, result.InputWidth);
        Assert.Equal(.751f, result.Pieces[1].Confidence); Assert.Equal(.0123f, result.RejectProbability);
        Assert.Equal(new[] { 0, 1, 3 }, result.Pieces.Select(p => p.Start));
        Assert.Equal(new[] { 1, 2, 4 }, result.Pieces.Select(p => p.End));
        Assert.Equal(new int?[] { 12, 13, 14 }, result.Glyphs.Select(g => g.SourceOrder));
        Assert.All(result.Glyphs, g => Assert.Equal(7, g.SourceLine));
        Assert.Equal(240, result.Glyphs[2].X); Assert.Equal(" ", result.Glyphs[2].Text);
    }

    [Fact]
    public void UnsupportedPaddingRetainsAllRawPiecesButProvidesNoPartialEvidence()
    {
        using var fixture = new Fixture(); using var runtime = fixture.Open();
        var result = runtime.Read(Image(), new(0, 0, 160, 48), 0, 0);
        Assert.Equal("AI ", result.Text); Assert.False(result.PaddingSupported);
        Assert.Empty(result.Glyphs); Assert.Equal(3, result.Pieces.Count);
        Assert.Equal(160, result.ValidWidth); Assert.Equal(.0123f, result.RejectProbability);
    }

    [Fact]
    public void CancellationAndUnsupportedCropDoNotProduceObservation()
    {
        using var fixture = new Fixture(); using var runtime = fixture.Open();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => runtime.Read(Image(), new(0, 0, 320, 48), 0, 0, cancellation.Token));
        Assert.Throws<InvalidDataException>(() => runtime.Read(Image(), new(-1, 0, 320, 48), 0, 0));
    }

    [Fact]
    public void MalformedDictionaryIsRefusedBeforeOpeningModel()
    {
        using var fixture = new Fixture(); File.WriteAllText(fixture.Dictionary, "[\"\",\"A\"]");
        Assert.Throws<InvalidDataException>(() => new OnnxAsciiOcr("does-not-exist.onnx", fixture.Dictionary));
    }

    private static RecoveryRaster Image() => new(320, 48, Enumerable.Repeat((byte)255, 320 * 48 * 4).ToArray());

    // Constant synthetic outputs test the native runtime contract, not OCR
    // accuracy. Generator: tools/ocr-practical-documents/make_runtime_fixture.py.
    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ascii-contract-" + Guid.NewGuid().ToString("N"));
        public string Dictionary => Path.Combine(_root, "dictionary.json");
        public Fixture()
        {
            Directory.CreateDirectory(_root);
            var tokens = Enumerable.Repeat("x", 438).ToArray(); tokens[0] = ""; tokens[1] = "A"; tokens[2] = "I"; tokens[^1] = " ";
            File.WriteAllText(Dictionary, JsonSerializer.Serialize(tokens));
        }
        public OnnxAsciiOcr Open() => new(Path.Combine(AppContext.BaseDirectory, "fixtures", "ascii-runtime-contract.onnx"), Dictionary);
        public void Dispose() => Directory.Delete(_root, true);
    }
}
