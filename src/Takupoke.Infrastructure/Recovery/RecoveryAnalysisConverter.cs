using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Materials;
using Takupoke.Infrastructure.Storage;

namespace Takupoke.Infrastructure.Recovery;

public static class RecoveryAnalysisConverter
{
    public static MaterialAnalysis Convert(SourceRecord source, RecoveryDocument document, RecoveryResult result, DateTimeOffset at, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (source.Digest != document.PdfHash || RecoveryPolicy.Kind(source.Kind) != document.Kind || !RecoveryValidator.Validate(document, result, token).CanAdopt)
            throw new InvalidDataException("復旧結果を正式な解析結果へ変換できません。");
        token.ThrowIfCancellationRequested();
        var sources = document.Sources.ToDictionary(s => { token.ThrowIfCancellationRequested(); return s.Id; });
        var raw = new Dictionary<string, string>();
        var recovered = result.Cells.ToDictionary(c => c.CellId);
        string Raw(RecoveryCell c)
        {
            token.ThrowIfCancellationRequested();
            if (!raw.TryGetValue(c.Id, out var value)) raw[c.Id] = value = string.Join("\n", c.SourceIds.Select(id => { token.ThrowIfCancellationRequested(); return sources[id].Text; }));
            return value;
        }
        LessonNames Names(RecoveryLesson l) { token.ThrowIfCancellationRequested(); return new(l.Subject.Value, l.Teacher.Value, l.Room.Value); }
        if (source.Kind == MaterialKind.Timetable)
        {
            var lessons = document.Cells.SelectMany(c => c.Slots.SelectMany(slot => recovered[c.Id].Lessons.Select(l =>
                new NormalLesson(slot.ClassName, int.Parse(slot.Day, System.Globalization.CultureInfo.InvariantCulture), slot.Period, Names(l), Raw(c), c.Page)))).ToArray();
            return new(source.Id, source.Kind, MaterialCoordinator.ParserVersion(source.Kind), source.Digest, source.OriginalName, at, document.SchoolYear,
                Timetable: new(document.SchoolYear, document.Term, lessons));
        }
        static TimeRange Clock(string text) { var p = text.Split('〜'); if (p.Length != 2) throw new InvalidDataException("時刻を確認できません。"); var t = new TimeRange(p[0], p[1]); if (!t.IsValid) throw new InvalidDataException("時刻を確認できません。"); return t; }
        var times = document.Times.ToDictionary(p => p.Key, p => Clock(p.Value));
        var specialLessons = document.Cells.SelectMany(c => c.Slots.SelectMany(slot => recovered[c.Id].Lessons.Select(l => {
            token.ThrowIfCancellationRequested();
            var first = c.Slots.Min(s => s.Period); var last = c.Slots.Max(s => s.Period);
            var key = slot.Day + ":" + (first == last ? first.ToString() : first + "-" + last);
            var recorded = first == last ? times[key] : Clock(document.SpanTimes[key]);
            return new SpecialLesson(slot.Day, slot.ClassName, slot.Period, first, last, recorded, new[] { l.Subject.Value, l.Teacher.Value, l.Room.Value }, c.Page);
        }))).ToArray();
        var firstDay = document.Days.Order().First();
        var periodTimes = Enumerable.Range(1, source.Kind == MaterialKind.Exam ? 6 : 8).ToDictionary(p => p, p => times[firstDay + ":" + p]);
        var special = new SpecialAnalysis(source.Kind, document.SchoolYear, document.Days.Order().ToArray(), document.Classes, periodTimes, specialLessons) { DatePeriodTimes = times };
        return new(source.Id, source.Kind, MaterialCoordinator.ParserVersion(source.Kind), source.Digest, source.OriginalName, at, document.SchoolYear, Special: special);
    }
}
