using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Takupoke.Infrastructure.Storage;

// The only inputs are source bytes and their identity. No expected content,
// roles, coordinates or cells enter the native acquisition/recovery process.
if (args.Length != 2) throw new ArgumentException("Expected fictional source path and digest.");
var bytes = await File.ReadAllBytesAsync(args[0]);
var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
if (hash != args[1] || bytes.Length is < 1 or > 50*1024*1024) throw new InvalidDataException("Source identity failed.");
using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(2));
var token = lifetime.Token; var watch = Stopwatch.StartNew();
var capture = new RecoveryReadCapture();
string stage = "Reader", outcome = "unassessed"; string? strictFailure = null, failure = null;
TimetableAnalysis? formal = null; RecoveryDocument? doc = null; IReadOnlyList<PdfPageLayout>? pages = null;
try
{
    try
    {
        pages = PdfPigLayoutReader.Read(bytes, MaterialKind.Timetable, token, capture);
        stage = "Strict"; formal = PdfScheduleParser.Timetable(pages, token); outcome = "strict-returned";
    }
    catch (PdfParseException error)
    {
        strictFailure = error.Stage;
        if (!RecoveryPolicy.Eligible(MaterialKind.Timetable, strictFailure)) outcome = "strict-ineligible";
        else if (!capture.Complete) outcome = "incomplete-reader-refused";
        else
        {
            stage = "Builder";
            doc = RecoveryCapturedLayoutBuilder.TryBuildWithoutRaster(hash, MaterialKind.Timetable, capture, token);
            if (doc is null) outcome = "raster-evidence-required-refused";
            else
            {
                stage = "Engine";
                var run = await RecoveryEngine.RunAsync(doc, "windows", 10, true, [], _ => null, token);
                if (run.Result is null) { outcome = "engine-refused"; failure = string.Join(',', run.Errors); }
                else
                {
                    stage = "Validator"; var validation = RecoveryValidator.Validate(doc, run.Result, token);
                    if (!validation.CanAdopt) { outcome = "validator-refused"; failure = string.Join(',', validation.Errors); }
                    else
                    {
                        stage = "Formal";
                        var source = new SourceRecord("invented", MaterialKind.Timetable, "fictional.pdf", "fictional", "fictional.pdf", hash, 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);
                        formal = RecoveryAnalysisConverter.Convert(source, doc, run.Result, DateTimeOffset.UnixEpoch, token).Timetable;
                        outcome = "recovery-formal-returned";
                    }
                }
            }
        }
    }
}
catch (PdfParseException e) { outcome = "safe-refusal"; failure = e.Stage; }
catch (InvalidDataException e) { outcome = "safe-refusal"; failure = e.Message; }
catch (OperationCanceledException) { outcome = "execution-error"; failure = "cancelled-or-deadline"; }
catch (Exception e) { outcome = "execution-error"; failure = e.GetType().Name+":"+e.Message; }
finally
{
    Console.WriteLine(JsonSerializer.Serialize(new { recipe = "ordered-row-e2e-v1", hash, stage, outcome, strictFailure, failure,
        readerComplete = capture.Complete, acquiredPages = capture.Pages.Count,
        acquiredGlyphs = capture.Pages.Sum(p => p.Layout?.Glyphs.Count ?? 0), builderCells = doc?.Cells.Count,
        originalOrderedProofs = doc?.Cells.Count(c => c.OrderedRowProof is not null), formal,
        nativeOcrCalls = 0, llmCalls = 0, milliseconds = watch.ElapsedMilliseconds }));
    CryptographicOperations.ZeroMemory(bytes);
}
