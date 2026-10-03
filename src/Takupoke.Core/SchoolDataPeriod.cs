namespace Takupoke.Core;

/// <summary>The school-data retention period, determined from the actual instant in Japan.</summary>
public readonly record struct SchoolDataPeriod
{
    [System.Text.Json.Serialization.JsonConstructor]
    public SchoolDataPeriod(int schoolYear, int half)
    {
        if (schoolYear is < 1900 or > 9998 || half is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(schoolYear));
        SchoolYear = schoolYear;
        Half = half;
    }

    public int SchoolYear { get; }
    public int Half { get; }

    public static SchoolDataPeriod FromInstant(DateTimeOffset instant)
    {
        var japanDate = instant.ToOffset(TimeSpan.FromHours(9));
        var schoolYear = japanDate.Month < 4 ? japanDate.Year - 1 : japanDate.Year;
        var half = japanDate.Month is >= 4 and <= 9 ? 1 : 2;
        return new SchoolDataPeriod(schoolYear, half);
    }
}
