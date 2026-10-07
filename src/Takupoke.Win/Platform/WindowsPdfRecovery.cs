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
public sealed record RecoveryRuleEndpointCapture(PdfRule Rule, int X, int Y, int Width, int Height, int[] MinimumChannels, int[] MeanChannels);
public sealed record RecoveryRasterCaptureInfo(int Page, int Width, int Height, string Method, string BgraSha256, string ColourManagedRgbSha256, IReadOnlyList<RecoveryRuleEndpointCapture> RuleEndpoints);
public sealed record RecoveryNativeSourceCapture(string Id, int Page, PdfGlyph Glyph, double Confidence);
public sealed class WindowsPdfRecovery(WindowsRecoveryModels models)
{
    public async Task<RecoveryDocument> BuildAsync(byte[] bytes, MaterialKind kind, string hash, RecoveryReadCapture capture, CancellationToken token, Action<RecoveryRasterCaptureInfo>? observer = null,
        Action<RecoveryNativeSourceCapture>? sourceObserver = null, Action<RecoveryBuilderTrace>? builderObserver = null)
    {
        var capturedDocument = await Task.Run(() => RecoveryCapturedLayoutBuilder.TryBuildWithoutRaster(hash, kind, capture, token), token);
        if (capturedDocument is not null) return capturedDocument;
        var originalImageSizes = await Task.Run(() => RecoveryPdfImageResolution.Inspect(bytes, token), token);
        using var input = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(input)) { writer.WriteBytes(bytes); await writer.StoreAsync().AsTask(token); writer.DetachStream(); }
        input.Seek(0); var pdf = await PdfDocument.LoadFromStreamAsync(input).AsTask(token);
        if (pdf.PageCount is < 1 or > 12) throw new PdfParseException("limit");
        var pages = new List<PdfPageLayout>(); var rasters = new List<RecoveryRaster>(); var rasterRules = new List<IReadOnlyList<PdfRule>>(); var ocrPages = new HashSet<int>(); OnnxJapaneseOcr? ocr = null;
        var nativeConfidence = new Dictionary<string, double>(); var completeInk = new List<bool>();
        try
        {
            for (uint i = 0; i < pdf.PageCount; i++)
            {
                token.ThrowIfCancellationRequested(); using var page = pdf.GetPage(i); using var image = new InMemoryRandomAccessStream();
                var width = (uint)Math.Clamp(Math.Round(page.Size.Width * 2), 640, 2400); var height = (uint)Math.Round(page.Size.Height / page.Size.Width * width);
                if (height > 3200) { width = (uint)Math.Round(width * 3200d / height); height = 3200; }
                if (originalImageSizes.TryGetValue((int)i + 1, out var originalSize)) { width = (uint)originalSize.Width; height = (uint)originalSize.Height; }
                using var original = originalImageSizes.ContainsKey((int)i + 1) ? await Task.Run(() => RecoveryPdfOriginalImage.TryRead(bytes, (int)i + 1, token), token) : null;
                if (original is not null)
                {
                    using var writer = new DataWriter(image); writer.WriteBytes(original.Png); await writer.StoreAsync().AsTask(token); writer.DetachStream();
                }
                else await page.RenderToStreamAsync(image, new PdfPageRenderOptions { DestinationWidth = width, DestinationHeight = height }).AsTask(token);
                var colour = original is null ? ColorManagementMode.DoNotColorManage : ColorManagementMode.ColorManageToSRgb;
                image.Seek(0); var decoder = await BitmapDecoder.CreateAsync(image).AsTask(token);
                using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, colour).AsTask(token);
                if (originalImageSizes.ContainsKey((int)i + 1) && (bitmap.PixelWidth != width || bitmap.PixelHeight != height))
                    throw new InvalidDataException("元画像の描画寸法を確認できません。");
                var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, colour).AsTask(token);
                var raster = new RecoveryRaster(bitmap.PixelWidth, bitmap.PixelHeight, pixels.DetachPixelData()); rasters.Add(raster);
                var rules = await Task.Run(() => raster.Rules(token), token); rasterRules.Add(rules);
                if (observer is not null) observer(new((int)i + 1, raster.Width, raster.Height, original is null ? "original-PDF-render" : "original-image-with-colour-profile", Convert.ToHexStringLower(SHA256.HashData(raster.Bgra)), RgbHash(raster, token), CaptureRuleEndpoints(raster, rules, token)));
                var captured = capture.Pages.FirstOrDefault(p => p.Page == i + 1);
                var layout = captured?.State == RecoveryInputState.Complete ? captured.Layout : null;
                if (layout is not null)
                {
                    var sx = raster.Width / layout.Width; var sy = raster.Height / layout.Height;
                    pages.Add(new(raster.Width, raster.Height, layout.Glyphs.Select(g => g with { X = g.X * sx, Y = g.Y * sy, Width = g.Width * sx, Height = g.Height * sy }).ToArray(),
                        layout.Lines.Select(l => new PdfRule(l.X1 * sx, l.Y1 * sy, l.X2 * sx, l.Y2 * sy)).Concat(rules).ToArray()));
                    completeInk.Add(false); // Manual coverage is checked only if low-confidence OCR needs it.
                    continue;
                }
                IReadOnlyList<PdfGlyph>? glyphs = null; IReadOnlyList<RecoveryBox> recognizedBoxes = []; IReadOnlyList<double>? confidences = null;
                try
                {
                    if (TextRecognizer.GetReadyState() == AIFeatureReadyState.Ready)
                    {
                        using var recognizer = await TextRecognizer.CreateAsync().AsTask(token); using var buffer = ImageBuffer.CreateForSoftwareBitmap(bitmap);
                        var recognized = await recognizer.RecognizeTextFromImageAsync(buffer).AsTask(token); var output = new List<PdfGlyph>(); var scores = new List<double>(); var order = 0; var line = 0;
                        foreach (var row in recognized.Lines) { foreach (var word in row.Words) { if (!double.IsFinite(word.MatchConfidence) || word.MatchConfidence < .8 || word.MatchConfidence > 1) throw new InvalidDataException("OS OCRの文字を判読できません。"); var b = word.BoundingBox; var x = Math.Min(b.TopLeft.X, b.BottomLeft.X); var y = Math.Min(b.TopLeft.Y, b.TopRight.Y); var right = Math.Max(b.TopRight.X, b.BottomRight.X); var bottom = Math.Max(b.BottomLeft.Y, b.BottomRight.Y); output.Add(new(word.Text, x, y, right - x, bottom - y, line, order++)); scores.Add(word.MatchConfidence); } line++; }
                        glyphs = output; confidences = scores; recognizedBoxes = output.Select(g => new RecoveryBox(g.X, g.Y, g.Width, g.Height)).ToArray();
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { /* The independent runtime is also available without package identity. */ }
                if (glyphs is null) { ocr ??= await models.OpenOcrAsync(token); glyphs = await Task.Run(() => ocr.ReadForManualCapture(raster, token), token); recognizedBoxes = ocr.RecognizedBoxes; confidences = ocr.NativeConfidences; }
                if (await Task.Run(() => raster.HasUnrecognizedInk(recognizedBoxes, rules, token), token)) throw new InvalidDataException("OCRが認識していない印字があります。読めなかった内容を省略できません。");
                if (confidences is null || confidences.Count != glyphs.Count) throw new InvalidDataException("OCRの原文と確信度の対応を確認できません。");
                for (var n = 0; n < glyphs.Count; n++)
                {
                    var id = $"p{i + 1}s{n}";
                    nativeConfidence.Add(id, confidences[n]);
                    sourceObserver?.Invoke(new(id, (int)i + 1, glyphs[n], confidences[n]));
                }
                completeInk.Add(true);
                ocrPages.Add((int)i + 1); pages.Add(new(raster.Width, raster.Height, glyphs, rules));
            }
            if (nativeConfidence.Values.Any(score => score < .8))
                for (var index = 0; index < pages.Count; index++)
                    if (!ocrPages.Contains(index + 1))
                    {
                        var nativeBoxes = pages[index].Glyphs.Select(g => new RecoveryBox(g.X, g.Y, g.Width, g.Height)).ToArray();
                        try { completeInk[index] = !await Task.Run(() => rasters[index].HasUnrecognizedInk(nativeBoxes, rasterRules[index], token), token); }
                        catch (InvalidDataException) { completeInk[index] = false; } // This document remains ineligible for manual correction.
                    }
            return await Task.Run(() =>
            {
                var ruleMasks = rasters.Select((r, index) => r.RuleMask(rasterRules[index], token)).ToArray();
                var blankScanners = rasters.Select((r, index) => r.InkFreeScanner(ruleMasks[index], token)).ToArray();
                var document = RecoveryDocumentBuilder.Build(hash, kind, pages, (page, box) => blankScanners[page - 1](box), ocrPages, token, allowStructureProposal: true, observer: builderObserver);
                var sources = document.Sources.Select(source => nativeConfidence.TryGetValue(source.Id, out var score) ? source with { NativeConfidence = score } : source).ToArray();
                if (nativeConfidence.Count == 0) return document; // Keep the existing native-only acquisition contract.
                var proof = rasters.Select((r, index) => new RecoveryCapturedPage(index + 1, r.Width, r.Height,
                    Convert.ToHexStringLower(SHA256.HashData(r.Bgra)), sources.Count(s => s.Page == index + 1), completeInk[index])).ToArray();
                token.ThrowIfCancellationRequested();
                document = document with { Sources = sources, Capture = new(hash, (int)pdf.PageCount, RecoveryValidator.Fingerprint(sources), proof) };
                var targets = document.Sources.Any(s => s.NativeConfidence is < .8)
                    ? RecoveryManualAssistance.CaptureTargets(document, token) : null;
                if (targets is { Count: > 0 and <= RecoveryManualAssistance.MaximumFields })
                {
                    var crops = new List<RecoveryOriginalCrop>();
                    try
                    {
                        foreach (var target in targets)
                        {
                            token.ThrowIfCancellationRequested();
                            var raster = rasters[target.Page - 1]; var x = (int)Math.Floor(target.Crop.X); var y = (int)Math.Floor(target.Crop.Y);
                            var w = (int)Math.Ceiling(target.Crop.X + target.Crop.Width) - x;
                            var h = (int)Math.Ceiling(target.Crop.Y + target.Crop.Height) - y;
                            if (x < 0 || y < 0 || w < 1 || h < 1 || (long)w * h > RecoveryManualAssistance.MaximumCropPixels ||
                                (long)x + w > raster.Width || (long)y + h > raster.Height) break;
                            var crop = new byte[checked(w * h * 4)];
                            try
                            {
                                for (var row = 0; row < h; row++)
                                {
                                    token.ThrowIfCancellationRequested();
                                    System.Buffer.BlockCopy(raster.Bgra, ((y + row) * raster.Width + x) * 4, crop, row * w * 4, w * 4);
                                }
                                crops.Add(new(target.Target, target.Page, x, y, w, h, proof[target.Page - 1].RasterHash,
                                    crop, Convert.ToHexStringLower(SHA256.HashData(crop))));
                            }
                            catch { CryptographicOperations.ZeroMemory(crop); throw; }
                        }
                        token.ThrowIfCancellationRequested();
                        if (crops.Count == targets.Count) document = document with { Capture = document.Capture! with { OriginalCrops = crops } };
                        else foreach (var crop in crops) CryptographicOperations.ZeroMemory(crop.Bgra);
                    }
                    catch { foreach (var crop in crops) CryptographicOperations.ZeroMemory(crop.Bgra); throw; }
                }
                return document;
            }, token);
        }
        finally
        {
            await Task.Run(() => { ocr?.Dispose(); foreach (var raster in rasters) CryptographicOperations.ZeroMemory(raster.Bgra); });
        }
    }
    private static IReadOnlyList<RecoveryRuleEndpointCapture> CaptureRuleEndpoints(RecoveryRaster raster, IReadOnlyList<PdfRule> rules, CancellationToken token)
    {
        // Explicit research observer only: bounded physical endpoints, no text
        // candidates, alternate crop, mask tolerance or acceptance changes.
        var selected = new[] {
            rules.Where(r => r.Horizontal).OrderBy(r => r.X1).ThenBy(r => r.Y1).FirstOrDefault(),
            rules.Where(r => r.Horizontal).OrderByDescending(r => r.Y1).FirstOrDefault(),
            rules.Where(r => r.Vertical).OrderBy(r => r.X1).FirstOrDefault()
        }.OfType<PdfRule>().Distinct();
        var output = new List<RecoveryRuleEndpointCapture>();
        foreach (var rule in selected)
        foreach (var (cx, cy) in new[] { (rule.X1, rule.Y1), (rule.X2, rule.Y2) })
        {
            token.ThrowIfCancellationRequested();
            var left = Math.Max(0, (int)Math.Floor(cx) - 4); var top = Math.Max(0, (int)Math.Floor(cy) - 4);
            var width = Math.Min(9, raster.Width - left); var height = Math.Min(9, raster.Height - top);
            if (width < 1 || height < 1) continue;
            var minimum = new int[width * height]; var mean = new int[width * height];
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            {
                var p = ((top + y) * raster.Width + left + x) * 4;
                minimum[y * width + x] = Math.Min(raster.Bgra[p], Math.Min(raster.Bgra[p + 1], raster.Bgra[p + 2]));
                mean[y * width + x] = (raster.Bgra[p] + raster.Bgra[p + 1] + raster.Bgra[p + 2]) / 3;
            }
            output.Add(new(rule, left, top, width, height, minimum, mean));
        }
        return output;
    }
    private static string RgbHash(RecoveryRaster raster, CancellationToken token)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var row = new byte[checked(raster.Width * 3)];
        try
        {
            for (var y = 0; y < raster.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = 0; x < raster.Width; x++)
                {
                    var offset = (y * raster.Width + x) * 4;
                    row[x * 3] = raster.Bgra[offset + 2];
                    row[x * 3 + 1] = raster.Bgra[offset + 1];
                    row[x * 3 + 2] = raster.Bgra[offset];
                }
                digest.AppendData(row);
            }
            return Convert.ToHexStringLower(digest.GetHashAndReset());
        }
        finally { CryptographicOperations.ZeroMemory(row); }
    }
}
