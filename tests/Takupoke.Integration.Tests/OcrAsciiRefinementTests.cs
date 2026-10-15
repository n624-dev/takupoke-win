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
