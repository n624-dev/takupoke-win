using Takupoke.Core;
using Takupoke.Infrastructure.Storage;
using Takupoke.Infrastructure.Recovery;

namespace Takupoke.Infrastructure.Materials;

public static class MaterialProjection
{
    public static MaterialAnalysis Assign(MaterialAnalysis analysis) => analysis.Kind == MaterialKind.Changes ? analysis : analysis with
    {
        Timetable = analysis.Timetable is { } timetable ? PdfClassAssignment.Assign(timetable) : null,
        Special = analysis.Special is { } special ? PdfClassAssignment.Assign(special) : null,
        ClassAssignmentVersion = PdfClassAssignment.Version
    };
    public static MaterialAnalysis Upgrade(MaterialAnalysis analysis, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (analysis.Kind == MaterialKind.Changes || analysis.ClassAssignmentVersion != 0) return analysis;
        // Validate the stored legacy projection before changing its class keys.
        if (analysis.Recovery is not null && !RecoveryAnalysisConverter.MayDisplay(analysis, token)) return analysis;
        if (analysis.Timetable?.Lessons.Any(l => l.SourceClassName is not null) == true ||
            analysis.Special?.Lessons.Any(l => l.SourceClassName is not null) == true ||
            analysis.Special?.ClassAssignmentVersion is > 0) return analysis;
        return Assign(analysis);
    }
}
