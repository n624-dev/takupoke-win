using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Text.Json;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

// Research only: public fictional original pixels, no literal oracle or adoption.
Console.OutputEncoding = new System.Text.UTF8Encoding(false);
if (!OperatingSystem.IsWindows() || args.Length != 2)
    throw new ArgumentException("Expected fixed fictional pixel packet and new raw output on Windows.");
const int scale = 3, padding = 32;
var bytes = File.ReadAllBytes(args[0]);
if (Sha(bytes) != "6e54d6bc0cc2213dc51a6e0ff67f3551a736e4e75641ec00f96c7b9de87f14ff")
    throw new InvalidDataException("Fixed original pixel packet changed.");
using var packet = JsonDocument.Parse(bytes);
var rows = packet.RootElement.GetProperty("rows").EnumerateArray().ToArray();
if (rows.Length != 192 || rows.Select(r => r.GetProperty("id").GetString()).Distinct().Count() != 192)
    throw new InvalidDataException("Fixed rows changed.");
var available = OcrEngine.AvailableRecognizerLanguages.Select(l => l.LanguageTag).ToArray();
var selected = new[] { "ja-JP", "en-US" }.Where(tag => OcrEngine.IsLanguageSupported(new Language(tag))).ToArray();
Console.WriteLine(JsonSerializer.Serialize(new { recipe = "windows-system-ocr-integer3-white32-lines-v2", scale, padding,
    available, selected, maxImageDimension = OcrEngine.MaxImageDimension, os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
    runtime = Environment.Version.ToString(), inputSha256 = Sha(bytes), plannedCalls = 192 * selected.Length,
    qualification = false, modelActivation = false, nativeConfidenceAvailable = false }));
using var stream = new FileStream(args[1], FileMode.CreateNew, FileAccess.Write, FileShare.None);
using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
writer.NewLine = "\n";
var calls = 0;
foreach (var language in selected)
{
    var engine = OcrEngine.TryCreateFromLanguage(new Language(language))
        ?? throw new InvalidDataException("Advertised language engine could not be created.");
    foreach (var row in rows)
    {
        var png = Convert.FromBase64String(row.GetProperty("pngBase64").GetString()!);
        if (Sha(png) != row.GetProperty("pngSha256").GetString()) throw new InvalidDataException("PNG changed.");
        using var memory = new InMemoryRandomAccessStream();
        await memory.WriteAsync(png.AsBuffer()); memory.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(memory);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
            new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        if (bitmap.PixelWidth != row.GetProperty("width").GetInt32() || bitmap.PixelHeight != row.GetProperty("height").GetInt32())
            throw new InvalidDataException("Original dimensions changed.");
        var original = new byte[checked(bitmap.PixelWidth * bitmap.PixelHeight * 4)];
        bitmap.CopyToBuffer(original.AsBuffer());
        if (Sha(original) != row.GetProperty("bgraSha256").GetString()) throw new InvalidDataException("Windows original pixels changed.");
        var targetWidth = checked(bitmap.PixelWidth * scale + 2 * padding);
        var targetHeight = checked(bitmap.PixelHeight * scale + 2 * padding);
        if (targetWidth > OcrEngine.MaxImageDimension || targetHeight > OcrEngine.MaxImageDimension)
            throw new InvalidDataException("Fixed enlarged input exceeds native bounds.");
        var enlarged = new byte[checked(targetWidth * targetHeight * 4)]; Array.Fill(enlarged, (byte)255);
        // Each original BGRA pixel appears in a full3x3 block. No interpolation,
        // discarded ink, contrast rewrite or prediction-dependent choice.
        for (var y = 0; y < bitmap.PixelHeight; y++) for (var x = 0; x < bitmap.PixelWidth; x++)
            for (var dy = 0; dy < scale; dy++) for (var dx = 0; dx < scale; dx++)
                original.AsSpan((y * bitmap.PixelWidth + x) * 4, 4).CopyTo(enlarged.AsSpan(
                    ((padding + y * scale + dy) * targetWidth + padding + x * scale + dx) * 4, 4));
        using var input = SoftwareBitmap.CreateCopyFromBuffer(enlarged.AsBuffer(), BitmapPixelFormat.Bgra8,
            targetWidth, targetHeight, BitmapAlphaMode.Ignore);
        var timer = Stopwatch.StartNew(); var result = await engine.RecognizeAsync(input); timer.Stop();
        var lines = result.Lines.Select(l => new { l.Text, words = l.Words.Select(w => new { w.Text,
            box = new { w.BoundingRect.X, w.BoundingRect.Y, w.BoundingRect.Width, w.BoundingRect.Height } }).ToArray() }).ToArray();
        writer.WriteLine(JsonSerializer.Serialize(new { id = row.GetProperty("id").GetString(), language,
            fullText = result.Text, singleLineText = lines.Length == 1 ? lines[0].Text : null,
            lines, result.TextAngle, nativeConfidence = (double?)null, inferenceMilliseconds = timer.Elapsed.TotalMilliseconds,
            width = bitmap.PixelWidth, height = bitmap.PixelHeight, scale, padding, targetWidth, targetHeight,
            nativeInputBgraSha256 = Sha(enlarged), geometry = "Native boxes stay in enlarged input coordinates; source x/y=(native-32)/3; no output clamp." }));
        writer.Flush(); stream.Flush(flushToDisk: true);
        Array.Clear(original); Array.Clear(png); Array.Clear(enlarged);
        if (++calls % 48 == 0) Console.WriteLine(JsonSerializer.Serialize(new { completed = calls }));
    }
}
if (calls != selected.Length * 192) throw new InvalidDataException("Missing calls.");
var process = Process.GetCurrentProcess(); process.Refresh();
Console.WriteLine(JsonSerializer.Serialize(new { completed = calls, available, selected,
    status = selected.Length == 0 ? "no-supported-engine; precision-unassessed" : "closed-component-comparison; unqualified",
    processPeakWorkingSetBytes = process.PeakWorkingSet64, qualification = false,
    limits = "Windows OCR exposes no per-word native confidence. No score invented, no calibration or whole-document qualification." }));
return selected.Length == 0 ? 2 : 0;

static string Sha(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
