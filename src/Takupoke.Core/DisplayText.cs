using System.Text;
using System.Text.RegularExpressions;

namespace Takupoke.Core;

public static class DisplayText
{
    public static string Continuous(string value) => FullWidthKana(value.Replace("\r", "").Replace("\n", ""));
    public static string FullWidthKana(string value)
    {
        return Regex.Replace(value, "[\uFF61-\uFF9F]+", match => match.Value.Normalize(NormalizationForm.FormKC));
    }
    public static string CellSubject(string value) => Continuous(value).Replace('・', '•');
    public static string Metadata(string value) => Regex.Replace(Regex.Replace(value, "[()（）]", ""), @"\s{2,}", " ").Trim();
    public static bool IsInternational(string value) => value.Normalize(NormalizationForm.FormKC).Trim().StartsWith('留');
    public static string Accessibility(LessonNames names, TimeRange? time, string? kind = null)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(kind)) parts.Add(kind);
        if (names.DetailSubject.Length > 0) parts.Add("科目、" + Continuous(names.DetailSubject));
        parts.Add(time is null ? "時刻未確認" : "時刻、" + time.Display);
        if (names.DetailTeacher.Length > 0) parts.Add("教員、" + Continuous(names.DetailTeacher));
        if (names.DetailRoom.Length > 0) parts.Add("教室、" + Continuous(names.DetailRoom));
        return string.Join("、", parts);
    }
}
