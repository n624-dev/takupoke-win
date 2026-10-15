using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Recovery;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class OcrInkCropFrameTests
{
    [Fact]
    public void RetainsDisconnectedAndColoredOriginalInkWithFixedMargin()
    {
        var bytes = Enumerable.Repeat((byte)255,40*20*4).ToArray();
        bytes[(5*40+10)*4] = 0; bytes[(11*40+28)*4+1] = 254;
        var image = new RecoveryRaster(40,20,bytes); var original = bytes.ToArray();
        var result = new OcrInkCropFrame(image,new bool[800],default).Restrict(new(0,0,40,20));
        Assert.Equal(new RecoveryBox(8,3,23,11),result); Assert.Equal(original,image.Bgra);
    }

    [Fact]
    public void ProvenRulesDoNotWidenTextButNoEmptyFieldIsInvented()
    {
        var bytes = Enumerable.Repeat((byte)255,40*20*4).ToArray(); var mask = new bool[800];
        bytes[0]=0; mask[0]=true; bytes[(5*40+10)*4]=0;
        var image = new RecoveryRaster(40,20,bytes);
        var result = new OcrInkCropFrame(image,mask,default).Restrict(new(0,0,40,20));
        Assert.Equal(new RecoveryBox(8,3,5,5),result);
        var empty = new RecoveryBox(20,0,10,10);
        Assert.Equal(empty,new OcrInkCropFrame(image,mask,default).Restrict(empty));
    }

    [Fact]
    public void InvalidMaskBoundsAndCancellationAreNotAccepted()
    {
        var image = new RecoveryRaster(40,20,Enumerable.Repeat((byte)255,3200).ToArray());
        Assert.Throws<InvalidDataException>(() => new OcrInkCropFrame(image,[],default).Restrict(new(0,0,40,20)));
        Assert.Throws<InvalidDataException>(() => new OcrInkCropFrame(image,new bool[800],default).Restrict(new(-1,0,40,20)));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => new OcrInkCropFrame(image,new bool[800],cancellation.Token).Restrict(new(0,0,40,20)));
    }
}
