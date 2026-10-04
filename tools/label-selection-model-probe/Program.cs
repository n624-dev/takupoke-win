using System.Diagnostics;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.AI.Foundry.Local;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using Microsoft.Extensions.Logging.Abstractions;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Storage;
using Takupoke.Win.Platform;

// Public artifacts and entirely fictional documents only. No school file input,
// app storage, school endpoint, cloud inference, upload, or production activation.
Environment.SetEnvironmentVariable("ORT_TELEMETRY_DISABLED", "1");
var requested = args.Length == 0 ? new[] { "qwen2.5-1.5b-instruct-generic-cpu:4" } : args;
var allowed = new[] { "qwen3-0.6b-generic-cpu:4", "qwen2.5-1.5b-instruct-generic-cpu:4", "qwen3.5-2b-text-generic-cpu:1", "qwen3.5-4b-generic-cpu:3" };
if (!args.SequenceEqual(new[] { "--preflight" }) && requested.Any(id => !allowed.Contains(id))) throw new ArgumentException("Only pinned public evaluation IDs are accepted.");
var corpus = QualificationCorpus.Create().Where(c => c.Id.EndsWith("-0", StringComparison.Ordinal) || c.Id.EndsWith("-1", StringComparison.Ordinal)).ToArray();
// Preflight establishes that every positive admits a physical certificate and
// complete production Validator result; these oracle outputs are never model input.
var expectedResults = new Dictionary<string, RecoveryResult>();
foreach (var c in corpus.Where(c => c.ShouldAdopt))
{
    var cell = c.Document.Cells.Single(cell => cell.Id == c.CellId); var prompt = RecoveryStructure.Prompt(c.Document, cell);
    var scopes = RecoveryStructure.Verify(c.Document, cell, prompt, [QualificationCorpus.Oracle(prompt)]);
    var rebuilt = c.Document with { Cells = c.Document.Cells.Select(v => v.Id == cell.Id ? v with { RoleScopes = scopes } : v).ToArray() };
    var preflight = await RecoveryEngine.RunAsync(rebuilt, "windows", 10, true, [], _ => null);
    if (preflight.Result is null || !RecoveryValidator.Validate(rebuilt, preflight.Result).CanAdopt) throw new InvalidDataException("Fictional positive corpus preflight failed: " + c.Id + ":" + string.Join(',', preflight.Errors));
    expectedResults[c.Id] = preflight.Result;
}
Console.WriteLine(JsonSerializer.Serialize(new { labelProtocolPreflight = await LabelProtocolPreflight.RunAsync(corpus),
    corpusManifest = corpus.Select(c => new { c.Id, c.ShouldAdopt, sourceHash = RecoveryValidator.Fingerprint(c.Document) }).ToArray(),
    productionRuleControls = await ProductionRuleBench.RunAsync(),
    nativePrerequisites = "Model-free preflight does not check Visual C++ native prerequisites." }));
if (args.SequenceEqual(new[] { "--preflight" })) return 0;
// Check only the four known x64 CRT imports in System32. Do not search a
// caller-controlled directory and never download a model before this gate.
if (!OperatingSystem.IsWindows())
{
    Console.WriteLine(JsonSerializer.Serialize(new { errorType = "UnsupportedNativeHost", requiredHost = "Windows x64" }));
    return 1;
}
var requiredCrt = new[] { "MSVCP140.dll", "MSVCP140_1.dll", "VCRUNTIME140.dll", "VCRUNTIME140_1.dll" };
var missingCrt = requiredCrt.Where(name =>
{
    if (!NativeLibrary.TryLoad(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), name), out var handle)) return true;
    NativeLibrary.Free(handle); return false;
}).ToArray();
if (missingCrt.Length > 0)
{
    Console.WriteLine(JsonSerializer.Serialize(new { errorType = "MissingVisualCppRuntime", missingLibraries = missingCrt,
        installerUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe",
        action = "Install the official Microsoft Visual C++ x64 Redistributable, then rerun the model command. No model was downloaded." }));
    return 1;
}
var ownedRoot = Path.Combine(Path.GetTempPath(), "takupoke-public-model-evaluation-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(ownedRoot);
using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(100));
var results = new List<object>();
try
{
    await FoundryLocalManager.CreateAsync(new Configuration { AppName = "takupoke-public-model-evaluation", AppDataDir = Path.Combine(ownedRoot, "app"), ModelCacheDir = Path.Combine(ownedRoot, "models"), LogsDir = Path.Combine(ownedRoot, "logs"), AdditionalSettings = new Dictionary<string, string> { ["DisableNonessentialTelemetry"] = "true" } }, NullLogger.Instance, cancellation.Token);
    var catalog = await FoundryLocalManager.Instance.GetCatalogAsync(cancellation.Token);
    foreach (var id in requested)
    {
        try
        {
            var model = await catalog.GetModelVariantAsync(id, cancellation.Token) ?? throw new InvalidDataException("Pinned variant unavailable.");
            if (model.Id != id) throw new InvalidDataException("Variant ID mismatch.");
            await model.DownloadAsync(ct: cancellation.Token);
            var path = await model.GetPathAsync(cancellation.Token); if (path is null || !Directory.Exists(path)) throw new InvalidDataException("Cached directory missing.");
            var fullPath = Path.GetFullPath(path); var prefix = Path.GetFullPath(ownedRoot) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Cache outside owned evaluation directory.");
            var files = new List<Artifact>(); long bytes = 0;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            { await using var input = File.OpenRead(file); bytes += input.Length; files.Add(new(Path.GetRelativePath(path, file).Replace('\\', '/'), input.Length, Convert.ToHexStringLower(await SHA256.HashDataAsync(input, cancellation.Token)))); }
            if (files.Count == 0 || bytes > 8L * 1024 * 1024 * 1024) throw new InvalidDataException("Artifact inventory outside bounds.");
            async Task<bool> Verify(CancellationToken token)
            {
                foreach (var f in files)
                { await using var input = File.OpenRead(Path.Combine(path, f.Path)); if (input.Length != f.Size || Convert.ToHexStringLower(await SHA256.HashDataAsync(input, token)) != f.Sha256) return false; }
                return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Count() == files.Count;
            }
            // Evaluation-only readiness ticket. This is never a published manifest
            // or app catalog entry. Every real downloaded artifact is verified.
            var manifest = new RecoveryModelManifest(id, "evaluation-1", "https://models.example.invalid/evaluation-only", bytes,
                RecoveryValidator.Fingerprint(files), "foundryLocal", "10.0.26100", 1, "CPU", "candidate-not-activated", true);
            await using var production = new FoundryLocalRecoveryProvider(model, manifest, Verify, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);
            var measured = new CountingProvider(production); var caseResults = new List<object>(); var positiveExact = 0; var negativeRejected = 0; var incorrectValidatorAcceptances = 0; var errors = 0; var positiveLabelRolesExact = 0; var positiveLabelRolesWrong = 0; var positiveLabelRolesAssessed = 0;
            foreach (var c in corpus)
            {
                production.ResetObservation();
                var beforeCalls = measured.Calls; var beforeNative = production.NativeCompletionsStarted; var beforeReturned = production.NativeCompletionsReturned; var watch = Stopwatch.StartNew();
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token); deadline.CancelAfter(TimeSpan.FromSeconds(180));
                try
                {
                    var structure = await DirectNativeProposal.RunAsync(c.Document, measured, deadline.Token);
                    RecoveryRun? run = structure.Document is null ? null : await RecoveryEngine.RunAsync(structure.Document, "windows", 10, true, [measured], _ => null, deadline.Token);
                    var previewed = run?.Result is not null && run.State == RecoveryJobState.AwaitingConfirmation && RecoveryValidator.Validate(structure.Document!, run.Result, deadline.Token).CanAdopt;
                    var actual = run?.Result?.Cells.Single(v => v.CellId == c.CellId).Lessons.SingleOrDefault();
                    var targetCellExact = previewed && actual?.Subject.Value == c.Subject && actual?.Teacher.Value == c.Teacher && actual?.Room.Value == c.Room;
                    var wholeResultExact = targetCellExact && expectedResults.TryGetValue(c.Id, out var expected) &&
                        RecoveryValidator.Fingerprint(run!.Result! with { Metadata = expected.Metadata }) == RecoveryValidator.Fingerprint(expected);
                    var calls = measured.Calls - beforeCalls;
                    watch.Stop();
                    var caseErrors = run?.Errors ?? structure.Errors;
                    var runtimeError = caseErrors.Any(e => e is "runtimeFailure" or "noLocalProvider") || structure.State == RecoveryJobState.AwaitingModel;
                    if (runtimeError) errors++;
                    if (c.ShouldAdopt && wholeResultExact && calls > 0 && run!.Result!.Metadata.Provider == "foundryLocal") positiveExact++;
                    if (!c.ShouldAdopt && !previewed && calls > 0 && !runtimeError) negativeRejected++;
                    if (previewed && (!c.ShouldAdopt || !wholeResultExact)) incorrectValidatorAcceptances++;
                    int? labelRolesExact = null;
                    if (c.ShouldAdopt && calls > 0)
                    {
                        var prompt = RecoveryStructure.Prompt(c.Document, c.Document.Cells.Single(v => v.Id == c.CellId));
                        var oracle = QualificationCorpus.Oracle(prompt);
                        var expectedLabels = new Dictionary<string, string[]> { ["subject"] = oracle.Subject.Evidence.SkipLast(3).ToArray(),
                            ["teacher"] = oracle.Teacher.Evidence.SkipLast(3).ToArray(), ["room"] = oracle.Room.Evidence.SkipLast(3).ToArray() };
                        labelRolesExact = expectedLabels.Count(pair => production.LastSelection?.TryGetValue(pair.Key, out var actualIds) == true && pair.Value.SequenceEqual(actualIds!));
                        positiveLabelRolesExact += labelRolesExact.Value; positiveLabelRolesWrong += 3 - labelRolesExact.Value; positiveLabelRolesAssessed += 3;
                    }
                    watch.Stop();
                    caseResults.Add(new { c.Id, c.ShouldAdopt, previewed, targetCellExact, wholeResultExact, calls,
                        nativeCompletionsStarted = production.NativeCompletionsStarted - beforeNative, nativeCompletionsReturned = production.NativeCompletionsReturned - beforeReturned,
                        preflightRejected = calls == 0 && structure.State == RecoveryJobState.Failed,
                        labelRolesExact, rawLabelSelection = production.LastSelection, rawModelOutput = production.LastRawOutput,
                        rawOriginalLength = production.LastRawOriginalLength, rawTruncated = production.LastRawTruncated,
                        adapterStage = production.LastStage, sourceHash = RecoveryValidator.Fingerprint(c.Document), milliseconds = watch.ElapsedMilliseconds, errors = caseErrors });
                }
                catch (Exception failure)
                {
                    errors++; watch.Stop(); caseResults.Add(new { c.Id, c.ShouldAdopt, sourceHash = RecoveryValidator.Fingerprint(c.Document), errorType = failure.GetType().Name,
                        calls = measured.Calls - beforeCalls, nativeCompletionsStarted = production.NativeCompletionsStarted - beforeNative, nativeCompletionsReturned = production.NativeCompletionsReturned - beforeReturned,
                        rawModelOutput = production.LastRawOutput, rawOriginalLength = production.LastRawOriginalLength, rawTruncated = production.LastRawTruncated,
                        adapterStage = production.LastStage, milliseconds = watch.ElapsedMilliseconds });
                }
                Console.WriteLine(JsonSerializer.Serialize(new { model = id, caseId = c.Id, completed = caseResults.Count }));
            }
            var process = Process.GetCurrentProcess(); process.Refresh();
            results.Add(new { modelId = id, runtimeVersion = "Foundry.Local.WinML:1.2.4", backend = "Windows CPU", osVersion = Environment.OSVersion.VersionString,
                artifactBytes = bytes, artifacts = files, corpus = "fictional-folded-structure-v1-label-selection-pilot-variants-0-1", inferenceRecipe = LabelSelectionProtocol.Recipe, inputEncoding = "Japanese-readable label-selection JSON", sampler = new { temperature = 0, randomSeed = 17, maxTokens = 512, responseFormat = "json_schema", maxLabelIdsPerRole = 48 }, positiveCases = corpus.Count(c => c.ShouldAdopt), negativeCases = corpus.Count(c => !c.ShouldAdopt),
                positiveExact, negativeRejected, incorrectValidatorAcceptances, errors, modelCalls = measured.Calls, positiveLabelRolesExact, positiveLabelRolesWrong, positiveLabelRolesAssessed, positiveLabelRolesEligible = 3 * corpus.Count(c => c.ShouldAdopt),
                nativeCompletionsStarted = production.NativeCompletionsStarted, nativeCompletionsReturned = production.NativeCompletionsReturned,
                labelRoleScoring = "Positive source-label obligations only; a completion failing the strict label decoder fails all three obligations. Runtime exceptions remain separately counted; assessed and eligible denominators are both reported.",
                evaluationStatus = errors == 0 ? "completed" : "runtime-errors",
                developmentCorpusStatus = errors == 0 && positiveExact == corpus.Count(c => c.ShouldAdopt) && negativeRejected == corpus.Count(c => !c.ShouldAdopt) && incorrectValidatorAcceptances == 0 ? "exact" : "failed", evaluatorProcessPeakWorkingSetBytes = process.PeakWorkingSet64, evaluatorProcessTerminalPrivateMemoryBytes = process.PrivateMemorySize64,
                memoryScope = "Evaluator process only; peak cumulative within process, excludes any child runtime processes; no minimum-device claim",
                sourceCommit = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "local-uncommitted", pipelineStage = "Isolated model label-selection correctness experiment after production Rules safety preflight; generic all-ID schema and measured cuts then unchanged production certificate; NOT production model benefit or PDF/Strict/builder/formal-conversion model validation",
                rawOutputScope = "Every scored completion retains a bounded 16384-character prefix with original length/truncation marker; no extra or substituted diagnostic generation", caseResults, qualification = "candidate evidence only; independent held-out validation required before activation" });
        }
        catch (Exception failure) { results.Add(new { modelId = id, errorType = failure.GetType().Name }); }
    }
    Console.WriteLine(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    return results.Count == requested.Length && !results.Any(r => r.GetType().GetProperty("errorType") is not null || r.GetType().GetProperty("errors")?.GetValue(r) is int failures && failures > 0) ? 0 : 1;
}
catch (Exception failure) { Console.WriteLine(JsonSerializer.Serialize(new { errorType = failure.GetType().Name })); return 1; }
finally
{
    if (FoundryLocalManager.IsInitialized) FoundryLocalManager.Instance.Dispose();
    try { Directory.Delete(ownedRoot, true); } catch { }
}
internal sealed record Artifact(string Path, long Size, string Sha256);
internal sealed class CountingProvider(ILocalRecoveryProvider provider) : ILocalRecoveryProvider
{
    public int Calls { get; private set; }
    public string Id => provider.Id; public bool LocalOnly => provider.LocalOnly; public RecoveryMetadata Metadata => provider.Metadata;
    public Task<LocalProviderState> AvailabilityAsync(CancellationToken token) => provider.AvailabilityAsync(token);
    public Task<IReadOnlyList<RecoveryLesson>> RecoverCellAsync(RecoveryPromptCell cell, CancellationToken token) { Calls++; return provider.RecoverCellAsync(cell, token); }
}
