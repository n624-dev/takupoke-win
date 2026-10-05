using System.Security.Cryptography;
using System.Text.Json;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

// One actual independently generated PDF page, one unchanged product render.
// This helper neither invokes a recognizer nor accepts any oracle/role data.
if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual Windows PDF renderer required");
if (args.Length != 4) throw new ArgumentException("Expected PDF path, SHA256, case ID, empty owned output directory");
var file = new FileInfo(args[0]);
if (file.Length is < 1 or > 50 * 1024 * 1024) throw new InvalidDataException("PDF capacity guard");
if (args[1].Length != 64 || args[1].Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("SHA256 shape");
if (string.IsNullOrWhiteSpace(args[2]) || args[2].Length > 128 || args[2].Any(char.IsControl)) throw new InvalidDataException("Case ID shape");
var directory = new DirectoryInfo(args[3]);
if (!directory.Exists || directory.EnumerateFileSystemInfos().Any()) throw new InvalidDataException("Empty owned output directory required");
var bytes = File.ReadAllBytes(file.FullName);
var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
if (!string.Equals(hash, args[1], StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("PDF byte pin mismatch");
using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(3));
var token = lifetime.Token;
using var input = new InMemoryRandomAccessStream();
using (var writer = new DataWriter(input)) { writer.WriteBytes(bytes); await writer.StoreAsync().AsTask(token); writer.DetachStream(); }
input.Seek(0);
var pdf = await PdfDocument.LoadFromStreamAsync(input).AsTask(token);
if (pdf.PageCount != 1) throw new InvalidDataException("Finite endpoint recipe requires exactly one PDF page");
using var page = pdf.GetPage(0);
using var image = new InMemoryRandomAccessStream();
// Exact WindowsPdfRecovery dimensions, format and color handling.
var width = (uint)Math.Clamp(Math.Round(page.Size.Width * 2), 640, 2400);
var height = (uint)Math.Round(page.Size.Height / page.Size.Width * width);
if (height > 3200) { width = (uint)Math.Round(width * 3200d / height); height = 3200; }
await page.RenderToStreamAsync(image, new PdfPageRenderOptions { DestinationWidth = width, DestinationHeight = height }).AsTask(token);
image.Seek(0);
var decoder = await BitmapDecoder.CreateAsync(image).AsTask(token);
using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore).AsTask(token);
var pixelData = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(token);
var bgra = pixelData.DetachPixelData();
try
{
    if (bitmap.PixelWidth != width || bitmap.PixelHeight != height || bgra.Length != checked(bitmap.PixelWidth * bitmap.PixelHeight * 4)) throw new InvalidDataException("Actual rendered pixel shape mismatch");
    var bgraHash = Convert.ToHexStringLower(SHA256.HashData(bgra));
    if (image.Size is < 1 or > 64 * 1024 * 1024) throw new InvalidDataException("Encoded render capacity guard");
    using var reader = new DataReader(image.GetInputStreamAt(0));
    var loaded = await reader.LoadAsync((uint)image.Size).AsTask(token);
    if (loaded != image.Size) throw new InvalidDataException("Incomplete actual rendered image bytes");
    var encoded = new byte[loaded]; reader.ReadBytes(encoded);
    // CreateNew protects any existing evidence; case ID never forms a path.
    await using (var output = new FileStream(Path.Combine(directory.FullName, "page1.bgra"), FileMode.CreateNew)) await output.WriteAsync(bgra, token);
    await using (var output = new FileStream(Path.Combine(directory.FullName, "page1-rendered-image.bin"), FileMode.CreateNew)) await output.WriteAsync(encoded, token);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        scope = "Actual Windows PDF renderer provenance only; no OCR/Builder/adoption",
        caseId = args[2], actualPdfSha256 = hash, actualPdfBytes = bytes.Length, page = 1,
        nativePageWidth = page.Size.Width, nativePageHeight = page.Size.Height,
        width = bitmap.PixelWidth, height = bitmap.PixelHeight, pixelFormat = "BGRA8", alphaMode = "Ignore", colorManagement = "DoNotColorManage",
        bgraBytes = bgra.Length, bgraSha256 = bgraHash,
        renderedEncodedBytes = encoded.Length, renderedEncodedSha256 = Convert.ToHexStringLower(SHA256.HashData(encoded)),
        decoderId = decoder.DecoderInformation.CodecId, runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        operatingSystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        renderAttempts = 1, nativeOCRCalls = 0, expectedValuesLoaded = false
    }, new JsonSerializerOptions { WriteIndented = true }));
}
finally { CryptographicOperations.ZeroMemory(bgra); }
