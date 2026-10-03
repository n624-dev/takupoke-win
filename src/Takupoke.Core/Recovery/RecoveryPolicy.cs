namespace Takupoke.Core.Recovery;

public static class RecoveryPolicy
{
    public static RecoveryDocumentKind? Kind(MaterialKind kind) => kind switch
    { MaterialKind.Timetable => RecoveryDocumentKind.Timetable, MaterialKind.Exam => RecoveryDocumentKind.Exam, MaterialKind.ExamReturn => RecoveryDocumentKind.Return, _ => null };
    // Missing semantic headings still need evidence; this only permits an attempt, never adoption.
    public static bool Eligible(MaterialKind kind, string failure) => Kind(kind) is not null && failure is
        "raster" or "P01" or "P02" or "P03" or "P04" or "P05" or "P06" or "P07" or "P08" or "P10" or "P12" or "P13" or "P14" or "P15" or "P17" or "P18" or "P19" or "P20" or "P21";
    public static IReadOnlyList<string> Providers(string os, int majorVersion = 0) => os switch
    {
        "ios" when majorVersion >= 27 => ["systemLanguageModel", "coreAI", "llamaCpp"],
        "ios" => ["systemLanguageModel", "llamaCpp"],
        "android" => ["liteRtLm"], // ML Kit GenAI is deliberately excluded for this school app.
        "windows" => ["windowsLanguageModel", "foundryLocal"],
        _ => []
    };
    public static bool MayTryNext(LocalProviderState state) => state is LocalProviderState.Unsupported or LocalProviderState.InsufficientMemory;
    public static bool MatchesPeriod(RecoveryDocument document, SchoolDataPeriod period) =>
        document.SchoolYear == period.SchoolYear && (document.Kind != RecoveryDocumentKind.Timetable ||
            document.Term == (period.Half == 1 ? "前期" : "後期"));
}
