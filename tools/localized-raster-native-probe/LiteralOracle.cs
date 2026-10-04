using System.Text.Json;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Storage;
internal static class LiteralOracle
{
internal static bool Exact(RecoveryDocument document, MaterialAnalysis formal, JsonElement oracle)
{
    var year = oracle.GetProperty("schoolYear").GetInt32(); var classes = oracle.GetProperty("classes").EnumerateArray().Select(v => v.GetString()!).ToArray(); var days = oracle.GetProperty("days").EnumerateArray().Select(v => v.GetString()!).ToArray();
    if (formal.SchoolYear != year || document.SchoolYear != year || document.RequiredSlots.Count != oracle.GetProperty("requiredSlots").GetInt32() || !classes.ToHashSet().SetEquals(document.Classes) || !days.ToHashSet().SetEquals(document.Days)) return false;
    var expectedSlots = classes.SelectMany(cls => days.SelectMany(day => Enumerable.Range(1, oracle.GetProperty("maxPeriod").GetInt32()).Select(period => (cls, day, period)))).ToHashSet();
    if (!expectedSlots.SetEquals(document.RequiredSlots.Select(slot => (slot.ClassName, slot.Day, slot.Period)))) return false;
    var expected = oracle.GetProperty("lessons").EnumerateArray().Single();
    bool Names(LessonNames names) => names.Subject == expected.GetProperty("subject").GetString() && names.Teacher == expected.GetProperty("teacher").GetString() && names.Room == expected.GetProperty("room").GetString();
    if (formal.Timetable is { } normal) return normal.Term == oracle.GetProperty("term").GetString() && normal.Lessons.Count == 1 && normal.Lessons.Single() is { } lesson && Names(lesson.Names) && lesson.ClassName == expected.GetProperty("class").GetString() && lesson.Weekday.ToString() == expected.GetProperty("day").GetString() && lesson.Period == 1;
    if (formal.Special is not { } special || special.Lessons.Count != 1 || !Names(special.Lessons.Single().Names) || special.Lessons.Single() is not { } entry || entry.ClassName != expected.GetProperty("class").GetString() || entry.Date != expected.GetProperty("day").GetString() || entry.Period != 1 || entry.SpanStart != 1 || entry.SpanEnd != 1) return false;
    var firstClock = oracle.GetProperty("periodClocks").EnumerateArray().Single(clock => clock.GetProperty("period").GetInt32() == 1);
    var displayedClock = special.TimeFor(entry);
    if (entry.RecordedTime is null || entry.RecordedTime.Start != firstClock.GetProperty("start").GetString() || entry.RecordedTime.End != firstClock.GetProperty("end").GetString() || displayedClock != entry.RecordedTime) return false;
    if (!classes.ToHashSet().SetEquals(special.CoveredClasses) || !days.ToHashSet().SetEquals(special.CoveredDates)) return false;
    foreach (var day in days) foreach (var clock in oracle.GetProperty("periodClocks").EnumerateArray())
    {
        var actual = special.PeriodTime(DateOnly.ParseExact(day, "yyyy-MM-dd"), clock.GetProperty("period").GetInt32());
        if (actual is null || actual.Start != clock.GetProperty("start").GetString() || actual.End != clock.GetProperty("end").GetString()) return false;
    }
    return true;
}

}
