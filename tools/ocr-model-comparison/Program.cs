using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Takupoke.Infrastructure.Recovery;

// Receives ordinal pixels and pinned model artifacts only. The independent
// literal oracle is read by the Python scorer after this process exits.
if (args.Length != 2) throw new ArgumentException("Expected owned input manifest and output path.");
Environment.SetEnvironmentVariable("ORT_TELEMETRY_DISABLED", "1");
OrtEnv.Instance().DisableTelemetryEvents();
var root = Path.GetDirectoryName(Path.GetFullPath(args[0]))!;
using var manifest = JsonDocument.Parse(File.ReadAllBytes(args[0]));
if (manifest.RootElement.GetProperty("recipe").GetString() != "independent-ocr-model-color-v1") throw new InvalidDataException("Recipe mismatch.");
using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(12));
using var output = new FileStream(args[1], FileMode.CreateNew, FileAccess.Write, FileShare.None);
using var writer = new StreamWriter(output);
var rows = manifest.RootElement.GetProperty("rows").EnumerateArray().ToArray();
if (rows.Length != 192) throw new InvalidDataException("Frozen row count changed.");
var models = manifest.RootElement.GetProperty("models").EnumerateArray().ToArray();
if (models.Length is < 1 or > 4) throw new InvalidDataException("Bounded model count changed.");
var expectedCalls = rows.Length * models.Length * 2;
var completed = 0;
var nativeIdentities = new HashSet<string>(StringComparer.Ordinal);
foreach (var model in models)
{
    var name = model.GetProperty("id").GetString()!;
    var modelPath = Verified(model.GetProperty("file"));
    var dictionaryPath = Verified(model.GetProperty("dictionary"));
    var dictionary = JsonSerializer.Deserialize<string[]>(File.ReadAllBytes(dictionaryPath))!;
    if (dictionary.Length != model.GetProperty("symbols").GetInt32() || dictionary[0] != "" || dictionary[^1] != " ") throw new InvalidDataException("Alphabet mismatch.");
    using var options = new SessionOptions { IntraOpNumThreads = 2, InterOpNumThreads = 1, GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL, LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
    var load = Stopwatch.StartNew();
    using var session = new InferenceSession(modelPath, options);
    load.Stop();
    // Keep the session alive while inspecting its native module. Windows can
    // unload it when the final session is disposed; filename casing also varies.
    var nativeName = OperatingSystem.IsWindows() ? "onnxruntime.dll" : "libonnxruntime.so";
    var nativeModules = Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
        .Where(m => Path.GetFileName(m.FileName).Equals(nativeName, StringComparison.OrdinalIgnoreCase)).ToArray();
    if (nativeModules.Length != 1) throw new InvalidDataException("Cannot uniquely identify the active ONNX runtime before recognition.");
    nativeIdentities.Add(Sha(nativeModules[0].FileName));
    foreach (var row in rows)
    foreach (var condition in new[] { "bgr", "gray" })
    {
        lifetime.Token.ThrowIfCancellationRequested();
        var ordinal = row.GetProperty("id").GetString()!;
        var bytes = File.ReadAllBytes(Verified(row.GetProperty(condition)));
        var width = row.GetProperty("width").GetInt32(); var height = row.GetProperty("height").GetInt32();
        var raster = new RecoveryRaster(width, height, bytes);
        var input = OcrInputTransform.Recognition(raster, new(0, 0, width, height), lifetime.Token);
        var infer = Stopwatch.StartNew();
        using var result = session.Run([NamedOnnxValue.CreateFromTensor("x", input.Tensor)]);
        infer.Stop(); lifetime.Token.ThrowIfCancellationRequested();
        var probabilities = result.Single().AsTensor<float>();
        var pieces = OcrCtcDecoder.Decode(probabilities, dictionary, lifetime.Token);
        var legacy = Legacy(probabilities, dictionary, lifetime.Token);
        if (!pieces.SequenceEqual(legacy)) throw new InvalidDataException("CTC semantics changed.");
        // Same native tensor, three fixed decoding repetitions. Alternate
        // order by ordinal; neither path performs additional inference.
        double previousMs = 0, currentMs = 0;
        for (var repeat = 0; repeat < 3; repeat++)
        {
            if ((completed + repeat) % 2 == 0) { previousMs += TimeLegacy(); currentMs += TimeCurrent(); }
            else { currentMs += TimeCurrent(); previousMs += TimeLegacy(); }
        }
        double TimeLegacy() { var watch = Stopwatch.StartNew(); _ = Legacy(probabilities, dictionary, lifetime.Token); return watch.Elapsed.TotalMilliseconds; }
        double TimeCurrent() { var watch = Stopwatch.StartNew(); _ = OcrCtcDecoder.Decode(probabilities, dictionary, lifetime.Token); return watch.Elapsed.TotalMilliseconds; }
        var unsupportedPadding = false;
        foreach (var piece in pieces)
        {
            try { _ = input.SourceBox(piece.Start, piece.End, probabilities.Dimensions[1]); }
            catch (InvalidDataException) { unsupportedPadding = true; }
        }
        writer.WriteLine(JsonSerializer.Serialize(new { id = ordinal, model = name, condition,
            text = string.Concat(pieces.Select(p => p.Text)), minimumConfidence = pieces.Count == 0 ? (double?)null : pieces.Min(p => p.Confidence),
            lowConfidencePieces = pieces.Count(p => p.Confidence < .8f), unsupportedPadding,
            input.ValidWidth, input.InputWidth, symbols = dictionary.Length, timeSteps = probabilities.Dimensions[1],
            inferenceMilliseconds = infer.Elapsed.TotalMilliseconds, legacyDecodeMilliseconds = previousMs / 3, currentDecodeMilliseconds = currentMs / 3,
            decodeExact = true, pieces, modelLoadMilliseconds = load.Elapsed.TotalMilliseconds,
            runtimeVersion = typeof(InferenceSession).Assembly.GetName().Version?.ToString() }));
        writer.Flush(); output.Flush(flushToDisk: true);
        CryptographicOperations.ZeroMemory(bytes);
        if (++completed % 96 == 0) Console.WriteLine(JsonSerializer.Serialize(new { completed, total = expectedCalls }));
    }
}
writer.Flush(); output.Flush(flushToDisk: true);
if (completed != expectedCalls) throw new InvalidDataException("Missing calls remain unassessed.");
var process = Process.GetCurrentProcess(); process.Refresh();
Console.WriteLine(JsonSerializer.Serialize(new { completed, actualModelCalls = completed, legacyComparisonAdditionalModelCalls = 0,
    processPeakWorkingSetBytes = process.PeakWorkingSet64, os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
    architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), dotnet = Environment.Version.ToString(),
    nativeRuntimeSha256 = nativeIdentities.Single(),
    qualification = "unassessed", detectorCalls = 0, llmCalls = 0, formalAdoptions = 0 }));

string Verified(JsonElement asset)
{
    var relative = asset.GetProperty("path").GetString()!;
    if (Path.IsPathRooted(relative) || relative.Split('/', '\\').Any(p => p is ".." or "." or "")) throw new InvalidDataException("Asset path failed.");
    var path = Path.Combine(root, relative);
    if (new FileInfo(path).Length != asset.GetProperty("bytes").GetInt64() || Sha(path) != asset.GetProperty("sha256").GetString()) throw new InvalidDataException("Asset identity failed.");
    return path;
}
static string Sha(string path) { using var file = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(file)); }

// The former production two-pass algorithm, parameterized only by the pinned
// alphabet's size for this comparison. Never used by the application.
static IReadOnlyList<OcrRecognitionPiece> Legacy(Tensor<float> scores, string[] dictionary, CancellationToken token)
{
    var work = 0;
    foreach (var score in scores)
    {
        if (++work % 4096 == 0) token.ThrowIfCancellationRequested();
        if (!float.IsFinite(score) || score is < 0 or > 1) throw new InvalidDataException("Invalid probability.");
    }
    var output = new List<OcrRecognitionPiece>(); var previous = -1;
    for (var t = 0; t < scores.Dimensions[1]; t++)
    {
        var best = 0; var score = scores[0, t, 0];
        for (var c = 1; c < dictionary.Length; c++) if (scores[0, t, c] > score) { score = scores[0, t, c]; best = c; }
        if (best != 0 && best != previous) output.Add(new(dictionary[best], t, t + 1, score));
        else if (best != 0 && output.Count > 0) { var last = output[^1]; output[^1] = last with { End = t + 1, Confidence = Math.Max(last.Confidence, score) }; }
        previous = best;
    }
    return output;
}
