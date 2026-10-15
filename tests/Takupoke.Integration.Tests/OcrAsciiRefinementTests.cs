using System.Text.Json;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class OcrAsciiRefinementTests
{
    [Fact]
    public void NoncanonicalHeaderRetainsRawPrimaryAndItsNativeConfidence()
    {
        using var runtime = Runtime(out var dictionary);
        try
        {
            var image = White(); PdfGlyph[] primary = [new("Al_1",0,20,40,48,0,0)];
            var result = OcrAsciiRefinement.Refine(image,primary,[.91],new Dictionary<int,RecoveryBox> { [0]=new(0,20,64,48) },[new(80,10,80,100)],runtime);
            Assert.Equal("Al_1",string.Concat(result.Glyphs.Select(g => g.Text))); Assert.Equal(.91,Assert.Single(result.Confidences));
            Assert.Equal(1,result.RecognizerCalls); // Constant fixture emits AI + space, never canonical AI_1.
        }
        finally { File.Delete(dictionary); }
    }

    [Fact]
    public void LowConfidenceOrDifferentLengthTailIsNotSubstitutedAndPrefixIsUnchanged()
    {
        using var runtime = Runtime(out var dictionary);
        try
        {
            var image = White(); PdfGlyph[] primary = [new("原文",0,20,40,48,0,0),new("A1",50,20,80,48,0,1)];
            var result = OcrAsciiRefinement.Refine(image,primary,[.94,.95],new Dictionary<int,RecoveryBox> { [0]=new(0,20,160,48) },[],runtime);
            Assert.Equal(primary,result.Glyphs); Assert.Equal(new[] { .94,.95 },result.Confidences);
            Assert.Equal(1,result.RecognizerCalls);
        }
        finally { File.Delete(dictionary); }
    }

    [Fact]
    public void UnrelatedAsciiDoesNotRouteAndMissingSourceCropRefuses()
    {
        using var runtime = Runtime(out var dictionary);
        try
        {
            var image = White(); PdfGlyph[] primary = [new("ALPHA",0,20,40,48,0,0)];
            var result = OcrAsciiRefinement.Refine(image,primary,[.95],new Dictionary<int,RecoveryBox> { [0]=new(0,20,64,48) },[],runtime);
            Assert.Equal(0,result.RecognizerCalls); Assert.Equal(primary,result.Glyphs);
            Assert.Throws<InvalidDataException>(() => OcrAsciiRefinement.Refine(image,primary,[.95],new Dictionary<int,RecoveryBox>(),[],runtime));
        }
        finally { File.Delete(dictionary); }
    }

    private static RecoveryRaster White() => new(320,120,Enumerable.Repeat((byte)255,320*120*4).ToArray());
    [Fact]
    public void ManualCaptureRetainsSpecialistNativeUncertaintyWhileStrictRoutePreservesPrimary()
    {
        var dictionary = Path.Combine(Path.GetTempPath(), "ascii-manual-" + Guid.NewGuid().ToString("N") + ".json");
        var tokens = Enumerable.Repeat("x", 438).ToArray();
        tokens[0] = ""; tokens[1] = "A"; tokens[2] = "1"; tokens[3] = "2"; tokens[^1] = " ";
        File.WriteAllText(dictionary, JsonSerializer.Serialize(tokens));
        try
        {
            using var runtime = new OnnxAsciiOcr(Path.Combine(AppContext.BaseDirectory, "fixtures/ascii-manual-contract.onnx"), dictionary);
            PdfGlyph[] primary = [new("AI2", 0, 20, 300, 48, 0, 0)];
            var crops = new Dictionary<int, RecoveryBox> { [0] = new(0, 20, 320, 48) };
            var strict = OcrAsciiRefinement.Refine(White(), primary, [.96], crops, [], runtime);
            Assert.Equal(primary, strict.Glyphs); Assert.Equal(.96, Assert.Single(strict.Confidences));
            var manual = OcrAsciiRefinement.Refine(White(), primary, [.96], crops, [], runtime, retainUncertain: true);
            Assert.Equal("A12", string.Concat(manual.Glyphs.Select(g => g.Text)));
            Assert.Equal((double).7f, manual.Confidences[1]); // Native score is not promoted to the primary's .96.
            Assert.All(manual.Glyphs, glyph => Assert.True(crops[0].Contains(new(glyph.X, glyph.Y, glyph.Width, glyph.Height))));
            Assert.Equal(1, manual.RecognizerCalls);
            // The prefix keeps its original literal/native score; only a
            // bounded code tail from the original white band can change.
            PdfGlyph[] prefixed = [new("教室", 0, 20, 35, 48, 0, 0), new("AI2", 80, 20, 200, 48, 0, 1)];
            var refined = OcrAsciiRefinement.Refine(White(), prefixed, [.95, .96], crops, [], runtime, retainUncertain: true);
            Assert.Equal("教室A12", string.Concat(refined.Glyphs.Select(g => g.Text)));
            Assert.Equal(prefixed[0], refined.Glyphs[0]); Assert.Equal(.95, refined.Confidences[0]);
            Assert.Equal((double).7f, refined.Confidences[2]);
            Assert.Equal(1, refined.RecognizerCalls);
            PdfGlyph[] word = [prefixed[0], prefixed[1] with { Text = "III" }];
            Assert.Equal(word, OcrAsciiRefinement.Refine(White(), word, [.95, .96], crops, [], runtime, retainUncertain: true).Glyphs);
            PdfGlyph[] classLabel = [new("1_1", 0, 20, 300, 48, 0, 0)];
            Assert.Equal(classLabel, OcrAsciiRefinement.Refine(White(), classLabel, [.96], crops, [], runtime, retainUncertain: true).Glyphs);
            // Above-grid numeric headings are not body-code uncertainty.
            PdfGlyph[] year = [new("203", 0, 20, 300, 48, 0, 0)];
            Assert.Equal(year, OcrAsciiRefinement.Refine(White(), year, [.96], crops, [new(80, 80, 80, 110)], runtime, retainUncertain: true).Glyphs);
        }
        finally { File.Delete(dictionary); }
    }
    [Theory]
    [InlineData("A21",1)][InlineData("101",1)][InlineData("B-2_3",1)]
    [InlineData("1",0)][InlineData("BIOLOGY",0)][InlineData("A?21",0)][InlineData("2029年度",0)]
    public void StandaloneCodesUseSpecialistAndRetainPrimaryWhenNativeOutputFailsThreshold(string text, int expectedCalls)
    {
        using var runtime = Runtime(out var dictionary);
        try
        {
            PdfGlyph[] primary = [new(text,0,20,160,48,0,0)];
            var result = OcrAsciiRefinement.Refine(White(),primary,[.97],new Dictionary<int,RecoveryBox> { [0]=new(0,20,320,48) },[],runtime);
            Assert.Equal(expectedCalls,result.RecognizerCalls);
            // Fixture's native I score is .751: even an equal-length code
            // cannot replace the literal primary using this failed threshold.
            Assert.Equal(primary,result.Glyphs); Assert.Equal(.97,Assert.Single(result.Confidences));
        }
        finally { File.Delete(dictionary); }
    }
    private static OnnxAsciiOcr Runtime(out string dictionary)
    {
        dictionary=Path.Combine(Path.GetTempPath(),"ascii-refinement-"+Guid.NewGuid().ToString("N")+".json");
        var tokens=Enumerable.Repeat("x",438).ToArray();tokens[0]="";tokens[1]="A";tokens[2]="I";tokens[^1]=" ";
        File.WriteAllText(dictionary,JsonSerializer.Serialize(tokens));
        return new(Path.Combine(AppContext.BaseDirectory,"fixtures/ascii-runtime-contract.onnx"),dictionary);
    }
}
