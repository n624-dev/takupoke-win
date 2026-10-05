using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Takupoke.Core;
using Takupoke.Infrastructure.Parsing;

// This preparation helper accepts only an independently generated PDF and its
// expected byte hash. No drawing atoms, literal oracle, OCR output or model is
// an input. The actual original PDF identity is distinct from future renders.
if (args.Length != 3) throw new ArgumentException("Expected PDF path, SHA256 and source-only case ID");
var file = new FileInfo(args[0]);
if (file.Length is < 1 or > 50 * 1024 * 1024) throw new InvalidDataException("PDF capacity guard");
if (args[1].Length != 64 || args[1].Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("SHA256 shape");
if (string.IsNullOrWhiteSpace(args[2]) || args[2].Length > 128 || args[2].Any(char.IsControl)) throw new InvalidDataException("Case ID shape");
var bytes = File.ReadAllBytes(file.FullName);
var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
if (!string.Equals(hash, args[1], StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("PDF byte pin mismatch");
using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(3));
var capture = new RecoveryReadCapture();
IReadOnlyList<PdfPageLayout>? pages = null;
TimetableAnalysis? strict = null;
var report = new Dictionary<string, object?>
{
    ["scope"] = "Actual PDFReader then Strict; source-only preparation, no raster/OCR/Builder/adoption/quality claim",
    ["sourceCommit"] = "3b5b23bc19f0c63b34a44e6fc0690a05463a241f",
    ["caseId"] = args[2], ["actualPdfSha256"] = hash, ["actualPdfBytes"] = bytes.Length,
    ["renderSha256"] = null, ["nativeOCRCalls"] = 0, ["providerCalls"] = 0,
    ["runtime"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    ["readerAssembly"] = typeof(PdfPigLayoutReader).Assembly.FullName,
    ["readerAttempts"] = 0, ["strictAttempts"] = 0,
    ["operationalOrUnclassifiedError"] = false, ["adoptionEvaluated"] = false
};
object Error(Exception e) => new { type = e.GetType().Name, e.Message, stage = (e as PdfParseException)?.Stage };
bool ExpectedSourceRefusal(Exception e) => e is PdfParseException p && p.Stage is "raster" or "P01" or "P02" or "P03" or "P04" or "P05" or "P06" or "P07" or "P08" or "P09" or "P10" or "P11" or "P12" or "P13" or "P14" or "P15" or "P16" or "P17" or "P18";
var clock = Stopwatch.StartNew();
try
{
    report["readerAttempts"] = 1;
    pages = PdfPigLayoutReader.Read(bytes, MaterialKind.Timetable, lifetime.Token, capture);
    report["readerReturned"] = true;
}
catch (Exception e)
{
    report["readerReturned"] = false; report["readerError"] = Error(e);
    if (!ExpectedSourceRefusal(e)) report["operationalOrUnclassifiedError"] = true;
}
report["readerMilliseconds"] = clock.Elapsed.TotalMilliseconds;
report["readerCompleted"] = capture.ReaderCompleted;
report["captureComplete"] = capture.Complete;
report["capturedPages"] = capture.Pages;
report["completeVectorPagesEligibleForReuse"] = capture.Pages.Where(p => p.State == Takupoke.Core.Recovery.RecoveryInputState.Complete).Select(p => p.Page).ToArray();
report["wholePagesRequiringActualRasterOCR"] = capture.Pages.Where(p => p.State != Takupoke.Core.Recovery.RecoveryInputState.Complete).Select(p => p.Page).ToArray();
if (pages is not null)
{
    clock.Restart(); report["strictAttempts"] = 1;
    try { strict = PdfScheduleParser.Timetable(pages, lifetime.Token); report["strict"] = strict; }
    catch (Exception e)
    {
        report["strictError"] = Error(e);
        if (!ExpectedSourceRefusal(e)) report["operationalOrUnclassifiedError"] = true;
    }
    report["strictMilliseconds"] = clock.Elapsed.TotalMilliseconds;
}
report["strictReturned"] = strict is not null;
Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
