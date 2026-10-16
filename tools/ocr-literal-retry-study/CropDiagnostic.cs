using System.Collections;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;

if (args.Length == 2 && args[0] == "--bundle") { File.WriteAllText(args[1], JsonSerializer.Serialize(RecoveryOcrModels.Bundle)); return; }
if (args.Length != 2) throw new ArgumentException("owned native manifest new diagnostic output");
var root = Path.GetDirectoryName(Path.GetFullPath(args[0]))!;
using var manifest = JsonDocument.Parse(File.ReadAllBytes(args[0])); var m = manifest.RootElement;
using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(10)); var token = stop.Token;
var dictionary = Verify(m.GetProperty("hybridDictionary")); await RecoveryOcrModels.VerifyDictionaryAsync(dictionary, token);
if (!await RecoveryModelBundleStore.VerifyAsync(RecoveryOcrModels.Bundle, Path.Combine(root, "models"), token)) throw new InvalidDataException("Factory model bundle changed.");
using var runtime = RecoveryOcrModels.OpenRuntime(Path.Combine(root, "models"), dictionary);
using var writer = new StreamWriter(new FileStream(args[1], FileMode.CreateNew, FileAccess.Write));
foreach (var doc in m.GetProperty("documents").EnumerateArray())
foreach (var page in doc.GetProperty("pages").EnumerateArray())
{
    var raster = new RecoveryRaster(page.GetProperty("width").GetInt32(), page.GetProperty("height").GetInt32(), File.ReadAllBytes(Verify(page.GetProperty("pixels"))));
    var detections = new List<RecoveryBox>(); var observations = new List<OcrRecognitionObservation>();
    runtime.DetectionObserver = boxes => detections.AddRange(boxes); runtime.RecognitionObserver = o => observations.Add(o);
    IReadOnlyList<PdfGlyph>? glyphs = null; string? failure = null; var details = new Dictionary<string, object?>(); var watch = Stopwatch.StartNew();
    try { glyphs = runtime.ReadForManualCapture(raster, token); }
    catch (InvalidDataException error)
    {
        failure = error.Message;
        foreach (DictionaryEntry entry in error.Data) details[entry.Key.ToString()!] = entry.Value;
    }
    writer.WriteLine(JsonSerializer.Serialize(new { id = doc.GetProperty("id").GetString(), page = page.GetProperty("page").GetInt32(),
        sourceSha256 = page.GetProperty("pixels").GetProperty("sha256").GetString(), failure, details,
        detectedBoxes = detections, recognizedBoxes = runtime.RecognizedBoxes, observations, glyphs, scores = runtime.NativeConfidences,
        milliseconds = watch.ElapsedMilliseconds, scope = "Consumed fictional native input; actual unchanged10446factory diagnostics only, no gold/correction/newqualification" }));
    writer.Flush(); CryptographicOperations.ZeroMemory(raster.Bgra);
}
string Verify(JsonElement asset)
{
    var name = asset.GetProperty("file").GetString()!;
    if (Path.IsPathRooted(name) || name.Split('/', '\\').Contains("..")) throw new InvalidDataException("Owned relative asset required.");
    var path = Path.Combine(root, name); var bytes = File.ReadAllBytes(path);
    if (bytes.Length != asset.GetProperty("bytes").GetInt64() || Convert.ToHexStringLower(SHA256.HashData(bytes)) != asset.GetProperty("sha256").GetString()) throw new InvalidDataException("Frozen bytes changed.");
    return path;
}
