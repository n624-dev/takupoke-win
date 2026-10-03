using System.Text.RegularExpressions;
using Takupoke.Core;

namespace Takupoke.Infrastructure.Parsing;

public static partial class PdfScheduleParser
{
    private sealed record ParsedSpecialPage(IReadOnlyList<string> Dates, IReadOnlyList<string> Classes, IReadOnlyList<SpecialLesson> Lessons);
    public static SpecialAnalysis Special(IReadOnlyList<PdfPageLayout> pages, MaterialKind kind, CancellationToken token = default)
    {
        if (kind is not MaterialKind.Exam and not MaterialKind.ExamReturn || pages.Count != (kind == MaterialKind.Exam ? 6 : 1)) throw new PdfParseException("P04");
        int? year = null; string[]? expectedDates = null; PdfTimes? expectedTimes = null;
        var classes = new HashSet<string>(); var lessons = new List<SpecialLesson>();
        for (var index = 0; index < pages.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var page = pages[index]; page.Validate(index + 1);
            var heading = Heading(page, 0.25); var currentYear = Year(heading, index + 1);
            if (!heading.Contains("試験") || heading.Contains("返却") != (kind == MaterialKind.ExamReturn)) throw new PdfParseException("P04", index + 1);
            if (year is not null && year != currentYear) throw new PdfParseException("P03", index + 1);
            year = currentYear;
            var times = ReadTimes(page, kind == MaterialKind.Exam ? 6 : 8);
            if (expectedTimes is not null && !expectedTimes.Same(times)) throw new PdfParseException("P05", index + 1);
            expectedTimes = times;
            var parsed = kind == MaterialKind.Exam ? ExamPage(page, currentYear, index + 1, times, token) : ReturnPage(page, currentYear, times, token);
            var dates = parsed.Dates.Order(StringComparer.Ordinal).ToArray();
            if (expectedDates is not null && !expectedDates.SequenceEqual(dates)) throw new PdfParseException("P10", index + 1);
            expectedDates = dates;
            if (parsed.Classes.Any(classes.Contains)) throw new PdfParseException("P16", index + 1);
            classes.UnionWith(parsed.Classes); lessons.AddRange(parsed.Lessons);
            if (lessons.Count > MaximumRecords) throw new PdfParseException("limit", index + 1);
        }
        if (year is null || expectedDates is null || expectedTimes is null || !classes.SetEquals(Takupoke.Core.Recovery.RecoveryValidator.SpecialClasses) || lessons.Count == 0) throw new PdfParseException("P04");
        return new(kind, year.Value, expectedDates, classes.Order(StringComparer.Ordinal).ToArray(), expectedTimes.Single,
            lessons.OrderBy(l => l.Date, StringComparer.Ordinal).ThenBy(l => l.ClassName, StringComparer.Ordinal).ThenBy(l => l.Period).ThenBy(l => l.Page).ToArray());
    }
    private static ParsedSpecialPage ExamPage(PdfPageLayout page, int year, int number, PdfTimes times, CancellationToken token)
    {
        var columns = number == 6 ? 2 : 3; var periods = PeriodHeader(page, "123456", columns, 0.25); var headerY = periods[0].Cy;
        var labels = Runs(page.Glyphs.Where(g => g.Cy < headerY - 3 && g.Cy > headerY - page.Height / 10))
            .Where(run => Regex.IsMatch(run.Text, number == 6 ? "^[12]年$" : "^[1-5]-(?:[1-3]|[A-Z]{2})$")).OrderBy(run => run.Cx).ToArray();
        if (labels.Length != columns) throw new PdfParseException("P14", number);
        var names = labels.Select(run => number == 6 ? "AI_" + run.Text[..1] : run.Text.Replace('-', '_')).ToArray();
        var dates = Runs(page.Glyphs.Where(g => g.Cy > headerY + 5 && g.Cy < page.Height * 0.7))
            .Where(run => run.Cx < periods[0].Cx).Select(run => (Run: run, Day: Date(run.Text, year, false)))
            .Where(p => p.Day is not null).OrderBy(p => p.Run.Cy).ToArray();
        if (dates.Length != 5 || dates.Select(p => p.Day).Distinct().Count() != 5) throw new PdfParseException("P10", number);
        var grid = new PdfGrid(page); var lessons = new List<SpecialLesson>();
        var firstBox = grid.Box(periods[0].Cx, dates[0].Run.Cy); var lastBox = grid.Box(periods[^1].Cx, dates[^1].Run.Cy);
        grid.SetLessonArea(new(firstBox.Left, firstBox.Top, lastBox.Right, lastBox.Bottom));
        var lessonRows = dates.Select(pair => { token.ThrowIfCancellationRequested(); return grid.Box(pair.Run.Cx, pair.Run.Cy); }).ToArray();
        grid.SetLessonCells(lessonRows.SelectMany(row => periods.SelectMany(period => grid.Slices(row, period.Cx))));
        foreach (var (pair, row) in dates.Zip(lessonRows))
        {
            for (var column = 0; column < names.Length; column++)
            {
                var xs = Enumerable.Range(0, 6).Select(i => periods[column * 6 + i].Cx).ToArray();
                for (var period = 1; period <= 6; period++)
                    foreach (var box in grid.Slices(row, xs[period - 1]))
                    {
                        token.ThrowIfCancellationRequested();
                        if (SpecialCell(grid, box, pair.Day!.Value, names[column], period, xs, times, number) is { } lesson) lessons.Add(lesson);
                    }
            }
        }
        return new(dates.Select(p => p.Day!.Value.Iso()).ToArray(), names, lessons);
    }
    private static ParsedSpecialPage ReturnPage(PdfPageLayout page, int year, PdfTimes times, CancellationToken token)
    {
        var periods = PeriodHeader(page, "12345678", 5, 0.25); var headerY = periods[0].Cy; var step = periods[1].Cx - periods[0].Cx;
        if (step <= 5) throw new PdfParseException("P05", 1);
        var dates = Runs(page.Glyphs.Where(g => g.Cy < headerY && g.Cy > headerY - page.Height / 20))
            .Select(run => (Run: run, Day: Date(run.Text, year, true))).Where(p => p.Day is not null).OrderBy(p => p.Run.Cx).ToArray();
        if (dates.Length != 5 || dates.Select(p => p.Day).Distinct().Count() != 5) throw new PdfParseException("P10", 1);
        if (!dates.Select(p => p.Day!.Value).SequenceEqual(dates.Select(p => p.Day!.Value).Order())) throw new PdfParseException("P10", 1);
        var specialDay = dates[0].Day!.Value; var ordinaryStart = dates[1].Day!.Value; var ordinaryEnd = dates[^1].Day!.Value;
        var note = PdfGrid.Key(string.Concat(PdfGrid.Rows(page.Glyphs).Select(Joined))).Replace('～', '~').Replace('〜', '~');
        if (ordinaryStart.Month != ordinaryEnd.Month || !note.Contains($"{specialDay.Month}月{specialDay.Day}日の時間割は以下のとおり")
            || !note.Contains($"{ordinaryStart.Month}月{ordinaryStart.Day}日~{ordinaryEnd.Day}日は通常の授業日どおりの授業時間")) throw new PdfParseException("P05", 1);
        var ordinary = new PdfTimes(ScheduleTimes.Normal, new Dictionary<string, TimeRange>());
        var left = Runs(page.Glyphs.Where(g => g.Cx < periods[0].Cx - step * 0.15 && g.Cy > headerY + 5 && g.Cy < page.Height * 0.7));
        var gradeMax = periods[0].Cx - step * 0.8;
        var grades = left.Where(run => run.Cx < gradeMax && Regex.IsMatch(run.Text, "^(?:[1-5]|AI)$")).ToArray();
        var classRuns = left.Where(run => run.Cx >= gradeMax && Regex.IsMatch(run.Text, "^(?:[1-3]|CN|ES|IT)$")).OrderBy(run => run.Cy).ToArray();
        if (grades.Length != 6 || classRuns.Length != 17) throw new PdfParseException("P14", 1);
        var classes = new HashSet<string>(); var lessons = new List<SpecialLesson>(); var grid = new PdfGrid(page);
        var firstBox = grid.Box(periods[0].Cx, classRuns[0].Cy); var lastBox = grid.Box(periods[^1].Cx, classRuns[^1].Cy);
        grid.SetLessonArea(new(firstBox.Left, firstBox.Top, lastBox.Right, lastBox.Bottom));
        var lessonRows = classRuns.Select(run => { token.ThrowIfCancellationRequested(); return grid.Box(run.Cx, run.Cy); }).ToArray();
        grid.SetLessonCells(lessonRows.SelectMany(row => periods.SelectMany(period => grid.Slices(row, period.Cx))));
        foreach (var (run, row) in classRuns.Zip(lessonRows))
        {
            var grade = grades.MinBy(g => Math.Abs(g.Cy - run.Cy))!;
            if (Math.Abs(grade.Cy - run.Cy) >= step * 2.5) throw new PdfParseException("P15", 1);
            var name = grade.Text == "AI" ? "AI_" + run.Text : grade.Text + "_" + run.Text;
            if (!classes.Add(name)) throw new PdfParseException("P16", 1);
            for (var dayIndex = 0; dayIndex < dates.Length; dayIndex++)
            {
                var dayTimes = dayIndex == 0 ? times : ordinary; var day = dates[dayIndex].Day!.Value;
                var xs = Enumerable.Range(0, 8).Select(i => periods[dayIndex * 8 + i].Cx).ToArray();
                for (var period = 1; period <= 8; period++)
                    foreach (var box in grid.Slices(row, xs[period - 1]))
                    {
                        token.ThrowIfCancellationRequested();
                        if (SpecialCell(grid, box, day, name, period, xs, dayTimes, 1) is { } lesson) lessons.Add(lesson);
                    }
            }
        }
        return new(dates.Select(p => p.Day!.Value.Iso()).ToArray(), classes.Order(StringComparer.Ordinal).ToArray(), lessons);
    }
}
