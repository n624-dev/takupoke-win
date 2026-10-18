using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;

// No reference/oracle input. Baseline invokes deployed factory; candidate adds
// the verified optional manual-code expert with original-v1 fallback.
// Native comparison includes literal role labels when their OCR colon is omitted.
if (args.Length == 2 && args[0] == "--bundle")
{
    File.WriteAllText(args[1], JsonSerializer.Serialize(RecoveryOcrModels.Bundle)); return;
}
if (args.Length != 3 || args[2] is not ("baseline" or "candidate")) throw new ArgumentException("manifest output arm");
var root = Path.GetDirectoryName(Path.GetFullPath(args[0]))!;
using var manifest = JsonDocument.Parse(File.ReadAllBytes(args[0])); var m = manifest.RootElement;
var modelRoot = Path.Combine(root, "models");
if (RecoveryOcrModels.Bundle.Artifacts.Single(a => a.Path == "ascii-rec.onnx").Sha256 != "9dd3ed77db4f2af171cf434d200c666033c9bdfe5f33446bc47dbfecac90d703")
    throw new InvalidDataException("This frozen comparison requires the historical v1 baseline factory; do not silently compare a later default against itself.");
if (!await RecoveryModelBundleStore.VerifyAsync(RecoveryOcrModels.Bundle, modelRoot)) throw new InvalidDataException("Pinned model bundle differs.");
var dictionary = Verify(m.GetProperty("hybridDictionary")); await RecoveryOcrModels.VerifyDictionaryAsync(dictionary, default);
using var ocr = args[2] == "baseline" ? RecoveryOcrModels.OpenRuntime(modelRoot, dictionary)
    : new OnnxJapaneseOcr(Path.Combine(modelRoot, "det.onnx"), Path.Combine(modelRoot, "rec.onnx"), dictionary, 18710, Path.Combine(modelRoot, "ascii-rec.onnx"), Path.Combine(modelRoot, "ascii-dictionary.json"), frameOriginalInk: true, manualCodeModel: Verify(m.GetProperty("candidateRecognizer")), retryUnpaddedInput: true, allowContainedRuleContact: true, retainVerifiedWhitespace: true, retryRoleLabels: true, manualCodeRequiresLetter: true);
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(20)); var token = timeout.Token;
ocr.SmokeTest(token);
using var writer = new StreamWriter(new FileStream(args[1], FileMode.CreateNew, FileAccess.Write));
foreach (var input in m.GetProperty("documents").EnumerateArray())
{
    var watch = Stopwatch.StartNew(); var rasters = new List<RecoveryRaster>(); var pages = new List<PdfPageLayout>();
    var pageReports = new List<object>(); var scores = new Dictionary<string,double>();
    RecoveryDocument? document = null; RecoveryResult? formal = null;
    var stage = "Read"; var outcome = "unassessed"; string? failure = null; var calls = 0; var asciiCalls = 0; var detections = 0;
    try
    {
        foreach (var page in input.GetProperty("pages").EnumerateArray())
        {
            token.ThrowIfCancellationRequested(); var number = page.GetProperty("page").GetInt32();
            var raster = new RecoveryRaster(page.GetProperty("width").GetInt32(), page.GetProperty("height").GetInt32(), File.ReadAllBytes(Verify(page.GetProperty("pixels"))));
            if (!raster.Valid) throw new InvalidDataException("Original pixels invalid."); rasters.Add(raster);
            var observed = new List<OcrRecognitionObservation>(); ocr.RecognitionObserver = observation => { calls++; observed.Add(observation); };
            detections++; var glyphs = ocr.ReadForManualCapture(raster, token); asciiCalls += ocr.AsciiRecognitionCalls;
            var rules = raster.Rules(token);
            if (raster.HasUnrecognizedInk(ocr.RecognizedBoxes, rules, token)) throw new InvalidDataException("Unrecognized original ink.");
            if (glyphs.Count != ocr.NativeConfidences.Count) throw new InvalidDataException("Source score identity differs.");
            for (var n = 0; n < glyphs.Count; n++) scores.Add($"p{number}s{n}", ocr.NativeConfidences[n]);
            pages.Add(new(raster.Width, raster.Height, glyphs, rules));
            pageReports.Add(new { number, raster.Width, raster.Height, complete = true, glyphs, scores = ocr.NativeConfidences.ToArray(), rules,
                primaryObservations = observed.ToArray(), recognizedBoxes = ocr.RecognizedBoxes.ToArray() });
        }
        if (pages.Count != input.GetProperty("pages").GetArrayLength()) throw new InvalidDataException("Pages missing.");
        stage = "Builder"; var scanners = rasters.Select((s,i) => s.InkFreeScanner(s.RuleMask(pages[i].Lines,token),token)).ToArray();
        var hash = input.GetProperty("originalPDFSHA256").GetString()!;
        document = RecoveryDocumentBuilder.Build(hash, MaterialKind.Timetable, pages, (p,b) => scanners[p-1](b), Enumerable.Range(1,pages.Count).ToHashSet(), token, allowStructureProposal: true);
        var sources = document.Sources.Select(s => s with { NativeConfidence = scores[s.Id] }).ToArray();
        document = document with { Sources = sources, Capture = new(hash,pages.Count,RecoveryValidator.Fingerprint(sources),
            rasters.Select((s,i) => new RecoveryCapturedPage(i+1,s.Width,s.Height,Sha(s.Bgra),sources.Count(x => x.Page == i+1),true)
                { RenderProfile = m.TryGetProperty("renderer",out _) ? "windows-pdf-png-v1" : null }).ToArray(),3) };
        var targets = RecoveryManualAssistance.CaptureTargets(document, token);
        if (targets is { Count: > 0 })
        {
            var crops = targets.Select(t => {
                var image = rasters[t.Page-1]; var x = (int)Math.Floor(t.Crop.X); var y = (int)Math.Floor(t.Crop.Y);
                var w = (int)Math.Ceiling(t.Crop.X+t.Crop.Width)-x; var h = (int)Math.Ceiling(t.Crop.Y+t.Crop.Height)-y;
                var pixels = new byte[w*h*4];
                for (var n=0;n<h;n++) Buffer.BlockCopy(image.Bgra,((y+n)*image.Width+x)*4,pixels,n*w*4,w*4);
                return new RecoveryOriginalCrop(t.Target,t.Page,x,y,w,h,document.Capture.Pages[t.Page-1].RasterHash,pixels,Sha(pixels));
            }).ToArray();
            document = document with { Capture = document.Capture with { OriginalCrops = crops } };
        }
        stage = "Engine"; var run = await RecoveryEngine.RunAsync(document,"windows",10,true,[],_ => null,token);
        formal = run.Result;
        if (formal is null) { outcome = RecoveryManualAssistance.Prepare(document,token) is null ? "refused" : "original-review-required"; failure = string.Join(',',run.Errors); }
        else outcome = "validated-preview";
    }
    catch (Exception e) when (e is InvalidDataException or PdfParseException) { outcome = "refused"; failure = e.Message; }
    finally
    {
        writer.WriteLine(JsonSerializer.Serialize(new { id=input.GetProperty("id").GetString(),condition=args[2],stage,outcome,failure,document,formal,pageReports,
            requiredSlots=document?.RequiredSlots.Count,cells=document?.Cells.Count,recognizerCalls=calls,asciiRecognizerCalls=asciiCalls,detectorCalls=detections,
            milliseconds=watch.ElapsedMilliseconds,processPeakWorkingSetBytes=Process.GetCurrentProcess().PeakWorkingSet64,
            manualCaptureCodeRefinement = true,
            manualCodeTailRefinement = true,
            manualCodeExpert = args[2] == "candidate",
            acquisitionRepairs = args[2] == "candidate",
            uncertainManualCodeRequiresLetter = args[2] == "candidate",
            scope="Actual production runtime, unchanged original page pixels; no gold, user input, score raising or source omission. Primary calls include actual label/unpadded retries and exclude specialist/smoke calls." }));
        writer.Flush(); foreach (var raster in rasters) CryptographicOperations.ZeroMemory(raster.Bgra);
    }
}
string Verify(JsonElement asset)
{
    var name = asset.GetProperty("file").GetString()!;
    if (Path.IsPathRooted(name) || name.Contains("..")) throw new InvalidDataException("Owned relative path invalid.");
    var path = Path.Combine(root,name); var bytes = File.ReadAllBytes(path);
    if (bytes.Length != asset.GetProperty("bytes").GetInt64() || Sha(bytes) != asset.GetProperty("sha256").GetString()) throw new InvalidDataException("Asset changed.");
    return path;
}
static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
