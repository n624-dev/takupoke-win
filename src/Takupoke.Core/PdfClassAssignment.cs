using System.Text.RegularExpressions;

namespace Takupoke.Core;

/// Original PDF class keys stay available for provenance and comparisons.
public static class PdfClassAssignment
{
    public const int Version = 1;
    public static IReadOnlyList<string> Departments { get; } = ["1_CN", "1_ES", "1_IT"];
    public static string Destination(string sourceClass, string rawSubject)
    {
        if (!sourceClass.StartsWith("1_", StringComparison.Ordinal)) return sourceClass;
        var matches = new[] { "CN", "ES", "IT" }.Where(department =>
            Regex.IsMatch(rawSubject, "(?<![A-Za-z])" + department + "(?![A-Za-z])", RegexOptions.CultureInvariant)).ToArray();
        return matches.Length == 1 ? "1_" + matches[0] : sourceClass;
    }
    public static NormalLesson Assign(NormalLesson lesson)
    {
        var origin = lesson.SourceClassName ?? lesson.ClassName;
        var target = Destination(origin, lesson.Names.Subject);
        return lesson with { ClassName = target, SourceClassName = target == origin ? null : origin };
    }
    public static SpecialLesson Assign(SpecialLesson lesson)
    {
        var origin = lesson.SourceClassName ?? lesson.ClassName;
        var target = Destination(origin, lesson.Names.Subject);
        return lesson with { ClassName = target, SourceClassName = target == origin ? null : origin };
    }
    public static TimetableAnalysis Assign(TimetableAnalysis analysis) => analysis with { Lessons = analysis.Lessons.Select(Assign).ToArray() };
    public static SpecialAnalysis Assign(SpecialAnalysis analysis) => analysis with
    { Lessons = analysis.Lessons.Select(Assign).ToArray(), ClassAssignmentVersion = Version };
}
