using System.Text;
using System.Text.RegularExpressions;

namespace Takupoke.Core.Recovery;

// One original-label vocabulary for preprocessing, validation, and Strict refusal.
public static class RecoveryRoleLabels
{
    private static readonly IReadOnlyList<string> Subject = Array.AsReadOnly(new[] { "科目", "授業科目", "科目名", "授業名" });
    private static readonly IReadOnlyList<string> Teacher = Array.AsReadOnly(new[] { "教員", "担当", "担当教員", "教師", "教員名", "教師名", "担当者" });
    private static readonly IReadOnlyList<string> Room = Array.AsReadOnly(new[] { "教室", "場所", "授業教室", "教室名", "会場" });
    public static IReadOnlyList<string> For(RecoveryFieldRole role) => role switch { RecoveryFieldRole.Subject => Subject, RecoveryFieldRole.Teacher => Teacher, RecoveryFieldRole.Room => Room, _ => [] };
    public static string Pattern { get; } = string.Join("|", Subject.Concat(Teacher).Concat(Room).OrderByDescending(s => s.Length).Select(Regex.Escape));
    private static readonly Regex Prefix = new("(?:^|[・/])(?:" + Pattern + "):", RegexOptions.CultureInvariant);
    public static bool HasPrefix(string text) => Prefix.IsMatch(Regex.Replace(text.Normalize(NormalizationForm.FormKC), @"\s+", ""));
}
