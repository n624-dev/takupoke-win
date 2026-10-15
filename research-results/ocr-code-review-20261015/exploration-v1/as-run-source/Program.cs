using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Recovery;

// Pixel/model manifest only. The oracle is read after this process closes.
if (args.Length != 2) throw new ArgumentException("manifest new-output");
var root = Path.GetDirectoryName(Path.GetFullPath(args[0]))!;
using var input = JsonDocument.Parse(File.ReadAllBytes(args[0]));
if (input.RootElement.GetProperty("recipe").GetString() != "independent-code-images-v1") throw new InvalidDataException("Recipe changed.");
OrtEnv.Instance().DisableTelemetryEvents();
using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(10));
using var writer = new StreamWriter(new FileStream(args[1], FileMode.CreateNew, FileAccess.Write));
foreach (var model in input.RootElement.GetProperty("models").EnumerateArray())
{
    var id = model.GetProperty("id").GetString();
    var dictionary = JsonSerializer.Deserialize<string[]>(File.ReadAllBytes(Verify(model.GetProperty("dictionary"))))!;
    if (dictionary[0] != "" || dictionary[^1] != " ") throw new InvalidDataException("Dictionary contract changed.");
    using var options = new SessionOptions { IntraOpNumThreads = 2, InterOpNumThreads = 1,
        GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL, LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
    using var session = new InferenceSession(Verify(model.GetProperty("model")), options);
    foreach (var row in input.RootElement.GetProperty("rows").EnumerateArray())
    {
        deadline.Token.ThrowIfCancellationRequested();
        var raster = new RecoveryRaster(row.GetProperty("width").GetInt32(), row.GetProperty("height").GetInt32(),
            File.ReadAllBytes(Verify(row.GetProperty("pixels"))));
        var watch = Stopwatch.StartNew();
        var transformed = OcrInputTransform.Recognition(raster, new(0, 0, raster.Width, raster.Height), deadline.Token);
        var feed = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("x", transformed.Tensor) };
        if (session.InputMetadata.ContainsKey("valid_width")) feed.Add(NamedOnnxValue.CreateFromTensor("valid_width",
            new DenseTensor<long>(new long[] { transformed.ValidWidth }, new[] { 1 })));
        using var output = session.Run(feed);
        var probabilities = output.Single(v => v.Name != "reject_probability").AsTensor<float>();
        var pieces = OcrCtcDecoder.Decode(probabilities, dictionary, deadline.Token);
        var supported = true;
        foreach (var p in pieces)
            try { transformed.SourceBox(p.Start, p.End, probabilities.Dimensions[1]); }
            catch (InvalidDataException) { supported = false; }
        var reject = output.SingleOrDefault(v => v.Name == "reject_probability")?.AsTensor<float>()[0];
        writer.WriteLine(JsonSerializer.Serialize(new { id = row.GetProperty("id").GetString(), model = id,
            text = string.Concat(pieces.Select(p => p.Text)), pieces, reject, paddingSupported = supported,
            transformed.ValidWidth, transformed.InputWidth, milliseconds = watch.Elapsed.TotalMilliseconds,
            peakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64 }));
        writer.Flush();
        CryptographicOperations.ZeroMemory(raster.Bgra);
    }
}
string Verify(JsonElement asset)
{
    var name = asset.GetProperty("file").GetString()!;
    if (Path.IsPathRooted(name) || name.Split('/', '\\').Contains("..")) throw new InvalidDataException("Unowned path.");
    var path = Path.Combine(root, name); var bytes = File.ReadAllBytes(path);
    if (bytes.Length != asset.GetProperty("bytes").GetInt64() ||
        Convert.ToHexStringLower(SHA256.HashData(bytes)) != asset.GetProperty("sha256").GetString()) throw new InvalidDataException("Asset changed.");
    return path;
}
