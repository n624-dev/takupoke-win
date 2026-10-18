using System.IO.Compression;
using System.Text;
using Xunit;

public sealed class OriginalImageReadTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OriginalSamplesAndChannelOrderAreExactWithoutRendering(bool compress)
    {
        var rgb = new byte[] { 1, 25, 100, 255, 255, 255, 0, 0, 0, 254, 247, 239 };
        var pages = OriginalImageRead.Read(Pdf(rgb, compress), CancellationToken.None);
        var page = Assert.Single(pages);
        Assert.Equal(2, page.Width); Assert.Equal(2, page.Height);
        Assert.Equal(new byte[] { 100, 25, 1, 255, 255, 255, 255, 255, 0, 0, 0, 255, 239, 247, 254, 255 }, page.Bgra);
    }

    [Theory]
    [InlineData("q 0.5 0 0 1 0 0 cm /I Do Q")]
    [InlineData("q 1 0.1 0 1 0 0 cm /I Do Q")]
    [InlineData("q 1 0 0 1 0.1 0 cm /I Do Q")]
    [InlineData("q 1 0 0 -1 0 1 cm /I Do Q")]
    [InlineData("q 1 0 0 1 0 0 cm /I Do Q 0 0 1 1 re f")]
    [InlineData("q 1 0 0 1 0 0 cm /I Do /I Do Q")]
    [InlineData("q 0 0 1 1 re W n 1 0 0 1 0 0 cm /I Do Q")]
    public void PartialRotatedClippedDuplicateOrOverpaintedDrawingsRefuse(string operations)
    {
        Assert.Throws<InvalidDataException>(() => OriginalImageRead.Read(Pdf(new byte[12], operations: operations), CancellationToken.None));
    }

    [Theory]
    [InlineData("/SMask /None")]
    [InlineData("/Mask [0 0 0 0 0 0]")]
    [InlineData("/ImageMask false")]
    [InlineData("/Decode [0 1 0 1 0 1]")]
    [InlineData("/Interpolate true")]
    [InlineData("/OC 9 0 R")]
    [InlineData("/DecodeParms << /Predictor 1 >>")]
    public void UnsupportedImageStateCannotBecomeVerifiedOpaquePixels(string imageState)
    {
        Assert.Throws<InvalidDataException>(() => OriginalImageRead.Read(Pdf(new byte[12], imageState: imageState), CancellationToken.None));
    }

    [Theory]
    [InlineData("/Annots []")]
    [InlineData("/Group << /S /Transparency >>")]
    [InlineData("/UserUnit 2")]
    [InlineData("/Rotate 90")]
    public void AlternatePageStateRefuses(string pageState)
    {
        Assert.Throws<InvalidDataException>(() => OriginalImageRead.Read(Pdf(new byte[12], pageState: pageState), CancellationToken.None));
    }

    [Fact]
    public void SourceColorDefaultsCannotBeIgnored()
    {
        Assert.Throws<InvalidDataException>(() => OriginalImageRead.Read(Pdf(new byte[12], resourceState: "/ColorSpace << /DefaultRGB /DeviceRGB >>"), CancellationToken.None));
    }

    [Theory]
    [InlineData(11)]
    [InlineData(13)]
    public void RawOrInflatedLengthMustExactlyMatchDimensions(int size)
    {
        Assert.Throws<InvalidDataException>(() => OriginalImageRead.Read(Pdf(new byte[size], false), CancellationToken.None));
        Assert.Throws<InvalidDataException>(() => OriginalImageRead.Read(Pdf(new byte[size], true), CancellationToken.None));
    }

    [Fact]
    public void CancellationIsObservedBeforeDecoding()
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => OriginalImageRead.Read(Pdf(new byte[12]), cancel.Token));
    }

    private static byte[] Pdf(byte[] rgb, bool compress = false, string operations = "q 1 0 0 1 0 0 cm /I Do Q",
        string imageState = "", string pageState = "", string resourceState = "")
    {
        byte[] A(string text) => Encoding.ASCII.GetBytes(text);
        var pixels = rgb;
        if (compress)
        {
            using var buffer = new MemoryStream();
            using (var zip = new ZLibStream(buffer, CompressionLevel.SmallestSize, true)) zip.Write(rgb);
            pixels = buffer.ToArray();
        }
        var content = A(operations);
        var objects = new[] {
            A("<< /Type /Catalog /Pages 2 0 R >>"),
            A("<< /Type /Pages /Count 1 /Kids [3 0 R] >>"),
            A($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] {pageState} /Resources << /XObject << /I 4 0 R >> {resourceState} >> /Contents 5 0 R >>"),
            A($"<< /Type /XObject /Subtype /Image /Width 2 /Height 2 /ColorSpace /DeviceRGB /BitsPerComponent 8 {(compress ? "/Filter /FlateDecode" : "")} {imageState} /Length {pixels.Length} >>\nstream\n").Concat(pixels).Concat(A("\nendstream")).ToArray(),
            A($"<< /Length {content.Length} >>\nstream\n").Concat(content).Concat(A("\nendstream")).ToArray() };
        using var pdf = new MemoryStream(); pdf.Write(A("%PDF-1.4\n")); var offsets = new List<long> { 0 };
        for (var i = 0; i < objects.Length; i++)
        { offsets.Add(pdf.Position); pdf.Write(A($"{i + 1} 0 obj\n")); pdf.Write(objects[i]); pdf.Write(A("\nendobj\n")); }
        var xref = pdf.Position; pdf.Write(A($"xref\n0 {offsets.Count}\n0000000000 65535 f \n"));
        foreach (var offset in offsets.Skip(1)) pdf.Write(A($"{offset:0000000000} 00000 n \n"));
        pdf.Write(A($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"));
        return pdf.ToArray();
    }
}
