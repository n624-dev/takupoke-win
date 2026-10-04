using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Takupoke.Infrastructure.Storage;
using Takupoke.Win.Platform;

// Isolated real acquisition baseline: no LLM Providers, school input or adoption.
Environment.SetEnvironmentVariable("ORT_TELEMETRY_DISABLED", "1");
var fixtureRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "fixtures"));
var manifestBytes = await File.ReadAllBytesAsync(Path.Combine(fixtureRoot, "manifest.json"));
using var manifest = JsonDocument.Parse(manifestBytes);
var eligible = new HashSet<string>(["Timetable-clean", "Exam-clean", "ExamReturn-clean", "Timetable-confusable-1", "Timetable-confusable-2", "Timetable-blank-values", "Timetable-unreadable", "Timetable-small-font", "Timetable-rotate-90"], StringComparer.Ordinal);
var fixtures = manifest.RootElement.GetProperty("fixtures").EnumerateArray().ToArray();
if (fixtures.Length != eligible.Count || !eligible.SetEquals(fixtures.Select(f => f.GetProperty("id").GetString()!))) throw new InvalidDataException("Only the fixed invented fixture corpus is accepted.");
var corpus = new List<(string Id, byte[] Pdf, JsonDocument Oracle)>();
foreach (var fixture in fixtures)
{
    var id = fixture.GetProperty("id").GetString()!; var directory = Path.Combine(fixtureRoot, id);
    var oracleBytes = await File.ReadAllBytesAsync(Path.Combine(directory, "literal-oracle.json"));
    if (Sha(oracleBytes) != fixture.GetProperty("oracleSha256").GetString()) throw new InvalidDataException("Literal oracle SHA mismatch.");
    var oracle = JsonDocument.Parse(oracleBytes); var pdf = await File.ReadAllBytesAsync(Path.Combine(directory, "fictional.pdf"));
    if (pdf.Length > 2_000_000 || Sha(pdf) != oracle.RootElement.GetProperty("pdfSha256").GetString()) throw new InvalidDataException("Invented PDF SHA mismatch.");
    corpus.Add((id, pdf, oracle));
}
Console.WriteLine(JsonSerializer.Serialize(new { corpusSha256 = Sha(manifestBytes), cases = corpus.Select(c => new { c.Id, pdfSha256 = Sha(c.Pdf) }), scope = "Fixed literal image-only fictional PDF acquisition baseline; no LLM or model qualification" }));
if (args.SequenceEqual(new[] { "--preflight" })) { foreach (var c in corpus) c.Oracle.Dispose(); return 0; }
var diagnosticOnly = args.SequenceEqual(new[] { "--tensor-diagnostics" });
if ((!diagnosticOnly && args.Length != 0) || !OperatingSystem.IsWindows()) throw new InvalidOperationException("Native acquisition requires Windows; arbitrary inputs and arguments are prohibited.");
var ownedRoot = Path.Combine(Path.GetTempPath(), "takupoke-fictional-raster-" + Guid.NewGuid().ToString("N"));
using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(20));
var observations = new List<object>(); var acceptedExact = 0; var incorrectValidatorAcceptances = 0; var operationalErrors = 0; var unassessedAcquisitionFailures = 0; var pipelineSafeRejections = 0; var expectedNegativesRejected = 0; var readablePositiveRejections = 0; var positiveCasesAssessed = 0; var negativeCasesAssessed = 0;
var initializationStage = "Pinned OCR bundle installation/smoke test";
try
{
    var models = new WindowsRecoveryModels(ownedRoot);
    await models.InstallOcrAsync(null, lifetime.Token); // Actual pinned production OCR bundle/store and smoke test.
    initializationStage = "Per-document acquisition";
    if (diagnosticOnly)
    {
        var tensorObservations = new List<object>();
        foreach (var c in corpus)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); deadline.CancelAfter(TimeSpan.FromSeconds(90));
            try { tensorObservations.Add(await OcrTensorDiagnostics.RunAsync(c.Id, c.Pdf, models, deadline.Token)); }
            catch (Exception error) { tensorObservations.Add(new { c.Id, errorType = error.GetType().Name, errorMessage = error.Message[..Math.Min(error.Message.Length, 1024)] }); }
        }
        Console.WriteLine(JsonSerializer.Serialize(new { recipe = "fictional-native-ocr-tensor-diagnostics-v1", sourceCommit = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "research-uncommitted", tensorObservations,
            scope = "Separate raw tensor/CTC evidence only; same production preprocessing and thresholds, firstpage, no recovery/quality denominator or LLM. Single optimizer-off detector contrast only for existingconfusable2." }, new JsonSerializerOptions { WriteIndented = true }));
        return tensorObservations.Any(o => o.GetType().GetProperty("errorType") is not null) ? 1 : 0;
    }
    var reader = new WindowsPdfRecovery(models);
    foreach (var c in corpus)
    {
        var watch = Stopwatch.StartNew(); var capture = new RecoveryReadCapture(); var stage = "Reader/Strict";
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); deadline.CancelAfter(TimeSpan.FromSeconds(90));
        try
        {
            var oracle = c.Oracle.RootElement; var kind = Enum.Parse<MaterialKind>(oracle.GetProperty("kind").GetString()!);
            string? strictError = null;
            try { var pages = PdfPigLayoutReader.Read(c.Pdf, kind, deadline.Token, capture); if (kind == MaterialKind.Timetable) _ = PdfScheduleParser.Timetable(pages, deadline.Token); else _ = PdfScheduleParser.Special(pages, kind, deadline.Token); }
            catch (PdfParseException error) { strictError = error.Stage; }
            // Image-only input intentionally ends Strict with raster and an
            // incomplete capture; production independently renders every page.
            if (strictError is not null && !RecoveryPolicy.Eligible(kind, strictError)) throw new InvalidDataException("Strict failure is ineligible for recovery: " + strictError);
            stage = "Windows render/OCR/Builder";
            var document = await reader.BuildAsync(c.Pdf, kind, Sha(c.Pdf), capture, deadline.Token);
            stage = "Rules/Engine/Validator";
            var prepared = await RecoveryStructure.ResolveAsync(document, "windows", 10, [], deadline.Token);
            var run = prepared.Document is null ? null : await RecoveryEngine.RunAsync(prepared.Document, "windows", 10, true, [], _ => null, deadline.Token);
            var accepted = run?.Result is not null && run.State == RecoveryJobState.AwaitingConfirmation && RecoveryValidator.Validate(prepared.Document!, run.Result, deadline.Token).CanAdopt;
            var exact = false; object? formalResult = null;
            if (accepted)
            {
                stage = "formal conversion/full literal oracle";
                var source = new SourceRecord("fictional", kind, "fictional.pdf", "fictional", "fictional.pdf", Sha(c.Pdf), 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);
                var formal = RecoveryAnalysisConverter.Convert(source, prepared.Document!, run!.Result!, DateTimeOffset.UnixEpoch, deadline.Token);
                formalResult = formal;
                exact = Exact(prepared.Document!, formal, oracle);
            }
            var mustReject = oracle.GetProperty("expectedSafeRejection").GetBoolean();
            if (mustReject) negativeCasesAssessed++; else positiveCasesAssessed++;
            if (accepted && (!exact || mustReject)) incorrectValidatorAcceptances++;
            if (accepted && exact && !mustReject) acceptedExact++;
            if (!accepted) { pipelineSafeRejections++; if (mustReject) expectedNegativesRejected++; else readablePositiveRejections++; }
            observations.Add(new { c.Id, strictError, accepted, fullLiteralExact = exact, expectedSafeRejection = mustReject,
                milliseconds = watch.ElapsedMilliseconds, stage, errors = run?.Errors ?? prepared.Errors, documentHash = RecoveryValidator.Fingerprint(document),
                rawOcrSources = document.Sources.Where(s => s.FromOcr).ToArray(), formalResult });
        }
        catch (Exception error)
        {
            // InvalidDataException alone cannot distinguish unreadable ink from
            // missing artifacts, memory or dictionary prerequisites. Credit only
            // these two exact input-rejection sites in the unchanged OCR source.
            var safePipelineFailure = stage == "Windows render/OCR/Builder" && error is InvalidDataException && error.Message is
                "OCRで判読できない文字があります。空欄には置き換えません。" or "OCRが認識していない印字があります。読めなかった内容を省略できません。";
            if (safePipelineFailure)
            {
                pipelineSafeRejections++;
                if (c.Oracle.RootElement.GetProperty("expectedSafeRejection").GetBoolean()) { negativeCasesAssessed++; expectedNegativesRejected++; }
                else { positiveCasesAssessed++; readablePositiveRejections++; }
            }
            else { unassessedAcquisitionFailures++; if (error is not InvalidDataException and not PdfParseException) operationalErrors++; }
            observations.Add(new { c.Id, accepted = false, fullLiteralExact = false, stage, milliseconds = watch.ElapsedMilliseconds,
                confirmedInputRejection = safePipelineFailure, acquisitionUnassessed = !safePipelineFailure,
                errorType = error.GetType().Name, errorMessage = error.Message[..Math.Min(error.Message.Length, 1024)],
                captureCompleted = capture.ReaderCompleted, captureStates = capture.Pages.Select(p => new { p.Page, p.State }) });
        }
        Console.WriteLine(JsonSerializer.Serialize(new { acquisitionCase = c.Id, completed = observations.Count }));
    }
    var process = Process.GetCurrentProcess(); process.Refresh();
    Console.WriteLine(JsonSerializer.Serialize(new { sourceCommit = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "research-uncommitted",
        recipe = "real-fictional-raster-acquisition-baseline-v1", pdfCases = corpus.Count, positiveCases = corpus.Count(c => !c.Oracle.RootElement.GetProperty("expectedSafeRejection").GetBoolean()), negativeCases = corpus.Count(c => c.Oracle.RootElement.GetProperty("expectedSafeRejection").GetBoolean()),
        acceptedExact, incorrectValidatorAcceptances, operationalErrors, unassessedAcquisitionFailures, pipelineSafeRejections, expectedNegativesRejected, readablePositiveRejections, positiveCasesAssessed, negativeCasesAssessed,
        positiveRejectionScope = "Refusing a literal readable positive is recovery failure, never correct negative rejection", evaluationStatus = unassessedAcquisitionFailures == 0 ? "completed" : "acquisition-errors",
        llmCalls = 0, ocrBundle = WindowsRecoveryModels.OcrBundle, evaluatorProcessPeakWorkingSetBytes = process.PeakWorkingSet64,
        scope = "Actual Reader/Strict and linked production Windows PDF render/OCR/Builder/Rules/Engine/Validator/formal conversion; independently drawn literal oracle. No acquisition presets, AI utility, saved adoption or qualification claim.", observations }, new JsonSerializerOptions { WriteIndented = true }));
    return observations.Count == corpus.Count && incorrectValidatorAcceptances == 0 && unassessedAcquisitionFailures == 0 ? 0 : 1;
}
catch (Exception error)
{
    Console.WriteLine(JsonSerializer.Serialize(new { sourceCommit = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "research-uncommitted",
        recipe = diagnosticOnly ? "fictional-native-ocr-tensor-diagnostics-v1" : "real-fictional-raster-acquisition-baseline-v1", evaluationStatus = "initialization-or-run-error", initializationStage,
        pdfCasesEligible = corpus.Count, casesObserved = observations.Count, remainingCasesUnassessed = corpus.Count - observations.Count,
        acceptedExact, incorrectValidatorAcceptances, expectedNegativesRejected, readablePositiveRejections, positiveCasesAssessed, negativeCasesAssessed,
        executionError = new { type = error.GetType().Name, message = error.Message[..Math.Min(error.Message.Length, 1024)] },
        scope = "Execution/prerequisite failure gives no negative credit or accuracy for unassessed documents", observations }, new JsonSerializerOptions { WriteIndented = true }));
    return 1;
}
finally
{
    foreach (var c in corpus) c.Oracle.Dispose();
    try { if (Directory.Exists(ownedRoot)) Directory.Delete(ownedRoot, true); } catch { }
}
static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
static bool Exact(RecoveryDocument document, MaterialAnalysis formal, JsonElement oracle)
{
    var year = oracle.GetProperty("schoolYear").GetInt32(); var classes = oracle.GetProperty("classes").EnumerateArray().Select(v => v.GetString()!).ToArray(); var days = oracle.GetProperty("days").EnumerateArray().Select(v => v.GetString()!).ToArray();
    if (formal.SchoolYear != year || document.SchoolYear != year || document.RequiredSlots.Count != oracle.GetProperty("requiredSlots").GetInt32() || !classes.ToHashSet().SetEquals(document.Classes) || !days.ToHashSet().SetEquals(document.Days)) return false;
    var expectedSlots = classes.SelectMany(cls => days.SelectMany(day => Enumerable.Range(1, oracle.GetProperty("maxPeriod").GetInt32()).Select(period => (cls, day, period)))).ToHashSet();
    if (!expectedSlots.SetEquals(document.RequiredSlots.Select(slot => (slot.ClassName, slot.Day, slot.Period)))) return false;
    var expected = oracle.GetProperty("lessons").EnumerateArray().Single();
    bool Names(LessonNames names) => names.Subject == expected.GetProperty("subject").GetString() && names.Teacher == expected.GetProperty("teacher").GetString() && names.Room == expected.GetProperty("room").GetString();
    if (formal.Timetable is { } normal) return normal.Term == oracle.GetProperty("term").GetString() && normal.Lessons.Count == 1 && normal.Lessons.Single() is { } lesson && Names(lesson.Names) && lesson.ClassName == expected.GetProperty("class").GetString() && lesson.Weekday.ToString() == expected.GetProperty("day").GetString() && lesson.Period == 1;
    if (formal.Special is not { } special || special.Lessons.Count != 1 || !Names(special.Lessons.Single().Names) || special.Lessons.Single() is not { } entry || entry.ClassName != expected.GetProperty("class").GetString() || entry.Date != expected.GetProperty("day").GetString() || entry.Period != 1 || entry.SpanStart != 1 || entry.SpanEnd != 1) return false;
    var firstClock = oracle.GetProperty("periodClocks").EnumerateArray().Single(clock => clock.GetProperty("period").GetInt32() == 1);
    var displayedClock = special.TimeFor(entry);
    if (entry.RecordedTime is null || entry.RecordedTime.Start != firstClock.GetProperty("start").GetString() || entry.RecordedTime.End != firstClock.GetProperty("end").GetString() || displayedClock != entry.RecordedTime) return false;
    if (!classes.ToHashSet().SetEquals(special.CoveredClasses) || !days.ToHashSet().SetEquals(special.CoveredDates)) return false;
    foreach (var day in days) foreach (var clock in oracle.GetProperty("periodClocks").EnumerateArray())
    {
        var actual = special.PeriodTime(DateOnly.ParseExact(day, "yyyy-MM-dd"), clock.GetProperty("period").GetInt32());
        if (actual is null || actual.Start != clock.GetProperty("start").GetString() || actual.End != clock.GetProperty("end").GetString()) return false;
    }
    return true;
}
