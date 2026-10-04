using System.Security.Cryptography;
using System.IO.Compression;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

// Same PDF render dimensions/format and actual blank scanners as the product;
// no recognizer, oracle, model download or assumption that absent text is EMPTY.
internal sealed class VectorRasterBlankProof : IDisposable
{
    private readonly List<RecoveryRaster> rasters = [];
    internal List<PdfPageLayout> Pages { get; } = [];
    internal List<object> PixelProvenance { get; } = [];
    internal List<object> BlankQueries { get; } = [];
    private readonly List<Func<RecoveryBox, bool>> blankScanners = [];
    internal int TotalBlankQueries { get; private set; }
    internal bool BlankQueriesTruncated => TotalBlankQueries > BlankQueries.Count;
    internal bool InkFree(int page, RecoveryBox box)
    {
        TotalBlankQueries++;
        var answer = blankScanners[page - 1](box);
        if (BlankQueries.Count < 2000) BlankQueries.Add(new { page, box, inkFree = answer });
        return answer;
    }
    internal static async Task<VectorRasterBlankProof> Create(byte[] bytes, RecoveryReadCapture capture, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows() || !capture.Complete) throw new InvalidOperationException("Complete actual vector capture and Windows renderer required.");
        var result = new VectorRasterBlankProof();
        try
        {
            using var input = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(input)) { writer.WriteBytes(bytes); await writer.StoreAsync().AsTask(token); writer.DetachStream(); }
            input.Seek(0); var pdf = await PdfDocument.LoadFromStreamAsync(input).AsTask(token);
            if (pdf.PageCount != capture.Pages.Count || pdf.PageCount is < 1 or > 12) throw new InvalidDataException("Page capture mismatch.");
            for (uint i = 0; i < pdf.PageCount; i++)
            {
                token.ThrowIfCancellationRequested(); using var page = pdf.GetPage(i); using var image = new InMemoryRandomAccessStream();
                var width = (uint)Math.Clamp(Math.Round(page.Size.Width * 2), 640, 2400); var height = (uint)Math.Round(page.Size.Height / page.Size.Width * width);
                if (height > 3200) { width = (uint)Math.Round(width * 3200d / height); height = 3200; }
                await page.RenderToStreamAsync(image, new PdfPageRenderOptions { DestinationWidth = width, DestinationHeight = height }).AsTask(token);
                image.Seek(0); var decoder = await BitmapDecoder.CreateAsync(image).AsTask(token);
                using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore).AsTask(token);
                var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(token);
                var raster = new RecoveryRaster(bitmap.PixelWidth, bitmap.PixelHeight, pixels.DetachPixelData()); result.rasters.Add(raster);
                using var compressed = new MemoryStream();
                using (var gzip = new GZipStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true)) gzip.Write(raster.Bgra);
                var rules = raster.Rules(token); var original = capture.Pages[(int)i].Layout!;
                var sx = raster.Width / original.Width; var sy = raster.Height / original.Height;
                result.Pages.Add(new(raster.Width, raster.Height,
                    original.Glyphs.Select(g => g with { X = g.X * sx, Y = g.Y * sy, Width = g.Width * sx, Height = g.Height * sy }).ToArray(),
                    original.Lines.Select(l => new PdfRule(l.X1 * sx, l.Y1 * sy, l.X2 * sx, l.Y2 * sy)).Concat(rules).ToArray()));
                result.blankScanners.Add(raster.InkFreeScanner(raster.RuleMask(rules, token), token));
                result.PixelProvenance.Add(new { page = i + 1, width = raster.Width, height = raster.Height,
                    bgraSha256 = Convert.ToHexStringLower(SHA256.HashData(raster.Bgra)), bgraBytes = raster.Bgra.Length, bgraGzipBase64 = Convert.ToBase64String(compressed.ToArray()), sx, sy, detectedPhysicalRules = rules.Count });
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }
    public void Dispose() { foreach (var raster in rasters) CryptographicOperations.ZeroMemory(raster.Bgra); }
}
