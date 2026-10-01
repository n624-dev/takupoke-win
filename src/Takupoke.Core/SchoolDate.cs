using System.Globalization;

namespace Takupoke.Core;

public static class SchoolDate
{
    public static DateOnly InJapan(DateTimeOffset instant) => DateOnly.FromDateTime(instant.ToOffset(TimeSpan.FromHours(9)).DateTime);
    public static int SchoolYear(this DateOnly day) => day.Month >= 4 ? day.Year : day.Year - 1;
    public static int SchoolWeekday(this DateOnly day) => ((int)day.DayOfWeek + 6) % 7 + 1;
    public static DateOnly Monday(this DateOnly day) => day.AddDays(1 - day.SchoolWeekday());
    public static DateOnly DisplayWeekStart(this DateOnly day) => day.SchoolWeekday() >= 6 ? day.AddDays(8 - day.SchoolWeekday()) : day.Monday();
    public static string Iso(this DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static bool TryParse(string? value, out DateOnly day) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day)
        && value == day.Iso();
}

public readonly record struct SchoolDateRange(DateOnly Start, DateOnly EndExclusive)
{
    public bool Contains(DateOnly day) => Start <= day && day < EndExclusive;
    public bool Overlaps(SchoolDateRange other) => Start < other.EndExclusive && other.Start < EndExclusive;
}
