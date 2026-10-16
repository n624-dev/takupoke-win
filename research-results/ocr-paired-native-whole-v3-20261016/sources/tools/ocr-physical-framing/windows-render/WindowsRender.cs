using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

if (!OperatingSystem.IsWindows() || args.Length != 1) throw new InvalidOperationException("Native Windows PDF render with one owned manifest required.");
var path = Path.GetFullPath(args[0]); var root = Path.GetDirectoryName(path)!;
var manifest = JsonNode.Parse(File.ReadAllBytes(path))!; var receipts = new List<object>();
using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(5)); var token = lifetime.Token;
foreach (var item in manifest["documents"]!.AsArray())
{
    var asset = item!["sourcePDF"]!; var relative = asset["file"]!.GetValue<string>();
    if (Path.IsPathRooted(relative) || relative.Contains("..")) throw new InvalidDataException("Owned PDF path failed.");
    var bytes = File.ReadAllBytes(Path.Combine(root, relative)); var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
    if (hash != asset["sha256"]!.GetValue<string>() || bytes.Length != asset["bytes"]!.GetValue<int>()) throw new InvalidDataException("Source PDF identity failed.");
    var capture = new RecoveryReadCapture(); string? strictFailure = null;
    try
    {
        var pages = PdfPigLayoutReader.Read(bytes, MaterialKind.Timetable, token, capture);
        _ = PdfScheduleParser.Timetable(pages, token);
    }
    catch (PdfParseException e) { strictFailure = e.Stage; }
    if (strictFailure is null || !RecoveryPolicy.Eligible(MaterialKind.Timetable, strictFailure))
        throw new InvalidDataException("Independent image PDF did not require the eligible raster path.");
    using var stream = new InMemoryRandomAccessStream();
    using (var writer = new DataWriter(stream)) { writer.WriteBytes(bytes); await writer.StoreAsync().AsTask(token); writer.DetachStream(); }
    stream.Seek(0); var pdf = await PdfDocument.LoadFromStreamAsync(stream).AsTask(token);
    if (pdf.PageCount != item["pages"]!.AsArray().Count) throw new InvalidDataException("Native page count differs.");
    var pageReceipts = new List<object>();
    for (uint i = 0; i < pdf.PageCount; i++)
    {
        token.ThrowIfCancellationRequested(); using var page = pdf.GetPage(i); using var image = new InMemoryRandomAccessStream();
        // PdfPage.Size is in 96-DPI DIPs. Match production's 144-DPI raster.
        var width = (uint)Math.Clamp(Math.Round(page.Size.Width * 1.5), 640, 2400);
        var height = (uint)Math.Round(page.Size.Height / page.Size.Width * width);
        if (height > 3200) { width = (uint)Math.Round(width * 3200d / height); height = 3200; }
        await page.RenderToStreamAsync(image, new PdfPageRenderOptions { DestinationWidth = width, DestinationHeight = height,
            BitmapEncoderId = BitmapEncoder.PngEncoderId }).AsTask(token);
        image.Seek(0); var decoder = await BitmapDecoder.CreateAsync(image).AsTask(token);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore).AsTask(token);
        var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(token);
        var pixels = data.DetachPixelData(); var pixelHash = Convert.ToHexStringLower(SHA256.HashData(pixels));
        var original = item["pages"]!.AsArray()[(int)i]!; var oldHash = original["pixels"]!["sha256"]!.GetValue<string>();
        var name = item["id"]!.GetValue<string>() + "-native-p" + (i + 1) + ".bgra";
        File.WriteAllBytes(Path.Combine(root, name), pixels);
        original["width"] = bitmap.PixelWidth; original["height"] = bitmap.PixelHeight;
        original["pixels"] = new JsonObject { ["file"] = name, ["bytes"] = pixels.Length, ["sha256"] = pixelHash };
        pageReceipts.Add(new { page = i + 1, bitmap.PixelWidth, bitmap.PixelHeight, pixelHash, decoderCodecId = decoder.DecoderInformation.CodecId,
            originalLosslessPixelHash = oldHash, nativeEqualsOriginal = pixelHash == oldHash });
    }
    receipts.Add(new { id = item["id"]!.GetValue<string>(), sourcePDFHash = hash, strictFailure,
        capturedComplete = capture.Complete, renderedPages = pageReceipts });
    CryptographicOperations.ZeroMemory(bytes);
}
manifest["renderer"] = "Actual native Windows PDF renderer; lossless PNG; production dimensions and Bgra8/Ignore/DoNotColorManage";
File.WriteAllText(Path.Combine(root, "native-inputs.json"), manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
File.WriteAllText(Path.Combine(root, "native-render-receipt.json"), JsonSerializer.Serialize(new { receipts, modelCalls = 0 }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { renderedDocuments = receipts.Count, modelCalls = 0, scope = "Actual native acquisition only; recognition and qualification remain separate" }));
