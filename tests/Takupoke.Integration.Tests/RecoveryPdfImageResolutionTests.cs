using System.IO.Compression;
using System.Text;
using Takupoke.Infrastructure.Recovery;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class RecoveryPdfImageResolutionTests
{
    [Fact] public void FullPageImageKeepsItsOriginalGridAndSourceBytes()
    {
        var bytes=Pdf();var before=bytes.ToArray();
        Assert.Equal((800,1000),RecoveryPdfImageResolution.Inspect(bytes)[1]);
        Assert.Equal(before,bytes);
    }
    [Theory]
    [InlineData("/Rotate 90", "q 400 0 0 500 0 0 cm /I Do Q")]
    [InlineData("/CropBox [1 0 400 500]", "q 400 0 0 500 0 0 cm /I Do Q")]
    [InlineData("/Annots []", "q 400 0 0 500 0 0 cm /I Do Q")]
    [InlineData("/Group << /S /Transparency >>", "q 400 0 0 500 0 0 cm /I Do Q")]
    [InlineData("/UserUnit 2", "q 400 0 0 500 0 0 cm /I Do Q")]
    [InlineData("", "q -400 0 0 500 400 0 cm /I Do Q")]
    [InlineData("", "q 400 1 0 500 0 0 cm /I Do Q")]
    [InlineData("", "q 400 0 0 500 1 0 cm /I Do Q")]
    [InlineData("", "q 400 0 0 500 0 0 cm /I Do /I Do Q")]
    [InlineData("", "q 0 0 400 500 re W n 400 0 0 500 0 0 cm /I Do Q")]
    [InlineData("", "q 400 0 0 500 0 0 cm /I Do Q 10 10 10 10 re f")]
    public void OtherVisibleOrTransformedContentKeepsTheOriginalRenderChoice(string pageExtra,string content)
        =>Assert.Empty(RecoveryPdfImageResolution.Inspect(Pdf(pageExtra:pageExtra,content:content)));
    [Theory] [InlineData(400,500)] [InlineData(800,999)] [InlineData(2401,3001)] [InlineData(800,3201)]
    public void LowDensityDistortedOrOversizedGridIsNotSelected(int width,int height)
        =>Assert.Empty(RecoveryPdfImageResolution.Inspect(Pdf(width,height)));
    [Fact] public void InheritedAlternateUnitsKeepRenderChoice()
        =>Assert.Empty(RecoveryPdfImageResolution.Inspect(Pdf(parentExtra:"/UserUnit 2")));
    [Fact] public void ImageInsidePaintedFormIsNotAWholePageImage()
        =>Assert.Empty(RecoveryPdfImageResolution.Inspect(Pdf(form:true)));
    [Theory] [InlineData("/OutputIntents []")] [InlineData("/OCProperties << >>")]
    public void CatalogColourAndOptionalLayersKeepFullRender(string extra)
        =>Assert.Empty(RecoveryPdfImageResolution.Inspect(Pdf(catalogExtra:extra)));
    [Fact] public void DefaultColourSpaceOverrideKeepsFullRender()
        =>Assert.Empty(RecoveryPdfImageResolution.Inspect(Pdf(resourceExtra:"/ColorSpace << /DefaultRGB /DeviceRGB >>")));
    [Fact] public void MalformedInputAndCancellationRemainSeparate()
    {
        Assert.Empty(RecoveryPdfImageResolution.Inspect("not a PDF"u8.ToArray()));
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(()=>RecoveryPdfImageResolution.Inspect(Pdf(),cancelled.Token));
    }
    internal static byte[] Pdf(int width=800,int height=1000,string pageExtra="",string parentExtra="",string content="q 400 0 0 500 0 0 cm /I Do Q",string imageExtra="",string colorSpace="/DeviceRGB",byte[]? samples=null,bool form=false,string catalogExtra="",string resourceExtra="")
    {
        using var raw=new MemoryStream();using(var compressed=new ZLibStream(raw,CompressionLevel.Fastest,true))compressed.Write(samples ?? new byte[checked(width*height*3)]);
        var image=raw.ToArray();
        byte[] Stream(string dictionary,byte[] data)
            =>Encoding.ASCII.GetBytes($"<< {dictionary} /Length {data.Length} >>\nstream\n").Concat(data).Concat("\nendstream"u8.ToArray()).ToArray();
        var objects=new List<byte[]>{
            Encoding.ASCII.GetBytes($"<< /Type /Catalog /Pages 2 0 R {catalogExtra} >>"),
            Encoding.ASCII.GetBytes($"<< /Type /Pages /Kids [3 0 R] /Count 1 {parentExtra} >>"),
            Encoding.ASCII.GetBytes($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 500] /Resources << /XObject << /I {(form?6:5)} 0 R >> {resourceExtra} >> /Contents 4 0 R {pageExtra} >>"),
            Stream("",Encoding.ASCII.GetBytes(content)),
            Stream($"/Type /XObject /Subtype /Image /Width {width} /Height {height} /BitsPerComponent 8 /ColorSpace {colorSpace} /Filter /FlateDecode {imageExtra}",image)};
        if(form)objects.Add(Stream("/Type /XObject /Subtype /Form /BBox [0 0 1 1] /Resources << /XObject << /J 5 0 R >> >>",Encoding.ASCII.GetBytes("/J Do 0 0 1 1 re f")));
        using var pdf=new MemoryStream();void Write(string text)=>pdf.Write(Encoding.ASCII.GetBytes(text));
        Write("%PDF-1.7\n");var offsets=new List<long>{0};
        for(var i=0;i<objects.Count;i++){offsets.Add(pdf.Position);Write($"{i+1} 0 obj\n");pdf.Write(objects[i]);Write("\nendobj\n");}
        var xref=pdf.Position;Write($"xref\n0 {objects.Count+1}\n0000000000 65535 f \n");
        foreach(var offset in offsets.Skip(1))Write(offset.ToString("D10",System.Globalization.CultureInfo.InvariantCulture)+" 00000 n \n");
        Write($"trailer\n<< /Size {objects.Count+1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");return pdf.ToArray();
    }
}
