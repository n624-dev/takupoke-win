using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Takupoke.Infrastructure.Recovery;

if (args.Length != 2) throw new ArgumentException("manifest new-raw-jsonl");
var root = Path.GetDirectoryName(Path.GetFullPath(args[0]))!;
using var json = JsonDocument.Parse(File.ReadAllBytes(args[0])); var m = json.RootElement;
using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(20)); var token = stop.Token;
OnnxJapaneseOcr Open(bool candidate) => new(Verify(m.GetProperty("detector")), Verify(m.GetProperty("recognizer")), Verify(m.GetProperty("japaneseDictionary")),
    18710, Verify(m.GetProperty("v1")), Verify(m.GetProperty("dictionary")), frameOriginalInk: true,
    manualCodeModel: Verify(m.GetProperty("v3")), retryUnpaddedInput: true, allowContainedRuleContact: true,
    retainVerifiedWhitespace: true, retryRoleLabels: true, manualCodeRequiresLetter: true, retryLiteralCodes: candidate,
    uncertainJapaneseModel: candidate ? Verify(m.GetProperty("japaneseAlternative")) : null,
    uncertainJapaneseDictionary: candidate ? Verify(m.GetProperty("japaneseAlternativeDictionary")) : null);
using var baseline = Open(false); using var candidate = Open(true);
using var writer = new StreamWriter(new FileStream(args[1], FileMode.CreateNew, FileAccess.Write));
var watch = Stopwatch.StartNew(); var count = 0;
foreach (var row in m.GetProperty("rows").EnumerateArray())
{
    var bytes = File.ReadAllBytes(Verify(row.GetProperty("pixels")));
    var raster = new RecoveryRaster(row.GetProperty("width").GetInt32(), row.GetProperty("height").GetInt32(), bytes);
    if (!raster.Valid) throw new InvalidDataException("Original raster invalid.");
    foreach (var (name, runtime) in new[] { ("baseline", baseline), ("candidate", candidate) })
    {
        var observations = new List<OcrRecognitionObservation>(); runtime.RecognitionObserver = o => observations.Add(o);
        object? glyphs = null; object? confidences = null; string? failure = null;
        try { glyphs = runtime.ReadForManualCapture(raster, token); confidences = runtime.NativeConfidences; }
        catch (InvalidDataException e) { failure = e.Message; }
        writer.WriteLine(JsonSerializer.Serialize(new { id = row.GetProperty("id").GetString(), arm = name, failure, glyphs, confidences,
            observations, recognizedBoxes = runtime.RecognizedBoxes, asciiCalls = runtime.AsciiRecognitionCalls,
            scope = "Consumed-font development components, complete original pixel/detection/recognition only; no full-document or independent qualification" }));
        writer.Flush();
    }
    CryptographicOperations.ZeroMemory(bytes); count++;
    if (count % 32 == 0) Console.WriteLine(JsonSerializer.Serialize(new { closed = count, seconds = watch.Elapsed.TotalSeconds }));
}
Console.WriteLine(JsonSerializer.Serialize(new { closed = count, seconds = watch.Elapsed.TotalSeconds, peakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64, oracleReadByWorker = false }));
string Verify(JsonElement asset)
{
    var name = asset.GetProperty("file").GetString()!;
    if (Path.IsPathRooted(name) || name.Split('/', '\\').Contains("..")) throw new InvalidDataException("Owned relative asset required.");
    var path = Path.Combine(root, name); var bytes = File.ReadAllBytes(path);
    if (bytes.Length != asset.GetProperty("bytes").GetInt64() || Convert.ToHexStringLower(SHA256.HashData(bytes)) != asset.GetProperty("sha256").GetString()) throw new InvalidDataException("Input bytes changed.");
    return path;
}
