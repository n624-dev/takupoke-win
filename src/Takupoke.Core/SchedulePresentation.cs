namespace Takupoke.Core;

/// <summary>Names and originals for native views, without changing persisted source fields.</summary>
public sealed class SchedulePresentation(ScheduleData data, MappingRules? mappings)
{
    public TimetableEngine Engine(UserPreferences preferences, bool home = false) => new(data,
        home || preferences.IncludesChanges, preferences.International,
        mappings is null ? null : (source, cls) => mappings.IsInternational(mappings.SeparateChangeField(source).Subject, cls));

    public LessonNames Names(string cls, ScheduleBlock block, bool home = false) => block.Content switch
    {
        ChangeContent c => ChangeNames(c.Change).After,
        SpecialContent s when !home => s.Names,
        _ => mappings?.Apply(block.Content.Names, cls) ?? block.Content.Names
    };

    public ChangePresentation ChangeNames(ScheduleChange change) => mappings?.Present(change)
        ?? new(new(change.BeforeSubject), new(change.AfterSubject, change.Teacher, change.Room));

    public IReadOnlyList<NormalLesson> NormalOriginals(ScheduleChange change) => OriginalSlots(change).SelectMany(s => s.BaseLessons).ToArray();
    public IReadOnlyList<SpecialContent> SpecialOriginals(ScheduleChange change) => OriginalSlots(change).SelectMany(s => s.SpecialLessons).ToArray();
    private IEnumerable<ScheduleSlot> OriginalSlots(ScheduleChange change)
    {
        if (!SchoolDate.TryParse(change.ChangeDate, out var day)) return [];
        var original = new TimetableEngine(data, false, true);
        return (change.GridPeriods ?? []).Select(p => original.Slot(day, p, change.DisplayClassName));
    }
    public string BeforeSubject(ScheduleChange change, bool detail = false)
    {
        if (!string.IsNullOrEmpty(change.BeforeSubject))
        {
            var names = ChangeNames(change).Before;
            return detail ? names.DetailSubject : names.Subject.Trim();
        }
        var namesFromNormal = NormalOriginals(change).Select(l => l.Names.Subject.Trim()).Where(n => n.Length > 0).Distinct().ToArray();
        return namesFromNormal.Length == 0 ? "記載なし" : string.Join("・", namesFromNormal);
    }
}
