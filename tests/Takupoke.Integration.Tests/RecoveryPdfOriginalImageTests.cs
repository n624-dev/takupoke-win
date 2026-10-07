using System.Buffers.Binary;
using System.IO.Compression;
using Takupoke.Infrastructure.Recovery;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class RecoveryPdfOriginalImageTests
{
    [Fact] public void EveryOriginalColourSampleAndRowSurvivesWithoutResampling()
    {
        var rgb=Enumerable.Range(0,800*1000*3).Select(index=>(byte)((index*13+index/2400*17)%256)).ToArray();
        var source=RecoveryPdfImageResolutionTests.Pdf(samples:rgb);var unchanged=source.ToArray();
        using var image=RecoveryPdfOriginalImage.TryRead(source,1);
        Assert.NotNull(image);Assert.Equal(800,image.Width);Assert.Equal(1000,image.Height);Assert.Equal(unchanged,source);
        using var idat=new MemoryStream();var png=image.Png;Assert.Equal(new byte[]{137,80,78,71,13,10,26,10},png[..8]);
        for(var offset=8;offset<png.Length;)
        {
            var length=checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset,4)));var kind=System.Text.Encoding.ASCII.GetString(png,offset+4,4);
            if(kind=="IDAT")idat.Write(png,offset+8,length);offset+=length+12;
        }
        idat.Position=0;using var zip=new ZLibStream(idat,CompressionMode.Decompress);var row=new byte[2400];
        for(var number=0;number<1000;number++){Assert.Equal(0,zip.ReadByte());zip.ReadExactly(row);Assert.Equal(rgb.AsSpan(number*2400,2400).ToArray(),row);}
        Assert.Equal(-1,zip.ReadByte());
    }
    [Theory]
    [InlineData("/Decode [1 0 1 0 1 0]")]
    [InlineData("/SMask null")]
    [InlineData("/Mask [0 1 0 1 0 1]")]
    [InlineData("/OC null")]
    [InlineData("/Intent /Perceptual")]
    [InlineData("/DecodeParms << /Predictor 12 >>")]
    public void UnsupportedPixelInterpretationKeepsTheFullPDFRenderer(string extra)
        =>Assert.Null(RecoveryPdfOriginalImage.TryRead(RecoveryPdfImageResolutionTests.Pdf(imageExtra:extra),1));
    [Fact] public void UnsupportedColourSpaceIsNotTreatedAsRGB()
        =>Assert.Null(RecoveryPdfOriginalImage.TryRead(RecoveryPdfImageResolutionTests.Pdf(colorSpace:"/DeviceCMYK"),1));
    [Theory] [InlineData(-1)] [InlineData(1)]
    public void TruncatedOrExtraSourceSamplesAreNotCopied(int extra)
        =>Assert.Null(RecoveryPdfOriginalImage.TryRead(RecoveryPdfImageResolutionTests.Pdf(samples:new byte[800*1000*3+extra]),1));
    [Fact] public void ExplicitlyEmptyDecodeParametersKeepIdentitySamples()
    {
        using var image=RecoveryPdfOriginalImage.TryRead(RecoveryPdfImageResolutionTests.Pdf(imageExtra:"/DecodeParms << >>"),1);Assert.NotNull(image);
    }
    [Fact] public void OverlayAndChangedGeometryDoNotDropVisibleContent()
        =>Assert.Null(RecoveryPdfOriginalImage.TryRead(RecoveryPdfImageResolutionTests.Pdf(pageExtra:"/Annots []"),1));
    [Fact] public void DisposalClearsOwnedEncodedSource()
    {
        var image=RecoveryPdfOriginalImage.TryRead(RecoveryPdfImageResolutionTests.Pdf(),1);Assert.NotNull(image);var pixels=image.Png;
        image.Dispose();Assert.All(pixels,p=>Assert.Equal(0,p));
    }
    [Fact] public void CancellationIsNotAnUnsupportedImage()
    {
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>RecoveryPdfOriginalImage.TryRead(RecoveryPdfImageResolutionTests.Pdf(),1,cancellation.Token));
    }
}
