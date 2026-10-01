using System.Globalization;

namespace Takupoke.Core;

public sealed record TimeRange(string Start, string End)
{
    public string Display => Start + "～" + End;
    public bool IsValid => TryMinutes(Start, out var start) && TryMinutes(End, out var end) && start < end;
    public static bool TryMinutes(string value, out int minutes)
    {
        minutes = 0;
        if (value.Length != 5 || value[2] != ':' || value.Where((_, index) => index != 2).Any(c => c is < '0' or > '9')) return false;
        var hour = int.Parse(value[..2], CultureInfo.InvariantCulture);
        var minute = int.Parse(value[3..], CultureInfo.InvariantCulture);
        if (hour >= 24 || minute >= 60) return false;
        minutes = hour * 60 + minute;
        return true;
    }
    public static TimeRange? Parse(string value)
    {
        var pieces = value.Replace('〜', '~').Replace('～', '~').Split('~');
        if (pieces.Length != 2) return null;
        var range = new TimeRange(pieces[0].Trim(), pieces[1].Trim());
        return range.IsValid ? range : null;
    }
}

public sealed record DayTimes(string Date, IReadOnlyList<PeriodTime> Periods);
public sealed record PeriodTime(int Period, string Start, string End);
public sealed record ScheduleTimes(int SchemaVersion, IReadOnlyList<DayTimes> Days)
{
    public static IReadOnlyDictionary<int, TimeRange> Normal { get; } = new Dictionary<int, TimeRange>
    {
        [1] = new("08:50", "09:35"), [2] = new("09:35", "10:20"),
        [3] = new("10:30", "11:15"), [4] = new("11:15", "12:00"),
        [5] = new("12:50", "13:35"), [6] = new("13:35", "14:20"),
        [7] = new("14:30", "15:15"), [8] = new("15:15", "16:00")
    };
    public ScheduleTimes Validated()
    {
        if (SchemaVersion != 1 || Days.Count > 400 || Days.Select(d => d.Date).Distinct().Count() != Days.Count) throw new InvalidDataException("授業時刻の形式を確認できませんでした。");
        foreach (var day in Days)
        {
            if (!SchoolDate.TryParse(day.Date, out _) || day.Periods.Count != 8) throw new InvalidDataException("授業時刻の日付または時限数が不正です。");
            var previousEnd = "00:00";
            for (var index = 0; index < 8; index++)
            {
                var slot = day.Periods[index];
                if (slot.Period != index + 1 || !new TimeRange(slot.Start, slot.End).IsValid || string.CompareOrdinal(slot.Start, previousEnd) < 0)
                    throw new InvalidDataException("授業時刻の順序を確認できませんでした。");
                previousEnd = slot.End;
            }
        }
        return this;
    }
    public TimeRange? Time(DateOnly day, int period)
    {
        var slot = Days.FirstOrDefault(d => d.Date == day.Iso())?.Periods.FirstOrDefault(p => p.Period == period);
        return slot is null ? null : new(slot.Start, slot.End);
    }
}
