using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;

// No gold, reference roster, vocabulary, pattern rules or network access.
if (args.Length != 3) throw new ArgumentException("manifest new-jsonl new-tensor-gzip");
using var manifest = JsonDocument.Parse(File.ReadAllBytes(args[0])); var m = manifest.RootElement;
var secondName = m.TryGetProperty("secondName", out var secondLabel) ? secondLabel.GetString()! : "v2";
var meanName = "mean-v1-" + secondName;
var root = Path.GetDirectoryName(Path.GetFullPath(args[0]))!;
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(30)); var token = timeout.Token;
OrtEnv.Instance().DisableTelemetryEvents();
using var options = new SessionOptions { IntraOpNumThreads = 1, InterOpNumThreads = 1, GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL, LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
using var first = new InferenceSession(Verify(m.GetProperty("v1")), options);
using var second = new InferenceSession(Verify(m.GetProperty("v2")), options);
using var japanese = new InferenceSession(Verify(m.GetProperty("japanese")), options);
var dictionary = JsonSerializer.Deserialize<string[]>(File.ReadAllBytes(Verify(m.GetProperty("dictionary"))))!;
var jpDictionary = JsonSerializer.Deserialize<string[]>(File.ReadAllBytes(Verify(m.GetProperty("japaneseDictionary"))))!;
using var actual = new OnnxAsciiOcr(Verify(m.GetProperty("v1")), Verify(m.GetProperty("dictionary")));
var auxiliary = new List<(string Name, InferenceSession Session, string[] Dictionary)>();
if (m.TryGetProperty("auxiliary", out var auxiliaryAssets))
    foreach (var entry in auxiliaryAssets.EnumerateArray())
        auxiliary.Add((entry.GetProperty("name").GetString()!, new InferenceSession(Verify(entry.GetProperty("model")), options), JsonSerializer.Deserialize<string[]>(File.ReadAllBytes(Verify(entry.GetProperty("dictionary"))))!));
using var writer = new StreamWriter(new FileStream(args[1], FileMode.CreateNew, FileAccess.Write));
using var compressed = new GZipStream(new FileStream(args[2], FileMode.CreateNew, FileAccess.Write), CompressionLevel.SmallestSize);
using var tensors = new BinaryWriter(compressed); tensors.Write("TKCTC1");
foreach (var row in m.GetProperty("images").EnumerateArray())
{
    token.ThrowIfCancellationRequested(); var id = row.GetProperty("id").GetString()!;
    var raster = new RecoveryRaster(row.GetProperty("width").GetInt32(), row.GetProperty("height").GetInt32(), File.ReadAllBytes(Verify(row.GetProperty("pixels"))));
    if (!raster.Valid) throw new InvalidDataException("Raster shape mismatch.");
    var watch = Stopwatch.StartNew(); var line = 0;
    var crop = row.TryGetProperty("crop", out var box) ? box.Deserialize<RecoveryBox>()! : new(0, 0, raster.Width, raster.Height);
    var primaryInput = OcrInputTransform.Recognition(raster, crop, token);
    OcrRecognitionPiece[] primary; int times;
    if (row.TryGetProperty("primary", out var observed))
    {
        primary = observed.GetProperty("Pieces").Deserialize<OcrRecognitionPiece[]>()!;
        times = observed.GetProperty("TimeCount").GetInt32();
        if (primaryInput.ValidWidth != observed.GetProperty("ValidWidth").GetInt32() || primaryInput.InputWidth != observed.GetProperty("InputWidth").GetInt32()) throw new InvalidDataException("Frozen native input changed.");
    }
    else
    {
        using var result = japanese.Run([NamedOnnxValue.CreateFromTensor("x", primaryInput.Tensor)]);
        var tensor = result.First().AsTensor<float>(); times = tensor.Dimensions[1]; primary = OcrCtcDecoder.Decode(tensor, jpDictionary, token).ToArray();
    }
    var primaryGlyphs = new List<PdfGlyph>(); var primarySupported = true;
    foreach (var p in primary)
        try { primaryGlyphs.Add(primaryInput.SourceGlyph(p.Text, p.Start, p.End, times, line, primaryGlyphs.Count)); }
        catch (InvalidDataException) { primarySupported = false; }
    if (!primarySupported) { writer.WriteLine(JsonSerializer.Serialize(new { id, primarySupported = false })); continue; }
    var rules = row.TryGetProperty("rules", out var lines) ? lines.Deserialize<PdfRule[]>()! : [];
    var baseline = OcrAsciiRefinement.Refine(raster, primaryGlyphs, primary.Select(p => (double)p.Confidence).ToArray(), new Dictionary<int, RecoveryBox> { [line] = crop }, rules, actual, token, retainUncertain: true);
    var original = string.Concat(primary.Select(p => p.Text)); var baselineText = string.Concat(baseline.Glyphs.Select(g => g.Text));
    var lastJapanese = Array.FindLastIndex(primary, p => p.Text.Any(c => c > 127)) + 1;
    var tailText = string.Concat(primary.Skip(lastJapanese).Select(p => p.Text));
    var right = rules.Where(l => l.Vertical && l.X1 > 2).Select(l => l.X1).DefaultIfEmpty(double.NaN).Min();
    var top = rules.Where(l => l.Vertical && l.X1 > 2).Select(l => Math.Min(l.Y1, l.Y2)).DefaultIfEmpty(double.NaN).Min();
    var header = double.IsFinite(right) && double.IsFinite(top) && crop.X + crop.Width < right && crop.Y >= top && original.Length > 0 && original.All(c => c <= 127);
    bool Code(string text) => text.Length is >= 2 and <= 32 && text.Any(char.IsAsciiDigit) && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
    var standalone = !header && lastJapanese == 0 && Code(original);
    RecoveryBox? tailCrop = null;
    if (!header && lastJapanese > 0 && tailText.Length is >= 2 and <= 32 && tailText.All(c => c is >= ' ' and <= '~'))
        tailCrop = OcrWhiteGapPartition.Tail(raster, crop, primaryGlyphs.Take(lastJapanese).Max(g => g.X + g.Width), primaryGlyphs.Skip(lastJapanese).Min(g => g.X), token);
    var code = standalone ? original : tailText;
    var eligible = (standalone || tailCrop is not null) && Code(code) && !ClassSelection.Candidates.Contains(code) && (!double.IsFinite(top) || crop.Y >= top);
    var variants = new List<object>();
    if (eligible)
    {
        var activeCrop = standalone ? crop : tailCrop!;
        var emptyRuleMask = new bool[raster.Width * raster.Height];
        var cropPolicies = m.TryGetProperty("cropPolicies", out var declaredCrops) ? declaredCrops.EnumerateArray().Select(v => v.GetString()!).ToArray() : new[] { "original", "ink2" };
        foreach (var cropPolicy in cropPolicies)
        {
        if (cropPolicy is not ("original" or "ink0" or "ink2" or "ink4")) throw new InvalidDataException("Unregistered framing.");
        var recognitionCrop = cropPolicy == "original" ? activeCrop : new OcrInkCropFrame(raster, emptyRuleMask, token, int.Parse(cropPolicy[3..], System.Globalization.CultureInfo.InvariantCulture)).Restrict(activeCrop);
        var input = OcrInputTransform.Recognition(raster, recognitionCrop, token);
        if (auxiliary.Count > 0)
        {
            foreach (var aux in auxiliary)
            {
                using var result = aux.Session.Run([NamedOnnxValue.CreateFromTensor("x", input.Tensor)]);
                var probability = result.First().AsTensor<float>();
                var pieces = OcrCtcDecoder.Decode(probability, aux.Dictionary, token).ToArray();
                var glyphs = new List<PdfGlyph>(); var support = true;
                foreach (var piece in pieces)
                    try { glyphs.Add(input.SourceGlyph(piece.Text, piece.Start, piece.End, probability.Dimensions[1], line, glyphs.Count)); }
                    catch (InvalidDataException) { support = false; }
                var text = string.Concat(pieces.Select(p => p.Text));
                foreach (var gate in new[] { "native-floor", "manual-retain" })
                foreach (var lengthPolicy in new[] { "same-length", "bounded-code" })
                {
                    var selected = pieces.Length > 0 && support && Code(text) && (lengthPolicy == "bounded-code" || text.Length == code.Length) && (gate == "manual-retain" || pieces.All(p => p.Confidence >= .8));
                    var prefix = standalone ? "" : string.Concat(primary.Take(lastJapanese).Select(p => p.Text));
                    variants.Add(new { name = aux.Name + "/" + gate, decoder = "greedy", lengthPolicy, cropPolicy, recognitionCrop, rawText = text, text = selected ? prefix + text : baselineText, selected, reject = (float?)null, support, pieces, glyphs, input.ValidWidth, input.InputWidth });
                }
            }
            continue;
        }
        var (a, ar) = Run(first, input, id, "v1/" + cropPolicy); var (b, br) = Run(second, input, id, secondName + "/" + cropPolicy);
        if (!a.Dimensions.SequenceEqual(b.Dimensions)) throw new InvalidDataException("Ensemble frame mismatch.");
        var mean = new DenseTensor<float>(a.Dimensions.ToArray());
        for (var t = 0; t < a.Dimensions[1]; t++) for (var c = 0; c < a.Dimensions[2]; c++) mean[0, t, c] = (a[0, t, c] + b[0, t, c]) * .5f;
        foreach (var (name, tensor, reject) in new[] { ("v1", a, ar), (secondName, b, br), (meanName, mean, Math.Max(ar, br)) })
        foreach (var decoder in new[] { "greedy", "beam8" })
        {
            var pieces = decoder == "greedy" ? OcrCtcDecoder.Decode(tensor, dictionary, token).ToArray() : StudyCtcBeam.Decode(tensor, dictionary, token);
            var support = true; var glyphs = new List<PdfGlyph>();
            foreach (var p in pieces)
                try { glyphs.Add(input.SourceGlyph(p.Text, p.Start, p.End, tensor.Dimensions[1], line, glyphs.Count)); }
                catch (InvalidDataException) { support = false; }
            var text = string.Concat(pieces.Select(p => p.Text));
            foreach (var lengthPolicy in new[] { "same-length", "bounded-code" })
            {
                // Both policies are registered before the first worker run.
                // Unsupported output retains the actual current v1 route.
                var selected = pieces.Length > 0 && reject <= .05f && support && (lengthPolicy == "bounded-code" || text.Length == code.Length) && Code(text);
                var prefix = standalone ? "" : string.Concat(primary.Take(lastJapanese).Select(p => p.Text));
                var final = selected ? prefix + text : baselineText;
                if (cropPolicy == "original" && name == "v1" && decoder == "greedy" && lengthPolicy == "same-length" && final != baselineText) throw new InvalidDataException("Actual manual-route baseline mismatch.");
                variants.Add(new { name, decoder, lengthPolicy, cropPolicy, recognitionCrop, rawText = text, text = final, selected, reject, support, pieces, glyphs, input.ValidWidth, input.InputWidth });
            }
        }
        }
    }
    writer.WriteLine(JsonSerializer.Serialize(new { id, original, baselineText, eligible, standalone, prefixCount = lastJapanese, crop, tailCrop, variants, milliseconds = watch.Elapsed.TotalMilliseconds, peakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64 })); writer.Flush();
    CryptographicOperations.ZeroMemory(raster.Bgra);
}
foreach (var aux in auxiliary) aux.Session.Dispose();
(DenseTensor<float>, float) Run(InferenceSession session, OcrInputTransform.RecognitionInput input, string id, string model)
{
    using var result = session.Run([NamedOnnxValue.CreateFromTensor("x", input.Tensor), NamedOnnxValue.CreateFromTensor("valid_width", new DenseTensor<long>(new long[] { input.ValidWidth }, new[] { 1 }))]);
    var p = result.Single(v => v.Name == "probabilities").AsTensor<float>(); var reject = result.Single(v => v.Name == "reject_probability").AsTensor<float>()[0];
    _ = OcrCtcDecoder.Decode(p, dictionary, token); // validate every input probability before any ensemble/beam
    if (!float.IsFinite(reject) || reject is < 0 or > 1) throw new InvalidDataException("Invalid reject probability.");
    tensors.Write(id); tensors.Write(model); tensors.Write(p.Dimensions[1]); tensors.Write(p.Dimensions[2]);
    var copy = new DenseTensor<float>(p.Dimensions.ToArray());
    for (var t = 0; t < p.Dimensions[1]; t++) for (var c = 0; c < p.Dimensions[2]; c++) { var value = p[0, t, c]; copy[0, t, c] = value; tensors.Write(value); }
    return (copy, reject);
}
string Verify(JsonElement asset)
{
    var name = asset.GetProperty("file").GetString()!;
    if (Path.IsPathRooted(name) || name.Split('/', '\\').Contains("..")) throw new InvalidDataException("Unowned asset path.");
    var path = Path.Combine(root, name); var bytes = File.ReadAllBytes(path);
    if (bytes.Length != asset.GetProperty("bytes").GetInt64() || Convert.ToHexStringLower(SHA256.HashData(bytes)) != asset.GetProperty("sha256").GetString()) throw new InvalidDataException("Frozen asset changed.");
    return path;
}
