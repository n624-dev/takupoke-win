using System.Text.Json;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Storage;

internal static class IndependentFormalOracle
{
    internal static bool Exact(RecoveryDocument document, MaterialAnalysis formal, JsonElement expected)
    {
        if (!expected.GetProperty("expectedAdopt").GetBoolean() || formal.SchoolYear != expected.GetProperty("schoolYear").GetInt32() || formal.Kind.ToString() != expected.GetProperty("kind").GetString()) return false;
        var expectedSlots = expected.GetProperty("requiredSlotSet").EnumerateArray().Select(v => (v.GetProperty("className").GetString(), v.GetProperty("day").GetString(), v.GetProperty("period").GetInt32())).ToHashSet();
        if (expectedSlots.Count != document.RequiredSlots.Count || !expectedSlots.SetEquals(document.RequiredSlots.Select(s => ((string?)s.ClassName, (string?)s.Day, s.Period)))) return false;
        object? Time(TimeRange? t) => t is null ? null : new { start = t.Start, end = t.End };
        object Names(LessonNames n) => new { subject = n.Subject, teacher = n.Teacher, room = n.Room };
        JsonElement actual;
        JsonElement wanted;
        if (formal.Timetable is { } normal && expected.TryGetProperty("timetable", out wanted))
            actual = JsonSerializer.SerializeToElement(new { term = normal.Term, lessons = normal.Lessons.Select(l => new { className = l.ClassName, weekday = l.Weekday, period = l.Period, names = Names(l.Names) }) });
        else if (formal.Special is { } special && expected.TryGetProperty("special", out wanted))
        {
            // Coverage is a set; do not demand incidental source-array class order.
            var coveredClasses = special.CoveredClasses.Order(StringComparer.Ordinal).ToArray(); var coveredDates = special.CoveredDates.Order(StringComparer.Ordinal).ToArray();
            var expectedClasses = wanted.GetProperty("coveredClasses").EnumerateArray().Select(v => v.GetString()!).Order(StringComparer.Ordinal).ToArray(); var expectedDates = wanted.GetProperty("coveredDates").EnumerateArray().Select(v => v.GetString()!).Order(StringComparer.Ordinal).ToArray();
            if (!coveredClasses.SequenceEqual(expectedClasses) || !coveredDates.SequenceEqual(expectedDates)) return false;
            var periods = document.RequiredSlots.Select(s => s.Period).Distinct().Order().ToArray();
            var clocks = coveredDates.SelectMany(day => periods.Select(period => { var time = special.PeriodTime(DateOnly.ParseExact(day, "yyyy-MM-dd"), period); return new { date = day, period, start = time?.Start, end = time?.End }; })).ToArray();
            actual = JsonSerializer.SerializeToElement(new { coveredClasses, coveredDates, lessons = special.Lessons.Select(l => new { className = l.ClassName, date = l.Date, period = l.Period, spanStart = l.SpanStart, spanEnd = l.SpanEnd, names = Names(l.Names), recordedTime = Time(l.RecordedTime), displayedTime = Time(special.TimeFor(l)) }), datePeriodClocks = clocks });
            wanted = JsonSerializer.SerializeToElement(new { coveredClasses = expectedClasses, coveredDates = expectedDates, lessons = wanted.GetProperty("lessons"), datePeriodClocks = wanted.GetProperty("datePeriodClocks") });
        }
        else return false;
        return JsonElement.DeepEquals(actual, wanted);
    }
}
