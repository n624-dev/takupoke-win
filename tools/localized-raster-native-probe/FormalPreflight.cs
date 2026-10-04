using System.Text.Json;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Storage;

internal static class FormalPreflight
{
    internal static object Run(string holdoutRoot)
    {
        var checks = 0; var positives = 0;
        foreach (var path in Directory.GetFiles(holdoutRoot, "literal-analysis-oracle.json", SearchOption.AllDirectories))
        {
            using var expected = JsonDocument.Parse(File.ReadAllBytes(path)); var e = expected.RootElement;
            if (!e.GetProperty("expectedAdopt").GetBoolean()) continue;
            var kind = Enum.Parse<MaterialKind>(e.GetProperty("kind").GetString()!); var year = e.GetProperty("schoolYear").GetInt32();
            var slots = e.GetProperty("requiredSlotSet").EnumerateArray().Select(s => new RecoverySlot(s.GetProperty("className").GetString()!, s.GetProperty("day").GetString()!, s.GetProperty("period").GetInt32())).ToArray();
            var classes = slots.Select(s => s.ClassName).Distinct().ToArray(); var days = slots.Select(s => s.Day).Distinct().ToArray();
            var doc = new RecoveryDocument("fictional", kind == MaterialKind.Timetable ? RecoveryDocumentKind.Timetable : kind == MaterialKind.Exam ? RecoveryDocumentKind.Exam : RecoveryDocumentKind.Return, year, null, classes, days, slots, [], [], true, [], [], new Dictionary<string,IReadOnlyList<string>>(), new Dictionary<string,IReadOnlyList<string>>(), new Dictionary<string,IReadOnlyList<string>>(), new Dictionary<string,string>(), [], []);
            MaterialAnalysis formal;
            if (kind == MaterialKind.Timetable)
            {
                var normal = e.GetProperty("timetable");
                var lessons = normal.GetProperty("lessons").EnumerateArray().Select(l => { var n = l.GetProperty("names"); return new NormalLesson(l.GetProperty("className").GetString()!, l.GetProperty("weekday").GetInt32(), l.GetProperty("period").GetInt32(), new(n.GetProperty("subject").GetString()!, n.GetProperty("teacher").GetString()!, n.GetProperty("room").GetString()!), "fictional scorer control", 1); }).ToArray();
                formal = new("fictional", kind, 1, "fictional", "fictional", DateTimeOffset.UnixEpoch, year, Timetable: new(year, normal.GetProperty("term").GetString(), lessons));
            }
            else
            {
                var special = e.GetProperty("special"); var dates = special.GetProperty("coveredDates").EnumerateArray().Select(v => v.GetString()!).ToArray(); var covered = special.GetProperty("coveredClasses").EnumerateArray().Select(v => v.GetString()!).ToArray();
                var lessons = special.GetProperty("lessons").EnumerateArray().Select(l => { var n = l.GetProperty("names"); var t = l.GetProperty("recordedTime"); return new SpecialLesson(l.GetProperty("date").GetString()!, l.GetProperty("className").GetString()!, l.GetProperty("period").GetInt32(), l.GetProperty("spanStart").GetInt32(), l.GetProperty("spanEnd").GetInt32(), new(t.GetProperty("start").GetString()!,t.GetProperty("end").GetString()!), [n.GetProperty("subject").GetString()!,n.GetProperty("teacher").GetString()!,n.GetProperty("room").GetString()!],1); }).ToArray();
                var times = special.GetProperty("datePeriodClocks").EnumerateArray().ToDictionary(t => t.GetProperty("date").GetString() + ":" + t.GetProperty("period").GetInt32(), t => new TimeRange(t.GetProperty("start").GetString()!,t.GetProperty("end").GetString()!));
                formal = new("fictional", kind, 1, "fictional", "fictional", DateTimeOffset.UnixEpoch, year, Special: new(kind, year, dates, covered, new Dictionary<int,TimeRange>(), lessons) { DatePeriodTimes = times });
            }
            void Check(bool ok) { if (!ok) throw new InvalidDataException("Independent formal oracle scorer boundary mismatch: " + path); checks++; }
            Check(IndependentFormalOracle.Exact(doc, formal, e)); positives++;
            Check(!IndependentFormalOracle.Exact(doc with { RequiredSlots = slots.Skip(1).ToArray() }, formal, e));
            Check(!IndependentFormalOracle.Exact(doc, formal with { SchoolYear = year + 1 }, e));
            if (formal.Timetable is { } n)
            {
                var l = n.Lessons.Single(); Check(!IndependentFormalOracle.Exact(doc, formal with { Timetable = n with { Lessons = [l with { Names = l.Names with { Teacher = "wrong" } }] } }, e));
                Check(!IndependentFormalOracle.Exact(doc, formal with { Timetable = n with { Lessons = [l, l] } }, e));
            }
            else if (formal.Special is { } s)
            {
                var l = s.Lessons.Single(); Check(!IndependentFormalOracle.Exact(doc, formal with { Special = s with { Lessons = [l with { Lines = [l.Names.Subject, "wrong", l.Names.Room] }] } }, e));
                Check(!IndependentFormalOracle.Exact(doc, formal with { Special = s with { DatePeriodTimes = s.DatePeriodTimes!.Where(t => t.Key != s.DatePeriodTimes!.Keys.First()).ToDictionary() } }, e));
                var clockKey = s.DatePeriodTimes!.Keys.Last();
                var wrongClocks = s.DatePeriodTimes!.ToDictionary(); wrongClocks[clockKey] = new("00:00", "00:01");
                Check(!IndependentFormalOracle.Exact(doc, formal with { Special = s with { DatePeriodTimes = wrongClocks } }, e));
                Check(!IndependentFormalOracle.Exact(doc, formal with { Special = s with { Lessons = [l with { RecordedTime = new("00:00", "00:01") }] } }, e));
                Check(!IndependentFormalOracle.Exact(doc, formal with { Special = s with { CoveredDates = s.CoveredDates.Skip(1).ToArray() } }, e));
            }
        }
        if (positives != 5) throw new InvalidDataException("Five independent positive formal oracles required.");
        return new { scorerChecks = checks, positiveOracleShapes = positives, nativeCalls = 0, scope = "Synthetic semantic scorer controls made from immutable independent gold; never OCR or quality evidence" };
    }
}
