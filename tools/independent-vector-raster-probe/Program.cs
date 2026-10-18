using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Takupoke.Infrastructure.Storage;

// Research source-only observation. The independently generated manifest and
// literal assertions belong outside this program; none enter the pipeline.
if (args.Length != 3) throw new ArgumentException("Expected fictional PDF path, exact SHA256, fictional case ID.");
var bytes = await File.ReadAllBytesAsync(args[0]);
var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
var permitted = new Dictionary<string, string> {
    ["fictional-wide-unlabeled"] = "a90ed8be04c058789a354ab7a2ad78c2e598b75433fc1152df2584bd00f4223a",
    ["fictional-wide-labeled-control"] = "4ad4bed500a8b1b1112e1a918225353bf9db84eab784f577d49e08257307b0bc"
};
if (bytes.Length is < 1 or > 50 * 1024 * 1024 || hash != args[1] || !permitted.TryGetValue(args[2], out var approvedHash) || hash != approvedHash)
    throw new InvalidDataException("Fictional input identity/size guard failed.");
using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(2));
var token = lifetime.Token; var capture = new RecoveryReadCapture(); var watch = Stopwatch.StartNew();
IReadOnlyList<PdfPageLayout>? pages = null; object? strict = null; RecoveryDocument? document = null;
VectorRasterBlankProof? rasterProof = null; object? preparedObservation = null; RecoveryRun? run = null; object? validation = null; object? formal = null;
string stage = "Reader", outcome = "unassessed"; string? strictFailure = null; object? failure = null; object? strictDiagnostic = null;
try
{
    try
    {
        pages = PdfPigLayoutReader.Read(bytes, MaterialKind.Timetable, token, capture);
        stage = "Strict";
        strict = PdfScheduleParser.Timetable(pages, token);
        outcome = "strict-returned";
    }
    catch (PdfParseException error)
    {
        strictFailure = error.Stage; strictDiagnostic = new { error.Stage, error.StackTrace };
        if (!RecoveryPolicy.Eligible(MaterialKind.Timetable, strictFailure)) { outcome = "strict-ineligible"; }
        else if (!capture.Complete) { outcome = "recovery-needs-independent-raster-acquisition"; }
        else
        {
            // Complete vector glyphs are scaled to actual rendered pixel coordinates.
            // Only original pixel/rule scanner proves blank ink, never gold.
            stage = "Windows render/actual blank proof";
            rasterProof = await VectorRasterBlankProof.Create(bytes, capture, token);
            pages = rasterProof.Pages;
            stage = "Builder";
            document = RecoveryDocumentBuilder.Build(hash, MaterialKind.Timetable, pages,
                rasterProof.InkFree, token: token, allowStructureProposal: true);
            stage = "Structure";
            var prepared = await RecoveryStructure.ResolveAsync(document, "windows", 10, [], token);
            preparedObservation = new { prepared.State, prepared.Errors, HasDocument = prepared.Document is not null };
            if (prepared.Document is null) outcome = "structure-refused";
            else
            {
                document = prepared.Document; stage = "Rules/Engine/Validator";
                run = await RecoveryEngine.RunAsync(document, "windows", 10, true, [], _ => null, token);
                if (run.Result is null) outcome = "engine-refused";
                else
                {
                    var checkedResult = RecoveryValidator.Validate(document, run.Result, token);
                    validation = checkedResult;
                    if (!checkedResult.CanAdopt) outcome = "validator-refused";
                    else
                    {
                        stage = "Formal converter";
                        var source = new SourceRecord(args[2], MaterialKind.Timetable, "fictional.pdf", "fictional", "fictional.pdf", hash,
                            1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);
                        formal = RecoveryAnalysisConverter.Convert(source, document, run.Result, DateTimeOffset.UnixEpoch, token);
                        outcome = "formal-returned-unscored";
                    }
                }
            }
        }
    }
}
catch (Exception error)
{
    outcome = error is OperationCanceledException ? "execution-cancelled-unassessed" : "stage-exception-unclassified";
    failure = new { Type = error.GetType().Name, error.Message };
}
finally
{
    Console.WriteLine(JsonSerializer.Serialize(new {
        recipe = "fictional-windows-vector-render-blank-v1", sourceCommit = Environment.GetEnvironmentVariable("GITHUB_SHA"), parserVersion = PdfScheduleParser.TimetableVersion, id = args[2], pdfSha256 = hash, pdfBytes = bytes.Length,
        outcome, stage, strictFailure, strictDiagnostic, failure, milliseconds = watch.ElapsedMilliseconds,
        capture.ReaderCompleted, capture.Complete, capture.Pages, actualReaderPages = pages,
        renderedPixels = rasterProof?.PixelProvenance, blankQueries = rasterProof?.BlankQueries, totalBlankQueries = rasterProof?.TotalBlankQueries, blankQueriesTruncated = rasterProof?.BlankQueriesTruncated,
        strictResult = strict, document, preparedObservation, run, validation, formal,
        nativeOcrCalls = 0, llmCalls = 0, formalOracleCompared = false,
        limitations = "Actual Windows render blank proof for complete vector capture only; no OCR/model, oracle or quality score. Partial captured layouts are preserved but never promoted."
    }));
    rasterProof?.Dispose(); CryptographicOperations.ZeroMemory(bytes);
}
return outcome is "stage-exception-unclassified" or "execution-cancelled-unassessed" ? 1 : 0;
