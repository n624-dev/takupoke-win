using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Takupoke.Core;

public enum MaterialKind { Timetable, Changes, Exam, ExamReturn }
public enum ChangeRange { Today, Week, All }
public enum EventClassification { None, NoClass, WeekdayOverride, Supplementary, SchoolEventNoClass }

public sealed record LessonNames(string Subject, string Teacher = "", string Room = "",
    string? SubjectFullName = null, string? TeacherFullName = null, string? RoomFullName = null)
{
    public string DetailSubject => FullOrOriginal(SubjectFullName, Subject);
    public string DetailTeacher => FullOrOriginal(TeacherFullName, Teacher);
    public string DetailRoom => FullOrOriginal(RoomFullName, Room);
    private static string FullOrOriginal(string? full, string original) => string.IsNullOrWhiteSpace(full) ? original.Trim() : full.Trim();
}

public sealed record NormalLesson(string ClassName, int Weekday, int Period, LessonNames Names, string SourceText, int Page);
public sealed record TimetableAnalysis(int SchoolYear, string? Term, IReadOnlyList<NormalLesson> Lessons)
{
    public SchoolDateRange? ApplicableRange => Term switch
    {
        "前期" when SchoolYear is >= 1 and < 9999 => new(new(SchoolYear, 4, 1), new(SchoolYear, 10, 1)),
        "後期" when SchoolYear is >= 1 and < 9999 => new(new(SchoolYear, 10, 1), new(SchoolYear + 1, 4, 1)),
        _ => null
    };
}

public sealed record ScheduleChange(string ChangeDate, string ClassName, string Period, string BeforeSubject,
    string AfterSubject, string Teacher, string Room, string Note, string RawText, string CanonicalText)
{
    public string DisplayClassName => ClassSelection.Canonical(ClassName);
    public bool IsCancellation => Note.Normalize(NormalizationForm.FormKC).Trim() == "休講";
    public bool IsMakeup => Note.Normalize(NormalizationForm.FormKC).Trim() == "補講";
    public string KindLabel => IsCancellation ? "休講" : IsMakeup ? "補講" : "変更";
    public string DisplayPeriod => string.IsNullOrWhiteSpace(Period) ? "記載なし" : Regex.IsMatch(Period.Trim(), "^[0-9]+$") ? Period.Trim() + "限" : Period;
    public IReadOnlyList<int>? DetailPeriods
    {
        get
        {
            var value = Regex.Replace(Period.Normalize(NormalizationForm.FormKC).Replace('〜', '~'), @"\s", "");
            if (value.Contains('~') && !value.Contains(','))
            {
                var bounds = value.Split('~');
                if (bounds.Length != 2 || !int.TryParse(bounds[0], out var first) || !int.TryParse(bounds[1], out var last)
                    || first < 1 || last > 8 || first >= last) return null;
                return Enumerable.Range(first, last - first + 1).ToArray();
            }
            var pieces = value.Split(',');
            var result = new List<int>();
            foreach (var piece in pieces)
            {
                if (!int.TryParse(piece, NumberStyles.Integer, CultureInfo.InvariantCulture, out var period)
                    || period is < 1 or > 8 || result.Contains(period)) return null;
                result.Add(period);
            }
            return result;
        }
    }
    public IReadOnlyList<int>? GridPeriods
    {
        get
        {
            var periods = DetailPeriods;
            return periods is not null && periods.Zip(periods.Skip(1)).All(pair => pair.Second == pair.First + 1) ? periods : null;
        }
    }
}

public sealed record SpecialLesson(string Date, string ClassName, int Period, int SpanStart, int SpanEnd,
    TimeRange? RecordedTime, IReadOnlyList<string> Lines, int Page)
{
    public LessonNames Names => new(Lines.ElementAtOrDefault(0) ?? "", Lines.ElementAtOrDefault(1) ?? "", Lines.ElementAtOrDefault(2) ?? "");
}

public sealed record SpecialAnalysis(MaterialKind Kind, int SchoolYear, IReadOnlyList<string> CoveredDates,
    IReadOnlyList<string> CoveredClasses, IReadOnlyDictionary<int, TimeRange> PeriodTimes, IReadOnlyList<SpecialLesson> Lessons)
{
    public bool Applies(DateOnly day, string className) => CoveredDates.Contains(day.Iso()) && CoveredClasses.Contains(className);
    public TimeRange? PeriodTime(DateOnly day, int period)
    {
        if (!CoveredDates.Contains(day.Iso())) return null;
        if (Kind == MaterialKind.ExamReturn && day.Iso() != CoveredDates.FirstOrDefault()) return ScheduleTimes.Normal.GetValueOrDefault(period);
        return PeriodTimes.GetValueOrDefault(period);
    }
    public TimeRange? TimeFor(SpecialLesson lesson)
    {
        if (!SchoolDate.TryParse(lesson.Date, out var day) || !Applies(day, lesson.ClassName)) return null;
        if ((Kind != MaterialKind.ExamReturn || lesson.Date == CoveredDates.FirstOrDefault()) && lesson.RecordedTime is not null) return lesson.RecordedTime;
        var start = PeriodTime(day, lesson.SpanStart);
        var end = PeriodTime(day, lesson.SpanEnd);
        return start is not null && end is not null ? new(start.Start, end.End) : null;
    }
}

public sealed record SchoolEvent(string Date, string Title, string Tag, string? EndDate = null,
    EventClassification Classification = EventClassification.None, int? ScheduleDay = null, bool NeedsReview = false)
{
    public bool Applies(DateOnly day)
    {
        if (!SchoolDate.TryParse(Date, out var start)) return false;
        if (start == day) return true;
        return !NeedsReview && SchoolDate.TryParse(EndDate, out var end) && start < day && day <= end;
    }
}

public sealed record ScheduleData(TimetableAnalysis? Timetable = null, IReadOnlyList<ScheduleChange>? Changes = null,
    IReadOnlyList<SpecialAnalysis>? Specials = null, IReadOnlyList<SchoolEvent>? Events = null, ScheduleTimes? Times = null);
