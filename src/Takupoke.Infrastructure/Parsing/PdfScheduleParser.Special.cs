using System.Text.RegularExpressions;
using Takupoke.Core;

namespace Takupoke.Infrastructure.Parsing;

public static partial class PdfScheduleParser
{
    private sealed record ParsedSpecialPage(IReadOnlyList<string> Dates, IReadOnlyList<string> Classes, IReadOnlyList<SpecialLesson> Lessons);
    public static SpecialAnalysis Special(IReadOnlyList<PdfPageLayout> pages, MaterialKind kind, CancellationToken token = default,
        IReadOnlySet<int>? ocrPages = null,bool allowNativeTwoRowExam = false, bool ordinary = false, ReturnGeometryRecorder? geometry = null)
    {
        if (kind is not MaterialKind.Exam and not MaterialKind.ExamReturn || pages.Count != (kind == MaterialKind.Exam ? 6 : 1)) throw new PdfParseException("P04", branch:"R1135");
        int? year = null; string[]? expectedDates = null; PdfTimes? expectedTimes = null;
        var classes = new HashSet<string>(); var lessons = new List<SpecialLesson>();
        for (var index = 0; index < pages.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var page = pages[index]; page.Validate(index + 1);
            var rawHeading = Heading(page, 0.25); var currentYear = Year(rawHeading, index + 1); var heading = PdfGrid.Key(rawHeading);
            if (!heading.Contains("試験") || heading.Contains("返却") != (kind == MaterialKind.ExamReturn)) throw new PdfParseException("P04", index + 1, branch:"R1136");
            if (year is not null && year != currentYear) throw new PdfParseException("P03", index + 1, branch:"R1137");
            year = currentYear;
            var times = ReadTimes(page, kind == MaterialKind.Exam ? 6 : 8);
            if (expectedTimes is not null && !expectedTimes.Same(times)) throw new PdfParseException("P05", index + 1, branch:"R1138");
            expectedTimes = times;
            var fromOcr = ocrPages?.Contains(index + 1) == true;
            var parsed = kind == MaterialKind.Exam ? ExamPage(page, currentYear, index + 1, times, token, fromOcr,allowNativeTwoRowExam, ordinary) : ReturnPage(page, currentYear, times, token, fromOcr, ordinary, geometry);
            var dates = parsed.Dates.Order(StringComparer.Ordinal).ToArray();
            if (expectedDates is not null && !expectedDates.SequenceEqual(dates)) throw new PdfParseException("P10", index + 1, branch:"R1139");
            expectedDates = dates;
            if (parsed.Classes.Any(classes.Contains)) throw new PdfParseException("P16", index + 1, branch:"R1140");
            classes.UnionWith(parsed.Classes); lessons.AddRange(parsed.Lessons);
            if (lessons.Count > MaximumRecords) throw new PdfParseException("limit", index + 1, branch:"R1141");
        }
        if (year is null || expectedDates is null || expectedTimes is null || (ordinary ? classes.Count != 17 : !classes.SetEquals(Takupoke.Core.Recovery.RecoveryValidator.SpecialClasses)) || lessons.Count == 0) throw new PdfParseException("P04", branch:"R1142");
        return new(kind, year.Value, expectedDates, classes.Order(StringComparer.Ordinal).ToArray(), expectedTimes.Single,
            lessons.OrderBy(l => l.Date, StringComparer.Ordinal).ThenBy(l => l.ClassName, StringComparer.Ordinal).ThenBy(l => l.Period).ThenBy(l => l.Page).ToArray());
    }
    private static ParsedSpecialPage ExamPage(PdfPageLayout page, int year, int number, PdfTimes times, CancellationToken token, bool fromOcr,bool allowNativeTwoRowExam = false, bool ordinary = false)
    {
        var columns = number == 6 ? 2 : 3; var periods = PeriodHeader(page, "123456", columns, 0.25); var headerY = periods[0].Cy;
        var names = ExamClasses(page, periods, number, fromOcr, token);
        var dates = Runs(page, page.Glyphs.Where(g => g.Cy > headerY + 5 && g.Cy < page.Height * 0.7), fromOcr)
            .Where(run => run.Cx < periods[0].Cx).Select(run => (Run: run, Day: Date(run.Text, year, false)))
            .Where(p => p.Day is not null).OrderBy(p => p.Run.Cy).ToArray();
        if (dates.Length != 5 || dates.Select(p => p.Day).Distinct().Count() != 5) throw new PdfParseException("P10", number, branch:"R1143");
        var grid = new PdfGrid(page, token); var lessons = new List<SpecialLesson>();
        var firstBox = grid.Box(periods[0].Cx, dates[0].Run.Cy); var lastBox = grid.Box(periods[^1].Cx, dates[^1].Run.Cy);
        grid.SetLessonArea(new(firstBox.Left, firstBox.Top, lastBox.Right, lastBox.Bottom));
        var lessonRows = dates.Select(pair => {
            token.ThrowIfCancellationRequested(); var row=grid.Box(pair.Run.Cx,pair.Run.Cy);
            return row with { Top=Math.Max(row.Top,grid.Box(periods[0].Cx,pair.Run.Cy).Top) };
        }).ToArray();
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
                        if (SpecialCell(grid, box, pair.Day!.Value, names[column], period, xs, times, number,allowNativeTwoRowExam, ordinary) is { } lesson) lessons.Add(lesson);
                    }
            }
        }
        return new(dates.Select(p => p.Day!.Value.Iso()).ToArray(), names, lessons);
    }
    private static string[] ExamClasses(PdfPageLayout page, IReadOnlyList<PdfGlyph> periods,
        int number, bool fromOcr, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var headerY = periods[0].Cy;
        var candidates = page.Glyphs.Where(g => g.Cy < headerY - 3 && g.Cy > headerY - page.Height / 10).ToArray();
        var runs = Runs(page, candidates, fromOcr);
        var labels = runs.Where(run => Regex.IsMatch(run.Text, number == 6 ? "^[12]年$" : "^[1-5]-(?:[1-3]|[A-Z]{2})$"))
            .Select(run => (Name: number == 6 ? "AI_" + run.Text[..1] : run.Text.Replace('-', '_'), Cx: run.Cx)).ToList();
        var hasComposite = false;
        if (fromOcr && number == 6)
        {
            long work = 0;
            void Charge(long amount)
            {
                token.ThrowIfCancellationRequested(); work += amount;
                if (work > 20_000_000) throw new PdfParseException("limit", number, branch:"R1144");
            }
            Charge(page.Glyphs.Count * 2L + candidates.Length);
            var selected = new HashSet<PdfGlyph>(candidates, ReferenceEqualityComparer.Instance);
            var nativeRows = PdfGrid.OcrHeaderRows(page, candidates, Charge).ToArray();
            var proven = new HashSet<PdfGlyph>(nativeRows.SelectMany(r => r), ReferenceEqualityComparer.Instance);
            var grid = new PdfGrid(page, token);
            // A legacy grade prefix is usable only when it is the entire proven
            // native row and the entire physical header owner's ink. Losing ALL
            // canonical suffix metadata must not turn a composite into a grade.
            foreach (var grade in runs.Where(r => Regex.IsMatch(r.Text, "^[12]年$")))
            {
                Charge(nativeRows.Sum(r => r.Count * 2L + r.Sum(g => (long)g.Text.Length)));
                var rows = nativeRows.Where(r => PdfGrid.Key(string.Concat(r.Select(g => g.Text))) == grade.Text
                    && new PdfBox(r.Min(g => g.X), r.Min(g => g.Y), r.Max(g => g.X + g.Width), r.Max(g => g.Y + g.Height)) == grade.Box).ToArray();
                if (rows.Length != 1) throw new PdfParseException("P14", number, branch:"R1145");
                try
                {
                    var owner = grid.Box(grade.Cx, grade.Cy);
                    var row = new HashSet<PdfGlyph>(rows[0], ReferenceEqualityComparer.Instance);
                    Charge(page.Glyphs.Count * 4L + rows[0].Count * 4L);
                    if (rows[0].Any(g => grid.Box(g.Cx, g.Cy) != owner
                            || g.X <= owner.Left || g.X + g.Width >= owner.Right || g.Y <= owner.Top || g.Y + g.Height >= owner.Bottom)
                        || page.Glyphs.Any(g => !row.Contains(g)
                            && Math.Max(owner.Left, g.X) < Math.Min(owner.Right, g.X + g.Width)
                            && Math.Max(owner.Top, g.Y) < Math.Min(owner.Bottom, g.Y + g.Height)))
                        throw new PdfParseException("P14", number, branch:"R1146");
                }
                catch (PdfParseException error) when (error.Stage is not ("P14" or "limit"))
                { throw new PdfParseException("P14", number, branch:"R1147"); }
            }
            // Inspect complete original rows, including malformed ones. A failed
            // composite must not fall back to an independently split grade prefix.
            var compositeRows = new List<PdfGlyph[]>();
            foreach (var group in page.Glyphs.Where(g => g.SourceLine is >= 0).GroupBy(g => g.SourceLine))
            {
                var row = group.ToArray(); Charge(row.Length * 2L);
                var raw = string.Concat(row.Select(g => g.Text)); Charge(raw.Length);
                if (row.Any(selected.Contains) && Regex.IsMatch(raw, "^[12]年.+")) compositeRows.Add(row);
            }
            if (compositeRows.Count > 0)
            {
                hasComposite = true;
                var provenPeriods = new HashSet<PdfGlyph>(PdfGrid.OcrHeaderRows(page, periods, Charge).SelectMany(r => r), ReferenceEqualityComparer.Instance);
                foreach (var row in compositeRows)
                {
                    Charge(row.Length * 8L);
                    var raw = string.Concat(row.Select(g => g.Text));
                    var match = Regex.Match(raw, "^([12])年AI_([12])$");
                    if (!match.Success || match.Groups[1].Value != match.Groups[2].Value || !row.All(proven.Contains))
                        throw new PdfParseException("P14", number, branch:"R1148");
                    PdfBox owner;
                    try
                    {
                        owner = grid.Box(row[0].Cx, row[0].Cy);
                        if (row.Any(g => grid.Box(g.Cx, g.Cy) != owner
                            || g.X <= owner.Left || g.X + g.Width >= owner.Right || g.Y <= owner.Top || g.Y + g.Height >= owner.Bottom))
                            throw new PdfParseException("P14", number, branch:"R1149");
                        var owned = periods.Where(g => owner.Left < g.Cx && g.Cx < owner.Right).ToArray();
                        Charge(periods.Count * 4L);
                        if (owned.Length != 6 || !owned.All(provenPeriods.Contains)
                            || !owned.Select(g => g.Text).SequenceEqual(new[] { "1", "2", "3", "4", "5", "6" }))
                            throw new PdfParseException("P14", number, branch:"R1150");
                        var band = owned.Select(g => (Glyph: g, Box: grid.Box(g.Cx, g.Cy))).ToArray();
                        if (band.Any(p => p.Box.Top != owner.Bottom || p.Box.Left < owner.Left || p.Box.Right > owner.Right
                                || p.Glyph.X <= p.Box.Left || p.Glyph.X + p.Glyph.Width >= p.Box.Right
                                || p.Glyph.Y <= p.Box.Top || p.Glyph.Y + p.Glyph.Height >= p.Box.Bottom)
                            || band[0].Box.Left != owner.Left || band[^1].Box.Right != owner.Right
                            || band.Zip(band.Skip(1)).Any(p => p.First.Box.Right != p.Second.Box.Left))
                            throw new PdfParseException("P14", number, branch:"R1151");
                    }
                    catch (PdfParseException error) when (error.Stage is not ("P14" or "limit"))
                    { throw new PdfParseException("P14", number, branch:"R1152"); }
                    labels.Add(("AI_" + match.Groups[2].Value, (owner.Left + owner.Right) / 2));
                }
            }
        }
        if (labels.Count != (number == 6 ? 2 : 3)
            || hasComposite && labels.Select(l => l.Name).Distinct(StringComparer.Ordinal).Count() != labels.Count)
            throw new PdfParseException("P14", number, branch:"R1153");
        return labels.OrderBy(l => l.Cx).Select(l => l.Name).ToArray();
    }
    private static ParsedSpecialPage ReturnPage(PdfPageLayout page, int year, PdfTimes times, CancellationToken token, bool fromOcr, bool ordinary = false, ReturnGeometryRecorder? geometry = null)
    {
        var periods = PeriodHeader(page, "12345678", 5, 0.25); var headerY = periods[0].Cy; var step = periods[1].Cx - periods[0].Cx;
        if (step <= 5) throw new PdfParseException("P05", 1, branch:"R1154");
        var dates = Runs(page, page.Glyphs.Where(g => g.Cy < headerY && g.Cy > headerY - page.Height / 20), fromOcr)
            .Select(run => (Run: run, Day: Date(run.Text, year, true))).Where(p => p.Day is not null).OrderBy(p => p.Run.Cx).ToArray();
        if (dates.Length != 5 || dates.Select(p => p.Day).Distinct().Count() != 5) throw new PdfParseException("P10", 1, branch:"R1155");
        if (!ordinary && !dates.Select(p => p.Day!.Value).SequenceEqual(dates.Select(p => p.Day!.Value).Order())) throw new PdfParseException("P10", 1, branch:"R1156");
        var specialDay = dates[0].Day!.Value; var ordinaryStart = dates[1].Day!.Value; var ordinaryEnd = dates[^1].Day!.Value;
        var note = PdfGrid.Key(string.Concat(PdfGrid.Rows(page.Glyphs).Select(Joined))).Replace('～', '~').Replace('〜', '~');
        if (ordinaryStart.Month != ordinaryEnd.Month || !note.Contains($"{specialDay.Month}月{specialDay.Day}日の時間割は以下のとおり")
            || !note.Contains($"{ordinaryStart.Month}月{ordinaryStart.Day}日~{ordinaryEnd.Day}日は通常の授業日どおりの授業時間")) throw new PdfParseException("P05", 1, branch:"R1157");
        var ordinaryTimes = new PdfTimes(ScheduleTimes.Normal, new Dictionary<string, TimeRange>());
        var left = Runs(page, page.Glyphs.Where(g => g.Cx < periods[0].Cx - step * 0.15 && g.Cy > headerY + 5 && g.Cy < page.Height * 0.7), fromOcr);
        var gradeMax = periods[0].Cx - step * 0.8;
        var grades = left.Where(run => run.Cx < gradeMax && Regex.IsMatch(run.Text, "^(?:[1-5]|AI)$")).ToArray();
        var classRuns = left.Where(run => run.Cx >= gradeMax && Regex.IsMatch(run.Text, "^(?:[1-3]|CN|ES|IT)$")).OrderBy(run => run.Cy).ToArray();
        if (grades.Length != 6 || classRuns.Length != 17) throw new PdfParseException("P14", 1, branch:"R1158");
        var classes = new HashSet<string>(); var lessons = new List<SpecialLesson>(); var grid = new PdfGrid(page, token);
        var contentTop = ReturnContentTop(page, grid, classRuns[0], periods, token, fromOcr);
        var firstBox = grid.Box(periods[0].Cx, classRuns[0].Cy); var lastBox = grid.Box(periods[^1].Cx, classRuns[^1].Cy);
        grid.SetLessonArea(new(firstBox.Left, firstBox.Top, lastBox.Right, lastBox.Bottom));
        var lessonRows = classRuns.Select(run => {
            token.ThrowIfCancellationRequested(); var row=grid.Box(run.Cx,run.Cy);
            return row with { Top=Math.Max(row.Top,grid.Box(periods[0].Cx,run.Cy).Top) };
        }).ToArray();
        grid.SetLessonCells(lessonRows.SelectMany(row => periods.SelectMany(period => grid.Slices(row, period.Cx))));
        var gradeOwners = new List<(Run Grade,PdfBox Box)>();
        foreach (var grade in grades)
        {
            try { gradeOwners.Add((grade,grid.Box(grade.Cx,grade.Cy))); }
            catch(PdfParseException failure) when(failure.Stage!="limit") { gradeOwners.Clear();break; }
        }
        foreach (var (run, row) in classRuns.Zip(lessonRows))
        {
            var owners=gradeOwners.Where(g=>g.Box.Right<=row.Left && g.Box.Top<run.Cy && run.Cy<g.Box.Bottom).ToArray();
            if (gradeOwners.Count>0 && owners.Length!=1) throw new PdfParseException("P15",1, branch:"R1159");
            var grade = owners.Length==1 ? owners[0].Grade : grades.MinBy(g => Math.Abs(g.Cy - run.Cy))!;
            if (Math.Abs(grade.Cy - run.Cy) >= step * 2.5) throw new PdfParseException("P15", 1, branch:"R1160");
            var name = grade.Text == "AI" ? "AI_" + run.Text : grade.Text + "_" + run.Text;
            if (!classes.Add(name)) throw new PdfParseException("P16", 1, branch:"R1161");
            for (var dayIndex = 0; dayIndex < dates.Length; dayIndex++)
            {
                var dayTimes = dayIndex == 0 ? times : ordinaryTimes; var day = dates[dayIndex].Day!.Value;
                var xs = Enumerable.Range(0, 8).Select(i => periods[dayIndex * 8 + i].Cx).ToArray();
                for (var period = 1; period <= 8; period++)
                    foreach (var box in grid.Slices(row, xs[period - 1]))
                    {
                        token.ThrowIfCancellationRequested();
                        var content = box with { Top = Math.Max(box.Top, contentTop) };
                        geometry?.Record(page, grid, box, content, day.Iso(), name, period, token);
                        if (content.Top < content.Bottom && SpecialCell(grid, box, day, name, period, xs, dayTimes, 1,
                            ordinary:ordinary, contentTop:contentTop) is { } lesson) lessons.Add(lesson);
                    }
            }
        }
        return new(dates.Select(p => p.Day!.Value.Iso()).ToArray(), classes.Order(StringComparer.Ordinal).ToArray(), lessons);
    }
    private static double ReturnContentTop(PdfPageLayout page, PdfGrid grid, Run firstClass,
        IReadOnlyList<PdfGlyph> periods, CancellationToken token, bool fromOcr)
    {
        var classCell = grid.Box(firstClass.Cx, firstClass.Cy);
        var first = grid.Box(periods[0].Cx, firstClass.Cy);
        var last = grid.Box(periods[^1].Cx, firstClass.Cy);
        foreach (var bottom in page.Lines.Where(line => line.Horizontal &&
            line.X1 - .8 <= first.Left && last.Right <= line.X2 + .8 &&
            classCell.Top + .3 < line.Y1 && line.Y1 < firstClass.Box.Top).Select(line => line.Y1).Distinct().Order())
        {
            token.ThrowIfCancellationRequested();
            var headings = Runs(page, grid.Glyphs(new(first.Left, classCell.Top, last.Right, bottom)), fromOcr)
                .Where(run => Regex.IsMatch(run.Text, @"^[1-8]コマ(?:目)?(?:\([^
]*\))?$"));
            var days = headings.Select(run => Enumerable.Range(0, periods.Count)
                .MinBy(index => Math.Abs(periods[index].Cx - run.Cx)) / 8).Distinct().Take(2).Count();
            if (days > 1) return bottom;
        }
        return classCell.Top;
    }

}
