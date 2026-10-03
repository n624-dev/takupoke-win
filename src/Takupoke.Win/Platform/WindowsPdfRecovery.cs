using System.Security.Cryptography;
using Microsoft.Graphics.Imaging;
using Microsoft.Windows.AI;
using Microsoft.Windows.AI.Imaging;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Takupoke.Win.Platform;
public sealed class WindowsPdfRecovery(WindowsRecoveryModels models)
{
    public async Task<RecoveryDocument> BuildAsync(byte[] bytes, MaterialKind kind, string hash, RecoveryReadCapture capture, CancellationToken token)
    {
        using var input = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(input)) { writer.WriteBytes(bytes); await writer.StoreAsync().AsTask(token); writer.DetachStream(); }
        input.Seek(0); var pdf = await PdfDocument.LoadFromStreamAsync(input).AsTask(token);
        if (pdf.PageCount is < 1 or > 12) throw new PdfParseException("limit");
        var pages = new List<PdfPageLayout>(); var rasters = new List<RecoveryRaster>(); var ocrPages = new HashSet<int>(); OnnxJapaneseOcr? ocr = null;
        try
        {
            for (uint i = 0; i < pdf.PageCount; i++)
            {
                token.ThrowIfCancellationRequested(); using var page = pdf.GetPage(i); using var image = new InMemoryRandomAccessStream();
                var width = (uint)Math.Clamp(Math.Round(page.Size.Width * 2), 640, 2400); var height = (uint)Math.Round(page.Size.Height / page.Size.Width * width);
                if (height > 3200) { width = (uint)Math.Round(width * 3200d / height); height = 3200; }
                await page.RenderToStreamAsync(image, new PdfPageRenderOptions { DestinationWidth = width, DestinationHeight = height }).AsTask(token);
                image.Seek(0); var decoder = await BitmapDecoder.CreateAsync(image).AsTask(token); using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore).AsTask(token);
                var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(token);
                var raster = new RecoveryRaster(bitmap.PixelWidth, bitmap.PixelHeight, pixels.DetachPixelData()); rasters.Add(raster);
                var captured = capture.Pages.FirstOrDefault(p => p.Page == i + 1);
                var layout = captured?.State == RecoveryInputState.Complete ? captured.Layout : null;
                if (layout is not null)
                {
                    var sx = raster.Width / layout.Width; var sy = raster.Height / layout.Height;
                    pages.Add(new(raster.Width, raster.Height, layout.Glyphs.Select(g => g with { X = g.X * sx, Y = g.Y * sy, Width = g.Width * sx, Height = g.Height * sy }).ToArray(),
                        layout.Lines.Select(l => new PdfRule(l.X1 * sx, l.Y1 * sy, l.X2 * sx, l.Y2 * sy)).Concat(raster.Rules()).ToArray()));
                    continue;
                }
                IReadOnlyList<PdfGlyph>? glyphs = null; IReadOnlyList<RecoveryBox> recognizedBoxes = [];
                try
                {
                    if (TextRecognizer.GetReadyState() == AIFeatureReadyState.Ready)
                    {
                        using var recognizer = await TextRecognizer.CreateAsync().AsTask(token); using var buffer = ImageBuffer.CreateForSoftwareBitmap(bitmap);
                        var recognized = await recognizer.RecognizeTextFromImageAsync(buffer).AsTask(token); var output = new List<PdfGlyph>(); var order = 0; var line = 0;
                        foreach (var row in recognized.Lines) { foreach (var word in row.Words) { if (word.MatchConfidence < .8) throw new InvalidDataException("OS OCRの文字を判読できません。"); var b = word.BoundingBox; var x = Math.Min(b.TopLeft.X, b.BottomLeft.X); var y = Math.Min(b.TopLeft.Y, b.TopRight.Y); var right = Math.Max(b.TopRight.X, b.BottomRight.X); var bottom = Math.Max(b.BottomLeft.Y, b.BottomRight.Y); output.Add(new(word.Text, x, y, right - x, bottom - y, line, order++)); } line++; }
                        glyphs = output; recognizedBoxes = output.Select(g => new RecoveryBox(g.X, g.Y, g.Width, g.Height)).ToArray();
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { /* The independent runtime is also available without package identity. */ }
                if (glyphs is null) { ocr ??= await models.OpenOcrAsync(token); glyphs = await Task.Run(() => ocr.Read(raster, token), token); recognizedBoxes = ocr.RecognizedBoxes; }
                var rules = raster.Rules(); if (raster.HasUnrecognizedInk(recognizedBoxes, rules)) throw new InvalidDataException("OCRが認識していない印字があります。読めなかった内容を省略できません。");
                ocrPages.Add((int)i + 1); pages.Add(new(raster.Width, raster.Height, glyphs, rules));
            }
            var ruleMasks = rasters.Select(r => r.RuleMask(r.Rules())).ToArray();
            return await Task.Run(() => RecoveryDocumentBuilder.Build(hash, kind, pages, (page, box) => rasters[page - 1].InkFree(box, ruleMasks[page - 1]), ocrPages, token), token);
        }
        finally { ocr?.Dispose(); foreach (var raster in rasters) CryptographicOperations.ZeroMemory(raster.Bgra); }
    }
}
