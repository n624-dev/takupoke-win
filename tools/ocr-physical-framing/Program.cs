using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Takupoke.Infrastructure.Storage;

if (args.Length == 3 && args[0] == "--replay")
{
    await Replay.Run(args[1], args[2]);
    return;
}
if (args.Length == 2 && args[0] == "--extract-original-images")
{
    OriginalImageRead.Run(args[1]);
    return;
}
var diagnoseOwnership = args.Length == 3 && args[0] == "--diagnose-ownership";
if (diagnoseOwnership) args = args[1..];
if (args.Length != 2) throw new ArgumentException("Expected pixel/model manifest and raw observation path.");
using var manifest = JsonDocument.Parse(File.ReadAllBytes(args[0]));
var root = Path.GetDirectoryName(Path.GetFullPath(args[0]))!;
var m = manifest.RootElement;
if (m.GetProperty("recipe").GetString() is not ("physical-input-framing-consumed-originals-v1" or "physical-input-framing-fresh-development-v1")) throw new InvalidDataException("Recipe mismatch.");
var det = Verified(m.GetProperty("detector")); var rec = Verified(m.GetProperty("recognizer"));
var dictionary = Verified(m.GetProperty("dictionary"));
using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(15));
var token = lifetime.Token; OrtEnv.Instance().DisableTelemetryEvents();
using var baseline = new OnnxJapaneseOcr(det, rec, dictionary);
using var options = new SessionOptions { IntraOpNumThreads = 2, InterOpNumThreads = 1,
    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL, LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
using var detector = new InferenceSession(det, options); using var recognizer = new InferenceSession(rec, options);
var alphabet = JsonSerializer.Deserialize<string[]>(File.ReadAllBytes(dictionary))!;
if (alphabet.Length != 18385) throw new InvalidDataException("Native V5 alphabet required.");
using var writer = new StreamWriter(new FileStream(args[1], FileMode.CreateNew, FileAccess.Write));
foreach (var item in m.GetProperty("documents").EnumerateArray())
foreach (var condition in diagnoseOwnership ? new[] { "ownership-diagnosis" } :
    item.TryGetProperty("plannedConditions", out var planned) ? planned.EnumerateArray().Select(p => p.GetString()!).ToArray() : new[] { "production", "physical-input-frame" })
{
    if (condition is not ("ownership-diagnosis" or "production" or "physical-input-frame" or "ink-input-frame"))
        throw new InvalidDataException("Unplanned recognition condition.");
    var id = item.GetProperty("id").GetString()!; var watch = Stopwatch.StartNew();
    var layouts = new List<PdfPageLayout>(); var rasters = new List<RecoveryRaster>();
    var pageReports = new List<object>(); var confidences = new Dictionary<string, double>();
    RecoveryDocument? document = null; object? formal = null; string stage = "Read", outcome = "unassessed"; string? failure = null;
    var recognizerCalls = 0; var detectorCalls = 0; var lastObservations = new List<object>();
    try
    {
        foreach (var page in item.GetProperty("pages").EnumerateArray())
        {
            var number = page.GetProperty("page").GetInt32();
            var raster = new RecoveryRaster(page.GetProperty("width").GetInt32(), page.GetProperty("height").GetInt32(), File.ReadAllBytes(Verified(page.GetProperty("pixels"))));
            rasters.Add(raster); if (!raster.Valid) throw new InvalidDataException("Raster invalid.");
            var rules = raster.Rules(token); var mask = raster.RuleMask(rules, token);
            IReadOnlyList<PdfGlyph> glyphs; IReadOnlyList<RecoveryBox> supported;
            var observations = new List<object>(); lastObservations = observations; double[] scores;
            if (condition == "production")
            {
                baseline.RecognitionObserver = o => { recognizerCalls++; observations.Add(o); };
                detectorCalls++; glyphs = baseline.ReadForManualCapture(raster, token);
                supported = baseline.RecognizedBoxes; scores = baseline.NativeConfidences.ToArray();
            }
            else
            {
                detectorCalls++; var boxes = Detect(raster, token);
                if (diagnoseOwnership)
                {
                    pageReports.Add(new { number, raster.Width, raster.Height, rules, boxes,
                        ownership = OwnershipInspect.Analyze(raster, boxes, mask, token) });
                    continue;
                }
                var completed = OcrCropCompleteness.Complete(raster, boxes, token);
                var inkFrame = new InkCropFrame(raster, mask, token);
                var framed = completed.Select(b => condition == "ink-input-frame" ? inkFrame.Restrict(b) :
                    PhysicalCropFrame.Restrict(raster, b, rules, mask, token)).ToArray();
                var output = new List<PdfGlyph>(); var nativeScores = new List<double>(); var line = 0; var order = 0;
                foreach (var index in Enumerable.Range(0, framed.Length).OrderBy(i => framed[i].Y).ThenBy(i => framed[i].X))
                {
                    var input = OcrInputTransform.Recognition(raster, framed[index], token);
                    token.ThrowIfCancellationRequested();
                    using var result = recognizer.Run([NamedOnnxValue.CreateFromTensor("x", input.Tensor)]);
                    recognizerCalls++; token.ThrowIfCancellationRequested();
                    var tensor = result.First().AsTensor<float>(); var pieces = OcrCtcDecoder.Decode(tensor, alphabet, token);
                    observations.Add(new { detected = boxes[index], completed = completed[index], framed = framed[index],
                        changed = framed[index] != completed[index], input.ValidWidth, input.InputWidth, timeSteps = tensor.Dimensions[1], pieces });
                    if (pieces.Count == 0 || pieces.Any(p => p.Confidence < .8f && string.IsNullOrWhiteSpace(p.Text)))
                        throw new InvalidDataException("OCRで判読できない文字があります。空欄には置き換えません。");
                    foreach (var piece in pieces)
                    {
                        output.Add(input.SourceGlyph(piece.Text, piece.Start, piece.End, tensor.Dimensions[1], line, order++));
                        nativeScores.Add(piece.Confidence);
                    }
                    line++;
                }
                glyphs = output; scores = nativeScores.ToArray(); supported = framed;
            }
            var complete = !raster.HasUnrecognizedInk(supported, rules, token);
            pageReports.Add(new { number, raster.Width, raster.Height, complete, glyphs, scores, rules, observations });
            if (!complete) throw new InvalidDataException("Unrecognized original ink remains.");
            for (var n = 0; n < glyphs.Count; n++) confidences.Add($"p{number}s{n}", scores[n]);
            layouts.Add(new(raster.Width, raster.Height, glyphs, rules));
        }
        if (diagnoseOwnership) { outcome = "diagnosis-only-unassessed"; continue; }
        stage = "Builder";
        var scanners = rasters.Select((r, i) => r.InkFreeScanner(r.RuleMask(layouts[i].Lines, token), token)).ToArray();
        var hash = item.GetProperty("originalPDFSHA256").GetString()!;
        document = RecoveryDocumentBuilder.Build(hash, MaterialKind.Timetable, layouts,
            (page, box) => scanners[page - 1](box), Enumerable.Range(1, layouts.Count).ToHashSet(), token, allowStructureProposal: true);
        var sources = document.Sources.Select(s => s with { NativeConfidence = confidences[s.Id] }).ToArray();
        document = document with { Sources = sources, Capture = new(hash, layouts.Count, RecoveryValidator.Fingerprint(sources),
            rasters.Select((r, i) => new RecoveryCapturedPage(i + 1, r.Width, r.Height, Sha(r.Bgra), sources.Count(s => s.Page == i + 1), true)).ToArray()) };
        stage = "Engine";
        var run = await RecoveryEngine.RunAsync(document, "windows", 10, true, [], _ => null, token);
        if (run.Result is null) { outcome = "engine-refused"; failure = string.Join(',', run.Errors); }
        else
        {
            stage = "Validator"; var check = RecoveryValidator.Validate(document, run.Result, token);
            if (!check.CanAdopt) { outcome = "validator-refused"; failure = string.Join(',', check.Errors); }
            else
            {
                stage = "Formal";
                var source = new SourceRecord(id, MaterialKind.Timetable, "fictional.pdf", "fictional", "fictional.pdf", hash,
                    1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);
                formal = RecoveryAnalysisConverter.Convert(source, document, run.Result, DateTimeOffset.UnixEpoch, token).Timetable;
                outcome = "formal-returned-unscored";
            }
        }
    }
    catch (InvalidDataException e) { outcome = "pipeline-refused"; failure = e.Message; }
    catch (PdfParseException e) { outcome = "pipeline-refused"; failure = e.Stage; }
    catch (Exception e) { outcome = "execution-error-unassessed"; failure = e.GetType().Name + ":" + e.Message; }
    finally
    {
        writer.WriteLine(JsonSerializer.Serialize(new { id, condition, stage, outcome, failure, pageReports, formal, document,
            requiredSlots = document?.RequiredSlots.Count, cells = document?.Cells.Count,
            lowConfidenceSources = document?.Sources.Count(s => s.NativeConfidence is < .8),
            detectorCalls, recognizerCalls, manualCorrections = 0, llmCalls = 0, milliseconds = watch.ElapsedMilliseconds,
            recognizerCountMeaning = condition == "production" ? "Validated native recognition observations" : "Returned native recognizer calls",
            unfinishedPageObservations = layouts.Count < rasters.Count ? lastObservations : null,
            processPeakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64,
            scope = m.TryGetProperty("sourceScope", out var sourceScope) ? sourceScope.GetString() :
                m.TryGetProperty("renderer", out var renderer) ? renderer.GetString() + "; consumed original drawings, no independent document/qualification credit" : "Consumed full original image pages; Linux, no PDF reader/Windows renderer/new document or qualification credit" }));
        writer.Flush(); foreach (var raster in rasters) CryptographicOperations.ZeroMemory(raster.Bgra);
    }
}

IReadOnlyList<RecoveryBox> Detect(RecoveryRaster image, CancellationToken ct)
{
    var ratio = Math.Min(1, 960d / Math.Max(image.Width, image.Height));
    var dw = Math.Max(32, (int)Math.Round(image.Width * ratio / 32) * 32);
    var dh = Math.Max(32, (int)Math.Round(image.Height * ratio / 32) * 32);
    using var result = detector.Run([NamedOnnxValue.CreateFromTensor("x", OcrInputTransform.Detection(image, new(0, 0, image.Width, image.Height), dw, dh, ct))]);
    ct.ThrowIfCancellationRequested(); var map = result.First().AsTensor<float>(); OcrDetectorProbability.ValidateMapShape(map.Dimensions, dw, dh);
    var probabilities = new float[dw * dh];
    for (var y = 0; y < dh; y++) for (var x = 0; x < dw; x++) probabilities[y * dw + x] = OcrDetectorProbability.Normalize(map[0, 0, y, x]);
    var visited = new bool[dw * dh]; var boxes = new List<RecoveryBox>();
    for (var y = 0; y < dh; y++) for (var x = 0; x < dw; x++)
    {
        if (visited[y * dw + x] || probabilities[y * dw + x] < .3) continue;
        var queue = new Queue<(int X, int Y)>(); queue.Enqueue((x, y)); visited[y * dw + x] = true;
        var left = x; var top = y; var right = x; var bottom = y; double score = 0; var count = 0;
        while (queue.TryDequeue(out var p))
        {
            count++; score += probabilities[p.Y * dw + p.X]; left = Math.Min(left, p.X); right = Math.Max(right, p.X); top = Math.Min(top, p.Y); bottom = Math.Max(bottom, p.Y);
            foreach (var (nx, ny) in new[] { (p.X - 1, p.Y), (p.X + 1, p.Y), (p.X, p.Y - 1), (p.X, p.Y + 1) })
                if (nx >= 0 && ny >= 0 && nx < dw && ny < dh && !visited[ny * dw + nx] && probabilities[ny * dw + nx] >= .3)
                { visited[ny * dw + nx] = true; queue.Enqueue((nx, ny)); }
        }
        ct.ThrowIfCancellationRequested(); if (count < 6 || score / count < .6) continue;
        var margin = Math.Max(1, (bottom - top + 1) * .25); var bx = Math.Max(0, (left - margin) * image.Width / dw); var by = Math.Max(0, (top - margin) * image.Height / dh);
        var ex = Math.Min(image.Width, (right + 1 + margin) * image.Width / dw); var ey = Math.Min(image.Height, (bottom + 1 + margin) * image.Height / dh);
        boxes.Add(new(bx, by, ex - bx, ey - by)); if (boxes.Count > 10000) throw new InvalidDataException("Candidate count exceeded.");
    }
    return boxes;
}
string Verified(JsonElement asset)
{
    var relative = asset.GetProperty("file").GetString()!;
    if (Path.IsPathRooted(relative) || relative.Contains("..")) throw new InvalidDataException("Asset path invalid.");
    var path = Path.Combine(root, relative); var bytes = File.ReadAllBytes(path);
    if (bytes.Length != asset.GetProperty("bytes").GetInt64() || Sha(bytes) != asset.GetProperty("sha256").GetString()) throw new InvalidDataException("Pinned asset changed.");
    return path;
}
static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
