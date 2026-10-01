using System.Text.RegularExpressions;

namespace Takupoke.Core;

public static class ClassSelection
{
    public static IReadOnlyList<string> Candidates { get; } =
        new[] { "1_1", "1_2", "1_3" }
        .Concat(Enumerable.Range(1, 5).SelectMany(year => new[] { "CN", "ES", "IT" }.Select(name => $"{year}_{name}")))
        .Concat(new[] { "AI_1", "AI_2" }).ToArray();

    public static string Canonical(string value) => Regex.Replace(value, "^([0-9]+)_AI$", match =>
        int.TryParse(match.Groups[1].Value, out var year) && year is >= 1 and <= 9 ? "AI_" + match.Groups[1].Value : match.Value);
    public static string Display(string value) => DisplayText.FullWidthKana(value).Replace('_', '-');
    public static bool Compatible(string primary, string additional) =>
        (IsHomeroom(primary) && IsDepartment(additional)) || (IsDepartment(primary) && IsHomeroom(additional));
    public static bool IsValid(IReadOnlyList<string> values) => values.Count switch
    {
        0 => true,
        1 => Candidates.Contains(values[0], StringComparer.Ordinal),
        2 => Compatible(values[0], values[1]),
        _ => false
    };
    private static bool IsHomeroom(string value) => value is "1_1" or "1_2" or "1_3";
    private static bool IsDepartment(string value) => value is "1_CN" or "1_ES" or "1_IT";
}
