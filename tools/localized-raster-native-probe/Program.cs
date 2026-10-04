using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Takupoke.Infrastructure.Storage;
using Takupoke.Win.Platform;

Environment.SetEnvironmentVariable("ORT_TELEMETRY_DISABLED", "1");
var preflight = args.SequenceEqual(new[] { "--preflight" });
if (!preflight && (args.Length != 0 || !OperatingSystem.IsWindows())) throw new InvalidOperationException("Only fixed native Windows controls or model-free preflight are supported.");
const string recipeHash = "0bdec55548182fb2ed6f12738d0c5e0ea346ec52a21ecbffb4fe1d33d3769fdc";
var recipeBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "recipe.json"));
if (Sha(recipeBytes) != recipeHash) throw new InvalidDataException("Fixed research recipe changed.");
using var recipe = JsonDocument.Parse(recipeBytes);
var corpus = new List<(string Id, string Cohort, byte[] Pdf, JsonDocument Oracle, JsonDocument? FormalOracle)>();
var root = Path.Combine(AppContext.BaseDirectory, "fixtures");
Load(root, "development", 9, "4a3df43d65e8360966a0562922384779b456d3a3ac7c50aee29b82799a6e34c1");
var holdoutRoot = Path.Combine(AppContext.BaseDirectory, "holdout");
if (Directory.Exists(holdoutRoot)) Load(holdoutRoot, "unused-holdout", 6, "050b7640b6de309db2be78a932cf558f4d36b3b89c29b95f7737228e025079b9");
else if (!preflight) throw new InvalidDataException("Independent unused holdout is not frozen; native execution prohibited.");
if (corpus.Select(c => c.Id).Distinct().Count() != corpus.Count) throw new InvalidDataException("Duplicate fixture identity.");
Console.WriteLine(JsonSerializer.Serialize(new { recipeSHA256 = recipeHash, sourceCommit = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "uncommitted", originalPins = corpus.Select(c => new { c.Id, c.Cohort, pdfSHA256 = Sha(c.Pdf) }), preflight = TilePreflight.Run(), formalScorerPreflight = Directory.Exists(holdoutRoot) ? FormalPreflight.Run(holdoutRoot) : null, scope = "Deterministic geometry/fixture controls only; no native OCR or model quality" }));
if (preflight) { foreach (var c in corpus) { c.Oracle.Dispose(); c.FormalOracle?.Dispose(); } return 0; }
// Frozen source-validity controls passed only the two fresh ordinary positives.
// Historical development inputs and invalid special inputs are never re-inferred.
var nativeCorpus = corpus.Where(c => c.Cohort == "unused-holdout" && c.Oracle.RootElement.GetProperty("kind").GetString() == "Timetable").ToArray();
if (nativeCorpus.Length != 2) throw new InvalidDataException("Exactly two source-valid ordinary heldouts are required.");
var excludedInputs = corpus.Where(c => !nativeCorpus.Any(n => n.Id == c.Id)).Select(c => new {
    c.Id, c.Cohort, nativeCalls = 0, qualityAssessed = false, correctNegativeRejection = false,
    reason = c.Cohort == "development" ? "Consumed historical development; no repeated acquisition" : "Special input contract/capacity preflight failure; no OCR-quality or negative credit"
}).ToArray();
var ownedRoot = Path.Combine(Path.GetTempPath(), "takupoke-raster-tiles-" + Guid.NewGuid().ToString("N"));
using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(80));
var observations = new List<object>(); var executionErrors = 0; var incorrectAcceptances = 0;
try
{
    var models = new WindowsRecoveryModels(ownedRoot); await models.InstallOcrAsync(null, lifetime.Token);
    using var ocr = await models.OpenOcrAsync(lifetime.Token);
    var managed = new { version = typeof(InferenceSession).Assembly.GetName().Version?.ToString(), sha256 = Sha(File.ReadAllBytes(typeof(InferenceSession).Assembly.Location)) };
    var native = Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Where(m => Path.GetFileName(m.FileName).Equals("onnxruntime.dll", StringComparison.OrdinalIgnoreCase)).Select(m => new { m.FileVersionInfo.FileVersion, sha256 = Sha(File.ReadAllBytes(m.FileName)) }).ToArray();
    var pinned = recipe.RootElement.GetProperty("runtime");
    if (managed.version != pinned.GetProperty("managedVersion").GetString() || managed.sha256 != pinned.GetProperty("managedSHA256").GetString() || native.Length != 1 || native[0].sha256 != pinned.GetProperty("nativeSHA256").GetString()) throw new InvalidDataException("Loaded ORT differs from actual frozen Windows1.26 runtime.");
    Console.WriteLine(JsonSerializer.Serialize(new { runtimeEvidence = new { managed, native }, ocrBundle = WindowsRecoveryModels.OcrBundle, llmCalls = 0, scope = "Pinned CPU OCR sessions only; no OS OCR branch" }));
    foreach (var c in nativeCorpus)
    {
        IReadOnlyList<RecoveryRaster> rasters = [];
        try
        {
            using var renderDeadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); renderDeadline.CancelAfter(TimeSpan.FromSeconds(180));
            var kind = Enum.Parse<MaterialKind>(c.Oracle.RootElement.GetProperty("kind").GetString()!); var capture = new RecoveryReadCapture(); string? strictError = null;
            try { var pages = PdfPigLayoutReader.Read(c.Pdf, kind, renderDeadline.Token, capture); if (kind == MaterialKind.Timetable) _ = PdfScheduleParser.Timetable(pages, renderDeadline.Token); else _ = PdfScheduleParser.Special(pages, kind, renderDeadline.Token); }
            catch (PdfParseException error) { strictError = error.Stage; }
            if (strictError is null || !RecoveryPolicy.Eligible(kind, strictError)) throw new InvalidDataException("Fixed image-only PDF did not take the eligible original recovery boundary.");
            rasters = await NativeRasterReader.RenderAsync(c.Pdf, renderDeadline.Token);
            foreach (var tiled in new[] { false, true })
            {
                var arm = tiled ? "pinned-cpu-fixed-grid" : "pinned-cpu-fullpage"; var stage = "OCR/fullpage coverage/Builder"; var watch = Stopwatch.StartNew(); var validatorAccepted = false;
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); deadline.CancelAfter(TimeSpan.FromSeconds(900));
                try
                {
                    var doc = await Task.Run(() => NativeRasterReader.Build(rasters, Sha(c.Pdf), kind, tiled, ocr,
                        observation => Console.WriteLine(JsonSerializer.Serialize(new { c.Id, c.Cohort, arm, nativeEvidence = observation })), deadline.Token), deadline.Token);
                    stage = "Rules/Engine/Validator";
                    var prepared = await RecoveryStructure.ResolveAsync(doc, "windows", 10, [], deadline.Token);
                    var run = prepared.Document is null ? null : await RecoveryEngine.RunAsync(prepared.Document, "windows", 10, true, [], _ => null, deadline.Token);
                    var accepted = run?.Result is not null && run.State == RecoveryJobState.AwaitingConfirmation && RecoveryValidator.Validate(prepared.Document!, run.Result, deadline.Token).CanAdopt;
                    validatorAccepted = accepted;
                    MaterialAnalysis? formal = null; var exact = false;
                    if (accepted)
                    {
                        stage = "full formal/literal oracle";
                        var source = new SourceRecord("fictional", kind, "fictional.pdf", "fictional", "fictional.pdf", Sha(c.Pdf), c.Pdf.Length, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);
                        formal = RecoveryAnalysisConverter.Convert(source, prepared.Document!, run!.Result!, DateTimeOffset.UnixEpoch, deadline.Token);
                        // Historical literal controls assume one clock per period for every day.
                        // Independent heldout gold carries the complete date-specific clock contract.
                        exact = c.FormalOracle is null
                            ? LiteralOracle.Exact(prepared.Document!, formal, c.Oracle.RootElement)
                            : IndependentFormalOracle.Exact(prepared.Document!, formal, c.FormalOracle.RootElement);
                    }
                    var negative = c.Oracle.RootElement.GetProperty("expectedSafeRejection").GetBoolean(); var wrongAccept = accepted && (!exact || negative); if (wrongAccept) incorrectAcceptances++;
                    Record(new { c.Id, c.Cohort, arm, assessed = true, accepted, exact = accepted ? (bool?)exact : null, negative, incorrectValidatorAcceptance = wrongAccept, readablePositiveRejection = !negative && !accepted, correctNegativeRejection = negative && !accepted, stage, milliseconds = watch.ElapsedMilliseconds, strictError, errors = run?.Errors ?? prepared.Errors, rawOcrSources = doc.Sources.Where(s => s.FromOcr), formal, llmCalls = 0 });
                }
                catch (Exception error)
                {
                    var confirmedInputRejection = stage == "OCR/fullpage coverage/Builder" && (error is TileEvidenceException || error is InvalidDataException && error.Message is "OCRで判読できない文字があります。空欄には置き換えません。" or "OCRが認識していない印字があります。読めなかった内容を省略できません。");
                    var negative = c.Oracle.RootElement.GetProperty("expectedSafeRejection").GetBoolean(); if (validatorAccepted && negative) incorrectAcceptances++; if (!confirmedInputRejection) executionErrors++;
                    Record(new { c.Id, c.Cohort, arm, assessed = confirmedInputRejection, accepted = validatorAccepted, exact = (bool?)null, negative, incorrectValidatorAcceptance = validatorAccepted && !negative ? (bool?)null : validatorAccepted && negative, readablePositiveRejection = confirmedInputRejection && !negative, correctNegativeRejection = confirmedInputRejection && negative, stage, milliseconds = watch.ElapsedMilliseconds, errorType = error.GetType().Name, errorMessage = error.Message[..Math.Min(error.Message.Length, 1024)], llmCalls = 0 });
                }
            }
        }
        catch (Exception error)
        {
            executionErrors += 2;
            foreach (var arm in new[] { "pinned-cpu-fullpage", "pinned-cpu-fixed-grid" }) Record(new { c.Id, c.Cohort, arm, assessed = false, accepted = false, exact = (bool?)null, stage = "Shared original Reader/Strict/render", errorType = error.GetType().Name, errorMessage = error.Message[..Math.Min(error.Message.Length, 1024)], correctNegativeRejection = false, llmCalls = 0 });
        }
        finally { foreach (var raster in rasters) CryptographicOperations.ZeroMemory(raster.Bgra); }
    }
    var rows = observations.Select(o => JsonSerializer.SerializeToElement(o)).ToArray();
    var cohorts = nativeCorpus.Select(c => c.Cohort).Distinct().SelectMany(cohort => new[] { "pinned-cpu-fullpage", "pinned-cpu-fixed-grid" }.Select(arm =>
    {
        var eligible = nativeCorpus.Where(c => c.Cohort == cohort).ToArray();
        var group = rows.Where(o => o.GetProperty("Cohort").GetString() == cohort && o.GetProperty("arm").GetString() == arm).ToArray();
        bool Negative(JsonElement o) => eligible.Single(c => c.Id == o.GetProperty("Id").GetString()).Oracle.RootElement.GetProperty("expectedSafeRejection").GetBoolean();
        bool Yes(JsonElement o, string key) => o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;
        var positiveEligible = eligible.Count(c => !c.Oracle.RootElement.GetProperty("expectedSafeRejection").GetBoolean());
        var negativeEligible = eligible.Length - positiveEligible; var positiveAssessed = group.Count(o => !Negative(o) && Yes(o, "assessed")); var negativeAssessed = group.Count(o => Negative(o) && Yes(o, "assessed"));
        return new { cohort, arm, positiveEligible, negativeEligible, positiveAssessed, negativeAssessed, positiveUnassessed = positiveEligible - positiveAssessed, negativeUnassessed = negativeEligible - negativeAssessed,
            positiveExact = group.Count(o => !Negative(o) && Yes(o, "assessed") && Yes(o, "accepted") && Yes(o, "exact")), readablePositiveRejections = group.Count(o => Yes(o, "readablePositiveRejection")), correctNegativeRejections = group.Count(o => Yes(o, "correctNegativeRejection")), acceptedUnassessed = group.Count(o => Yes(o, "accepted") && !Yes(o, "assessed")), incorrectValidatorAcceptances = group.Count(o => Yes(o, "incorrectValidatorAcceptance")) };
    })).ToArray();
    var process = Process.GetCurrentProcess(); process.Refresh();
    Console.WriteLine(JsonSerializer.Serialize(new { recipeSHA256 = recipeHash, eligibleDocumentsPerArm = nativeCorpus.Length, excludedInputs, cohorts, executionErrors, incorrectValidatorAcceptances = incorrectAcceptances, observations, evaluatorProcessPeakWorkingSetBytes = process.PeakWorkingSet64,
        scope = "Two fresh source-valid ordinary PDFs only, same once-rendered image and pinned OCR; fixed fullpage-then-tile order/warm sessions, no causal latency rank. Consumed nine and invalid special inputs excluded without native calls/negative credit. Original thresholds and global ink/blank/certificate/Validator/formal guards. Accepted but formal-unassessed positives have unknown incorrect-accept status, never inferred correct. Native synchronous inference uses cooperative cancellation; hard CI deadline can terminate final report. No LLM/production adoption/model qualification." }));
    return executionErrors == 0 && incorrectAcceptances == 0 && observations.Count == nativeCorpus.Length * 2 ? 0 : 1;
}
catch (Exception error)
{
    Console.WriteLine(JsonSerializer.Serialize(new { initializationFailure = new { type = error.GetType().Name, message = error.Message[..Math.Min(error.Message.Length, 1024)] }, excludedInputs, observations, remainingArmObservationsUnassessed = nativeCorpus.Length * 2 - observations.Count, incorrectValidatorAcceptances = incorrectAcceptances, scope = "Execution failure is unassessed, never correct negative rejection" })); return 1;
}
finally
{
    foreach (var c in corpus) { c.Oracle.Dispose(); c.FormalOracle?.Dispose(); CryptographicOperations.ZeroMemory(c.Pdf); }
    try { if (Directory.Exists(ownedRoot)) Directory.Delete(ownedRoot, true); } catch (Exception error) { Console.Error.WriteLine("Owned research model directory cleanup failed: " + error.GetType().Name); throw new IOException("Owned model directory cleanup did not complete.", error); }
}
void Record(object observation) { observations.Add(observation); Console.WriteLine(JsonSerializer.Serialize(new { armObservation = observation })); }
void Load(string folder, string cohort, int count, string manifestHash)
{
    var manifestBytes = File.ReadAllBytes(Path.Combine(folder, "manifest.json")); if (Sha(manifestBytes) != manifestHash) throw new InvalidDataException("Frozen corpus manifest mismatch: " + cohort);
    using var manifest = JsonDocument.Parse(manifestBytes); var fixtures = manifest.RootElement.GetProperty("fixtures").EnumerateArray().ToArray(); if (fixtures.Length != count) throw new InvalidDataException("Frozen fixture count mismatch.");
    foreach (var fixture in fixtures)
    {
        var id = fixture.GetProperty("id").GetString()!; if (id != Path.GetFileName(id) || id.Contains("..") || id.Contains('/') || id.Contains('\\')) throw new InvalidDataException("Invalid fictional fixture identity.");
        var folderCase = Path.Combine(folder, id); var oracleBytes = File.ReadAllBytes(Path.Combine(folderCase, "literal-oracle.json")); if (Sha(oracleBytes) != fixture.GetProperty("oracleSha256").GetString()) throw new InvalidDataException("Independent original oracle pin failed.");
        var oracle = JsonDocument.Parse(oracleBytes); var pdf = File.ReadAllBytes(Path.Combine(folderCase, "fictional.pdf")); if (pdf.Length > 2000000 || Sha(pdf) != oracle.RootElement.GetProperty("pdfSha256").GetString()) { oracle.Dispose(); throw new InvalidDataException("Fixed image-only PDF pin failed."); }
        JsonDocument? formalOracle = null;
        if (cohort == "unused-holdout")
        {
            var formalBytes = File.ReadAllBytes(Path.Combine(folderCase, "literal-analysis-oracle.json"));
            if (Sha(formalBytes) != fixture.GetProperty("formalOracleSha256").GetString()) { oracle.Dispose(); throw new InvalidDataException("Independent formal oracle pin failed."); }
            formalOracle = JsonDocument.Parse(formalBytes);
        }
        corpus.Add((id, cohort, pdf, oracle, formalOracle));
    }
}
static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
