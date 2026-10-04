using System.Security.Cryptography;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

internal static class NativeRasterReader
{
    internal static async Task<IReadOnlyList<RecoveryRaster>> RenderAsync(byte[] bytes, CancellationToken token)
    {
        if (bytes.Length > 2000000) throw new InvalidDataException("Research PDF byte envelope exceeded.");
        using var input = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(input)) { writer.WriteBytes(bytes); await writer.StoreAsync().AsTask(token); writer.DetachStream(); }
        input.Seek(0); var pdf = await PdfDocument.LoadFromStreamAsync(input).AsTask(token);
        if (pdf.PageCount is < 1 or > 5) throw new InvalidDataException("Research PDF page envelope exceeded.");
        var result = new List<RecoveryRaster>(); long pixelsTotal = 0;
        try
        {
            for (uint i = 0; i < pdf.PageCount; i++)
            {
                token.ThrowIfCancellationRequested(); using var page = pdf.GetPage(i); using var image = new InMemoryRandomAccessStream();
                // Exact original render dimensions, including its height cap.
                var width = (uint)Math.Clamp(Math.Round(page.Size.Width * 2), 640, 2400); var height = (uint)Math.Round(page.Size.Height / page.Size.Width * width);
                if (height > 3200) { width = (uint)Math.Round(width * 3200d / height); height = 3200; }
                pixelsTotal += (long)width * height; if (pixelsTotal > 40000000) throw new InvalidDataException("Document pixel budget exceeded.");
                await page.RenderToStreamAsync(image, new PdfPageRenderOptions { DestinationWidth = width, DestinationHeight = height }).AsTask(token);
                image.Seek(0); var decoder = await BitmapDecoder.CreateAsync(image).AsTask(token);
                using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore).AsTask(token);
                var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(token);
                var raster = new RecoveryRaster(bitmap.PixelWidth, bitmap.PixelHeight, pixels.DetachPixelData()); result.Add(raster);
                if (!raster.Valid || raster.Width != width || raster.Height != height) throw new InvalidDataException("Rendered raster differs from bounded dimensions.");
            }
            return result;
        }
        catch { foreach (var raster in result) CryptographicOperations.ZeroMemory(raster.Bgra); throw; }
    }
    internal static RecoveryDocument Build(IReadOnlyList<RecoveryRaster> rasters, string pdfHash, MaterialKind kind, bool tiled, OnnxJapaneseOcr ocr, Action<object> evidence, CancellationToken token)
    {
        var layouts = new List<PdfPageLayout>(); var rulesByPage = new List<IReadOnlyList<PdfRule>>(); var tilesTotal = 0;
        var ownedReader = new OwnedTileOcr(ocr);
        for (var page = 0; page < rasters.Count; page++)
        {
            token.ThrowIfCancellationRequested(); var raster = rasters[page]; var rules = raster.Rules(token); rulesByPage.Add(rules);
            IReadOnlyList<PdfGlyph> glyphs; IReadOnlyList<RecoveryBox> recognized;
            if (!tiled)
            {
                glyphs = ocr.Read(raster, token); recognized = ocr.RecognizedBoxes.ToArray();
            }
            else
            {
                var regions = new List<OwnedRegion>();
                foreach (var tile in TileGeometry.Grid(raster.Width, raster.Height))
                {
                    if (++tilesTotal > 128) throw new InvalidDataException("Document tile budget exceeded.");
                    var crop = TileGeometry.Crop(raster, tile, token);
                    try
                    {
                        var output = ownedReader.Read(crop, tile, raster.Width, raster.Height, observation => evidence(new { page = page + 1, tile.Index, recognition = observation }), token); regions.AddRange(output);
                        if (regions.Count > 10000) throw new InvalidDataException("Page owned region count exceeded.");
                        evidence(new { page = page + 1, tile, originalCropBgraSHA256 = Convert.ToHexStringLower(SHA256.HashData(crop.Bgra)), ownedRegions = output });
                    }
                    finally { CryptographicOperations.ZeroMemory(crop.Bgra); }
                }
                glyphs = TileGeometry.Merge(regions, token); recognized = regions.Select(r => r.Box).ToArray();
            }
            // Entire original page is checked, not merely crop/owner cores.
            if (raster.HasUnrecognizedInk(recognized, rules, token)) throw new InvalidDataException("OCRが認識していない印字があります。読めなかった内容を省略できません。");
            layouts.Add(new(raster.Width, raster.Height, glyphs, rules));
            evidence(new { page = page + 1, fullPageBgraSHA256 = Convert.ToHexStringLower(SHA256.HashData(raster.Bgra)), recognized, glyphs, inkComplete = true, rules });
        }
        var scanners = rasters.Select((r, index) => r.InkFreeScanner(r.RuleMask(rulesByPage[index], token), token)).ToArray();
        return RecoveryDocumentBuilder.Build(pdfHash, kind, layouts, (page, box) => scanners[page - 1](box), Enumerable.Range(1, rasters.Count).ToHashSet(), token, allowStructureProposal: true);
    }
}
