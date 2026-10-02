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
    private static readonly IReadOnlyDictionary<string, string> CompactKana = Enumerable.Range(0xFF61, 0xFF9F - 0xFF61 + 1)
        .SelectMany(code => new[] { ((char)code).ToString(), ((char)code).ToString() + "ﾞ", ((char)code).ToString() + "ﾟ" })
        .Where(value => value.Length == 1 || value.Normalize(NormalizationForm.FormKC).Length == 1)
        .GroupBy(value => value.Normalize(NormalizationForm.FormKC)).ToDictionary(group => group.Key, group => group.First());
    public static string HalfWidthKana(string value) => string.Concat(value.Select(c => c is >= '゠' and <= 'ヿ'
        ? CompactKana.GetValueOrDefault(c.ToString()) ?? c.ToString() : c.ToString()));
    public static string ChangeCardSubject(string source, string? shortName, Func<string, bool> fits)
    {
        var original = Continuous(source); var compact = HalfWidthKana(original);
        var shortFull = shortName is null ? null : Continuous(shortName);
        var candidates = new[] { original, compact }.Concat(shortFull is null ? [] : new[] { shortFull, HalfWidthKana(shortFull) });
        return candidates.FirstOrDefault(fits) ?? (shortFull is null ? original : HalfWidthKana(shortFull));
    }
    public static string PeriodTime(TimeRange? time) => time is null ? "時刻未確認" : $"{time.Start}\n～\n{time.End}";
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
