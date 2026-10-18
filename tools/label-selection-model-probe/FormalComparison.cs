using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Recovery;
using Takupoke.Infrastructure.Storage;

// Conversion comparison extends this component probe; it does not fabricate an
// original PDF or establish Reader/OCR correctness. Expected recovery results
// are the same frozen certificate-verified corpus oracle used before inference.
internal static class FormalComparison
{
    internal static MaterialAnalysis Convert(RecoveryDocument document, RecoveryResult result, CancellationToken token)
    {
        var kind = document.Kind switch { RecoveryDocumentKind.Timetable => MaterialKind.Timetable,
            RecoveryDocumentKind.Exam => MaterialKind.Exam, _ => MaterialKind.ExamReturn };
        var source = new SourceRecord("fictional-component", kind, "fictional-component.pdf", "fictional-component", "fictional-component.pdf",
            document.PdfHash, 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);
        return RecoveryAnalysisConverter.Convert(source, document, result, DateTimeOffset.UnixEpoch, token);
    }
}
