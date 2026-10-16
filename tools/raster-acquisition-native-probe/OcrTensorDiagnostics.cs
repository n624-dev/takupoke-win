using System.Globalization;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Recovery;
using Takupoke.Win.Platform;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

// Research-only, fixed fictional inputs. Read-only reflection reuses production
// tensor preprocessing and sessions; this never emits a recovery proposal.
internal static class OcrTensorDiagnostics
{
    internal static async Task<object> RunAsync(string id, byte[] pdfBytes, WindowsRecoveryModels models, CancellationToken token)
    {
        using var input = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(input)) { writer.WriteBytes(pdfBytes); await writer.StoreAsync().AsTask(token); writer.DetachStream(); }
        input.Seek(0); var pdf = await PdfDocument.LoadFromStreamAsync(input).AsTask(token);
        // First-page component inspection. This does not claim to identify the
        // failed baseline page or which uninstrumented OS/fallback branch ran.
        using var page = pdf.GetPage(0); using var image = new InMemoryRandomAccessStream();
        var width = (uint)Math.Clamp(Math.Round(page.Size.Width * 2), 640, 2400); var height = (uint)Math.Round(page.Size.Height / page.Size.Width * width);
        if (height > 3200) { width = (uint)Math.Round(width * 3200d / height); height = 3200; }
        await page.RenderToStreamAsync(image, new PdfPageRenderOptions { DestinationWidth = width, DestinationHeight = height }).AsTask(token);
        image.Seek(0); var decoder = await BitmapDecoder.CreateAsync(image).AsTask(token);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore).AsTask(token);
        var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(token);
        var raster = new RecoveryRaster(bitmap.PixelWidth, bitmap.PixelHeight, pixels.DetachPixelData());
        try
        {
            using var ocr = await models.OpenOcrAsync(token);
            var detector = (InferenceSession)typeof(OnnxJapaneseOcr).GetField("_detector", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ocr)!;
            var recognizer = (InferenceSession)typeof(OnnxJapaneseOcr).GetField("_recognizer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ocr)!;
            var dictionary = (string[])typeof(OnnxJapaneseOcr).GetField("_dictionary", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ocr)!;
            var preprocessing = typeof(OnnxJapaneseOcr).GetMethod("Image", BindingFlags.Static | BindingFlags.NonPublic)!;
            DenseTensor<float> Tensor(RecoveryBox crop, int w, int h, bool detection) =>
                (DenseTensor<float>)preprocessing.Invoke(null, [raster, crop, w, h, detection])!;
            var ratio = Math.Min(1, 960d / Math.Max(raster.Width, raster.Height));
            var dw = Math.Max(32, (int)Math.Round(raster.Width * ratio / 32) * 32); var dh = Math.Max(32, (int)Math.Round(raster.Height * ratio / 32) * 32);
            var detectorInput = Tensor(new(0, 0, raster.Width, raster.Height), dw, dh, true);
            using var detection = detector.Run([NamedOnnxValue.CreateFromTensor("x", detectorInput)]);
            var map = detection.First().AsTensor<float>(); var detectorSummary = Summary(map, token);
            var validMap = map.Dimensions.Length == 4 && map.Dimensions[0] == 1 && map.Dimensions[1] == 1 && map.Dimensions[2] == dh && map.Dimensions[3] == dw && !map.Any(score => !float.IsFinite(score) || score is < 0 or > 1);
            var crops = new List<object>();
            if (validMap)
            {
                var visited = new bool[dw * dh]; var boxes = new List<RecoveryBox>();
                // Same production DB connected-component/crop recipe; no
                // unclip, confidence change, character repair or adoption.
                for (var y = 0; y < dh; y++) for (var x = 0; x < dw; x++)
                {
                    token.ThrowIfCancellationRequested();
                    if (visited[y * dw + x] || map[0, 0, y, x] < .3) continue;
                    var queue = new Queue<(int X, int Y)>(); queue.Enqueue((x, y)); visited[y * dw + x] = true;
                    var left = x; var top = y; var right = x; var bottom = y; double score = 0; var count = 0;
                    while (queue.TryDequeue(out var p))
                    {
                        count++; score += map[0, 0, p.Y, p.X]; left = Math.Min(left, p.X); right = Math.Max(right, p.X); top = Math.Min(top, p.Y); bottom = Math.Max(bottom, p.Y);
                        foreach (var (nx, ny) in new[] { (p.X - 1, p.Y), (p.X + 1, p.Y), (p.X, p.Y - 1), (p.X, p.Y + 1) })
                            if (nx >= 0 && ny >= 0 && nx < dw && ny < dh && !visited[ny * dw + nx] && map[0, 0, ny, nx] >= .3) { visited[ny * dw + nx] = true; queue.Enqueue((nx, ny)); }
                    }
                    if (count < 6 || score / count < .6) continue;
                    var margin = Math.Max(1, (bottom - top + 1) * .25); var bx = Math.Max(0, (left - margin) * raster.Width / dw); var by = Math.Max(0, (top - margin) * raster.Height / dh); var ex = Math.Min(raster.Width, (right + 1 + margin) * raster.Width / dw); var ey = Math.Min(raster.Height, (bottom + 1 + margin) * raster.Height / dh);
                    boxes.Add(new(bx, by, ex - bx, ey - by));
                    if (boxes.Count > 10000) throw new InvalidDataException("Production detector candidate limit exceeded.");
                }
                foreach (var box in boxes.OrderBy(b => b.Y).ThenBy(b => b.X).Take(64))
                {
                    token.ThrowIfCancellationRequested(); var cw = Math.Clamp((int)Math.Ceiling(box.Width / box.Height * 48), 16, 960);
                    var recognitionInput = Tensor(box, cw, 48, false);
                    using var recognition = recognizer.Run([NamedOnnxValue.CreateFromTensor("x", recognitionInput)]);
                    var logits = recognition.First().AsTensor<float>(); var pieces = new List<(string Text, int Start, int End, float Confidence)>();
                    var shapeValid = logits.Dimensions.Length == 3 && logits.Dimensions[0] == 1 && logits.Dimensions[2] == 18385;
                    var probabilitiesValid = !logits.Any(v => !float.IsFinite(v) || v is < 0 or > 1);
                    if (shapeValid && probabilitiesValid)
                    {
                        var previous = -1;
                        for (var t = 0; t < logits.Dimensions[1]; t++)
                        {
                            var best = 0; var score = logits[0, t, 0]; for (var c = 1; c < 18385; c++) if (logits[0, t, c] > score) { score = logits[0, t, c]; best = c; }
                            if (best != 0 && best != previous) pieces.Add((dictionary[best], t, t + 1, score));
                            else if (best != 0 && pieces.Count > 0) { var last = pieces[^1]; pieces[^1] = last with { End = t + 1, Confidence = Math.Max(last.Confidence, score) }; }
                            previous = best;
                        }
                    }
                    var px = Math.Clamp((int)Math.Floor(box.X), 0, raster.Width - 1); var py = Math.Clamp((int)Math.Floor(box.Y), 0, raster.Height - 1);
                    var pw = Math.Min(raster.Width, (int)Math.Ceiling(box.X + box.Width)) - px; var ph = Math.Min(raster.Height, (int)Math.Ceiling(box.Y + box.Height)) - py;
                    var cropPixels = new byte[pw * ph * 4];
                    for (var row = 0; row < ph; row++) System.Buffer.BlockCopy(raster.Bgra, ((py + row) * raster.Width + px) * 4, cropPixels, row * pw * 4, pw * 4);
                    var cropPng = await Png(cropPixels, pw, ph, token);
                    crops.Add(new { regionId = "region-" + crops.Count, box, input = Summary(recognitionInput, token, probabilityOutput: false), output = Summary(logits, token),
                        text = string.Concat(pieces.Select(p => p.Text)), pieces = pieces.Select(p => new { p.Text, p.Start, p.End, confidence = F(p.Confidence), bits = BitConverter.SingleToInt32Bits(p.Confidence) }),
                        cropEnvelope = new { originalX = px, originalY = py, width = pw, height = ph, bgraSha256 = Sha(cropPixels), pngSha256 = Sha(cropPng), pngBase64 = Convert.ToBase64String(cropPng),
                            scope = "Integer envelope for inspection only; production preprocessing directly samples original fractional box, not this envelope PNG" },
                        emptyCtc = pieces.Count == 0, belowOriginalPoint8 = pieces.Any(p => p.Confidence < .8f), shapeValid, probabilitiesValid,
                        transform = new { cropOriginalBox = box, tensorWidth = cw, tensorHeight = 48, mapping = "originalX=box.X+(tensorX+.5)*box.Width/tensorWidth; same forY, then floor/clamp; nearest source sampling" } });
                }
            }
            object? optimizerOff = null;
            if (id == "Timetable-confusable-2")
            {
                var installed = await models.OcrStateAsync(token) ?? throw new InvalidDataException("Pinned OCR installation missing.");
                using var options = new SessionOptions { IntraOpNumThreads = 2, InterOpNumThreads = 1, GraphOptimizationLevel = GraphOptimizationLevel.ORT_DISABLE_ALL, LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
                using var off = new InferenceSession(Path.Combine(installed.Path, "det.onnx"), options);
                using var output = off.Run([NamedOnnxValue.CreateFromTensor("x", detectorInput)]);
                optimizerOff = new { sameInput = true, profile = "Single diagnostic optimizer-disabled contrast, not recovery recipe", output = Summary(output.First().AsTensor<float>(), token) };
            }
            var pngBytes = await Png(raster.Bgra, raster.Width, raster.Height, token);
            var nativeRuntime = Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Where(module => Path.GetFileName(module.FileName).Equals("onnxruntime.dll", StringComparison.OrdinalIgnoreCase))
                .Select(module => new { name = Path.GetFileName(module.FileName), module.FileVersionInfo.FileVersion, sha256 = Sha(File.ReadAllBytes(module.FileName)) }).ToArray();
            return new { id, page = 1, sourcePdfSha256 = Convert.ToHexStringLower(SHA256.HashData(pdfBytes)), raster.Width, raster.Height,
                originalRenderedBgraSha256 = Convert.ToHexStringLower(SHA256.HashData(raster.Bgra)), originalRenderedPngSha256 = Convert.ToHexStringLower(SHA256.HashData(pngBytes)), originalRenderedPngBase64 = Convert.ToBase64String(pngBytes),
                managedOrtVersion = typeof(InferenceSession).Assembly.GetName().Version?.ToString(), managedOrtSha256 = Sha(File.ReadAllBytes(typeof(InferenceSession).Assembly.Location)), nativeRuntime, detectorOptimization = "ORT_ENABLE_ALL; actual production sessions",
                input = Summary(detectorInput, token, probabilityOutput: false), detector = detectorSummary, validMap, dictionaryClasses = dictionary.Length, crops, optimizerOff,
                limit = "First native-rendered page; at most64 original detector crops, diagnostics may inspect later crops past original early rejection; no recovered document/scoring or qualified backend claim" };
        }
        finally { CryptographicOperations.ZeroMemory(raster.Bgra); }
    }
    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static async Task<byte[]> Png(byte[] pixels, int width, int height, CancellationToken token)
    {
        using var stream = new InMemoryRandomAccessStream(); var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream).AsTask(token);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)width, (uint)height, 96, 96, pixels); await encoder.FlushAsync().AsTask(token);
        if (stream.Size > 2_000_000) throw new InvalidDataException("Diagnostic PNG bound exceeded.");
        using var reader = new DataReader(stream.GetInputStreamAt(0)); await reader.LoadAsync((uint)stream.Size).AsTask(token); var bytes = new byte[(int)stream.Size]; reader.ReadBytes(bytes); return bytes;
    }
    private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static object Summary(Tensor<float> tensor, CancellationToken token, bool probabilityOutput = true)
    {
        var finite = 0; var min = float.PositiveInfinity; var max = float.NegativeInfinity; var invalid = new List<object>(); var index = 0; var outsideZeroToOne = 0;
        foreach (var v in tensor)
        {
            if (index % 4096 == 0) token.ThrowIfCancellationRequested();
            if (float.IsFinite(v)) { finite++; min = Math.Min(min, v); max = Math.Max(max, v); }
            if (!float.IsFinite(v) || v is < 0 or > 1) { outsideZeroToOne++; if (invalid.Count < 8) invalid.Add(new { flattenedIndex = index, value = F(v), bits = BitConverter.SingleToInt32Bits(v) }); }
            index++;
        }
        return new { shape = tensor.Dimensions.ToArray(), elements = index, finite, nonfinite = index - finite, min = F(min), max = F(max), outsideZeroToOne, firstOutsideZeroToOne = invalid, rangeMeaning = probabilityOutput ? "Probability output must be finite in [0,1]; no clipping or normalization" : "Normalized NCHW input may validly lie outside [0,1]", floatBytesSha256 = Sha(MemoryMarshal.AsBytes(tensor.ToArray().AsSpan()).ToArray()), floatByteOrder = BitConverter.IsLittleEndian ? "little-endian IEEE754" : "big-endian IEEE754" };
    }
}
