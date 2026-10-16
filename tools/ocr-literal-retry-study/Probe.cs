using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;

if (args.Length != 2) throw new ArgumentException("manifest new-output-directory");
var root = Path.GetDirectoryName(Path.GetFullPath(args[0]))!; var output = Path.GetFullPath(args[1]);
if (Directory.Exists(output)) throw new IOException("Output exists."); Directory.CreateDirectory(output);
using var manifest = JsonDocument.Parse(File.ReadAllBytes(args[0])); var m = manifest.RootElement;
using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(20)); var token = stop.Token;
using var v1 = new OnnxAsciiOcr(Verify(m.GetProperty("v1")), Verify(m.GetProperty("dictionary")));
using var v3 = new OnnxAsciiOcr(Verify(m.GetProperty("v3")), Verify(m.GetProperty("dictionary")));
using var japanese = new OnnxJapaneseAlternative(Verify(m.GetProperty("japaneseAlternative")), Verify(m.GetProperty("japaneseAlternativeDictionary")));
var writers = new Dictionary<string, StreamWriter>();
foreach (var name in new[] { "baseline", "actual-literal-retry", "actual-combined-retry" })
    writers.Add(name, new(new FileStream(Path.Combine(output, name + "-acquisition.jsonl"), FileMode.CreateNew, FileAccess.Write)));
var watch = Stopwatch.StartNew(); var parity = new List<object>(); var pageCount = 0; var actualCalls = 0;
try
{
foreach (var page in m.GetProperty("pages").EnumerateArray())
{
    var raster = new RecoveryRaster(page.GetProperty("width").GetInt32(), page.GetProperty("height").GetInt32(), File.ReadAllBytes(Verify(page.GetProperty("pixels"))));
    using var frozen = JsonDocument.Parse(File.ReadAllBytes(Verify(page.GetProperty("closedPage")))); var p = frozen.RootElement;
    var observations = p.GetProperty("primaryObservations").Deserialize<OcrRecognitionObservation[]>()!;
    var expected = p.GetProperty("glyphs").Deserialize<PdfGlyph[]>()!;
    var rules = p.GetProperty("rules").Deserialize<PdfRule[]>()!;
    var boxes = p.GetProperty("recognizedBoxes").Deserialize<RecoveryBox[]>()!;
    var primary = new List<PdfGlyph>(); var scores = new List<double>(); var crops = new Dictionary<int, RecoveryBox>();
    var alternatePrimary = new List<PdfGlyph>(); var alternateScores = new List<double>();
    var combinedObservations = new List<OcrRecognitionObservation>();
    var line = -1; var lineObservations = new List<OcrRecognitionObservation>();
    void Finish()
    {
        if (lineObservations.Count == 0) return;
        var original = lineObservations.Last(o => o.Purpose is "line" or "unpadded-line");
        var input = OcrInputTransform.Recognition(raster, original.RecognitionCrop, token);
        if (original.Purpose == "unpadded-line") input = input.WithoutRightPadding(token)!;
        if (input is null || input.ValidWidth != original.ValidWidth || input.InputWidth != original.InputWidth) throw new InvalidDataException("Frozen native tensor shape differs.");
        var glyphs = original.Pieces.Select((piece, n) => input.SourceGlyph(piece.Text, piece.Start, piece.End, original.TimeCount, line, n)).ToArray();
        var confidence = original.Pieces.Select(piece => (double)piece.Confidence).ToArray();
        foreach (var retry in lineObservations.Where(o => o.Purpose == "inline-role-label"))
        {
            var rinput = OcrInputTransform.Recognition(raster, retry.RecognitionCrop, token);
            if (!OcrRoleLabelRetry.Matches(original.Pieces, retry.Pieces) || retry.Pieces.Any(piece => (long)piece.End * rinput.InputWidth > (long)rinput.ValidWidth * retry.TimeCount)) continue;
            for (var n = 0; n < retry.Pieces.Count; n++)
            {
                var piece = retry.Pieces[n]; glyphs[n] = rinput.SourceGlyph(piece.Text, piece.Start, piece.End, retry.TimeCount, line, n); confidence[n] = piece.Confidence;
            }
        }
        combinedObservations.AddRange(lineObservations);
        OnnxJapaneseAlternative.Observation? alternate = null; IReadOnlyList<int> replace = [];
        if (OcrConditionalJapaneseRetry.Eligible(original.Pieces))
        {
            alternate = japanese.Read(input, line, token); actualCalls++;
            combinedObservations.Add(new(lineObservations[0].DetectedCrop, input.Crop, input.ValidWidth, input.InputWidth,
                alternate.TimeCount, alternate.Pieces) { Purpose = "uncertain-japanese-v3" });
            replace = OcrConditionalJapaneseRetry.Replacements(original.Pieces, alternate.Pieces, alternate.PaddingSupported);
        }
        crops.Add(line, input.Crop);
        for (var n = 0; n < glyphs.Length; n++)
        {
            primary.Add(glyphs[n] with { SourceOrder = primary.Count }); scores.Add(confidence[n]);
            alternatePrimary.Add((replace.Contains(n) ? alternate!.Glyphs[n] : glyphs[n]) with { SourceOrder = alternatePrimary.Count });
            alternateScores.Add(replace.Contains(n) ? alternate!.Pieces[n].Confidence : confidence[n]);
        }
    }
    foreach (var o in observations)
    {
        if (o.Purpose == "line") { Finish(); lineObservations.Clear(); line++; }
        lineObservations.Add(o);
    }
    Finish();
    var baseline = OcrAsciiRefinement.Refine(raster, primary, scores, crops, rules, v1, token,
        retainUncertain: true, manualCodeAscii: v3, manualCodeRequiresLetter: true);
    var equal = expected.Select(g => (g.Text, g.SourceLine)).SequenceEqual(baseline.Glyphs.Select(g => (g.Text, g.SourceLine)));
    parity.Add(new { id = page.GetProperty("id").GetString(), page = page.GetProperty("page").GetInt32(), sameOriginalNativePixels = true, baselineLiteralParity = equal,
        oldGlyphs = expected.Length, newGlyphs = baseline.Glyphs.Count });
    if (!equal) throw new InvalidDataException("Actual current manual-route baseline differs from closed original output.");
    void Write(string arm, OcrAsciiRefinement.Result result)
    {
        writers[arm].WriteLine(JsonSerializer.Serialize(new { id = page.GetProperty("id").GetString(), page = page.GetProperty("page").GetInt32(),
            sourceSha256 = page.GetProperty("pixels").GetProperty("sha256").GetString(), failure = (string?)null,
            glyphs = result.Glyphs, confidences = result.Confidences, rules,
            observations = arm == "actual-combined-retry" ? combinedObservations.ToArray() : observations, recognizedBoxes = boxes,
            manualCodeExpert = true, asciiRecognizerCalls = result.RecognizerCalls, scope = "Consumed exact native pixels and primary observations; actual current baseline and actual supplementary ASCII inference, no oracle or fresh accuracy credit." }));
        writers[arm].Flush();
    }
    Write("baseline", baseline);
    var literal = OcrAsciiRefinement.Refine(raster, primary, scores, crops, rules, v1, token,
        retainUncertain: true, manualCodeAscii: v3, manualCodeRequiresLetter: true, retryLiteralCodes: true);
    var combined = OcrAsciiRefinement.Refine(raster, alternatePrimary, alternateScores, crops, rules, v1, token,
        retainUncertain: true, manualCodeAscii: v3, manualCodeRequiresLetter: true, retryLiteralCodes: true);
    actualCalls += baseline.RecognizerCalls + literal.RecognizerCalls + combined.RecognizerCalls;
    Write("actual-literal-retry", literal); Write("actual-combined-retry", combined);
    pageCount++;
    Console.WriteLine(JsonSerializer.Serialize(new { pages = pageCount, actualSupplementaryCalls = actualCalls, seconds = watch.Elapsed.TotalSeconds }));
    CryptographicOperations.ZeroMemory(raster.Bgra);
}
}
finally
{
    foreach (var writer in writers.Values) writer.Dispose();
    File.WriteAllText(Path.Combine(output, "baseline-parity.json"), JsonSerializer.Serialize(parity));
}
File.WriteAllText(Path.Combine(output, "execution-receipt.json"), JsonSerializer.Serialize(new { pages = pageCount, actualSupplementaryCalls = actualCalls,
    seconds = watch.Elapsed.TotalSeconds, peakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64, source = "actual CPU ONNX on consumed native originals", oracleReadByWorker = false }));
string Verify(JsonElement asset)
{
    var name = asset.GetProperty("file").GetString()!;
    if (Path.IsPathRooted(name) || name.Split('/', '\\').Contains("..")) throw new InvalidDataException("Owned relative path required.");
    var path = Path.Combine(root, name); var bytes = File.ReadAllBytes(path);
    if (bytes.Length != asset.GetProperty("bytes").GetInt64() || Convert.ToHexStringLower(SHA256.HashData(bytes)) != asset.GetProperty("sha256").GetString()) throw new InvalidDataException("Frozen asset changed.");
    return path;
}
