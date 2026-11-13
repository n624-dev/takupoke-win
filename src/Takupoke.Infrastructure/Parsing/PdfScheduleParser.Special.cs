using System.Text.RegularExpressions;
using Takupoke.Core;

namespace Takupoke.Infrastructure.Parsing;

public static partial class PdfScheduleParser
{
    private sealed record ParsedSpecialPage(IReadOnlyList<string> Dates, IReadOnlyList<string> Classes, IReadOnlyList<SpecialLesson> Lessons);
    public static SpecialAnalysis Special(IReadOnlyList<PdfPageLayout> pages, MaterialKind kind, CancellationToken token = default)
    {
        if (kind is not MaterialKind.Exam and not MaterialKind.ExamReturn || pages.Count is < 1 or > 12 || kind == MaterialKind.ExamReturn && pages.Count != 1) throw new PdfParseException("P04");
        string? dedicatedTimeDate = null;
        int? year = null; string[]? expectedDates = null; PdfTimes? expectedTimes = null;
        var scopes = new List<SpecialScope>(); var pairs = new HashSet<(string Class, string Date)>();
        var classes = new HashSet<string>(); var lessons = new List<SpecialLesson>();
        for (var index = 0; index < pages.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var page = pages[index]; page.Validate(index + 1);
            var heading = Heading(page, 0.25); var currentYear = Year(heading, index + 1);
            if (!heading.Contains("試験") || heading.Contains("返却") != (kind == MaterialKind.ExamReturn)) throw new PdfParseException("P04", index + 1);
            if (year is not null && year != currentYear) throw new PdfParseException("P03", index + 1);
            year = currentYear;
            if (kind == MaterialKind.Exam) {
                var declarations=PdfGrid.Rows(page.Glyphs.Where(g=>g.Cy<page.Height/4 || g.Cy>page.Height*0.9))
                    .SelectMany(row=>Regex.Matches(PdfGrid.Key(Joined(row)),"全([0-9]{1,2})ページ").Cast<Match>());
                if(declarations.Any(m=>int.Parse(m.Groups[1].Value)!=pages.Count)) throw new PdfParseException("P04",index+1);
            }
            var times = ReadTimes(page, kind == MaterialKind.Exam ? 6 : 8);
            if (expectedTimes is not null && !expectedTimes.Same(times)) throw new PdfParseException("P05", index + 1);
            expectedTimes = times;
            var parsed = kind == MaterialKind.Exam ? ExamPage(page, currentYear, index + 1, times, token) : ReturnPage(page, currentYear, times, token);
            if (kind == MaterialKind.ExamReturn) dedicatedTimeDate = parsed.Dates.FirstOrDefault();
            var dates = parsed.Dates.Order(StringComparer.Ordinal).ToArray();
            if (kind == MaterialKind.ExamReturn && expectedDates is not null && !expectedDates.SequenceEqual(dates)) throw new PdfParseException("P10", index + 1);
            expectedDates = (expectedDates ?? []).Concat(dates).Distinct().Order(StringComparer.Ordinal).ToArray();
            foreach (var cls in parsed.Classes)
                foreach (var date in dates)
                    if (!pairs.Add((cls, date))) throw new PdfParseException("P16", index + 1);
            scopes.Add(new(index + 1, parsed.Classes, dates));
            classes.UnionWith(parsed.Classes); lessons.AddRange(parsed.Lessons);
            if (lessons.Count > MaximumRecords) throw new PdfParseException("limit", index + 1);
        }
        if (year is null || expectedDates is null || expectedTimes is null || classes.Count == 0 || kind == MaterialKind.ExamReturn && classes.Count != 17 || lessons.Count == 0) throw new PdfParseException("P04");
        var analysis = new SpecialAnalysis(kind, year.Value, expectedDates, classes.Order(StringComparer.Ordinal).ToArray(), expectedTimes.Single,
            lessons.OrderBy(l => l.Date, StringComparer.Ordinal).ThenBy(l => l.ClassName, StringComparer.Ordinal).ThenBy(l => l.Period).ThenBy(l => l.Page).ToArray(), dedicatedTimeDate, scopes);
        if (!analysis.ValidScopes()) throw new PdfParseException("P16");
        return analysis;
    }
    private static double? ExamBodyTop(PdfPageLayout page,IReadOnlyList<PdfGlyph> periods)
    {
        var rules=page.Lines.Where(l=>l.Horizontal && l.Y1>periods[0].Cy).OrderBy(l=>l.Y1).ToArray();
        for(var start=0;start<rules.Length;) {
            var y=rules[start].Y1;var end=start+1;
            while(end<rules.Length && rules[end].Y1-y<=0.8)end++;
            var column=0;
            foreach(var rule in rules[start..end].OrderBy(l=>l.X1)) {
                if(column==periods.Count || rule.X1-0.8>periods[column].Cx)break;
                while(column<periods.Count && rule.X1-0.8<=periods[column].Cx && periods[column].Cx<=rule.X2+0.8)column++;
            }
            if(column==periods.Count)return y;
            start=end;
        }
        return null;
    }

    private static ParsedSpecialPage ExamPage(PdfPageLayout page, int year, int number, PdfTimes times, CancellationToken token)
    {
        var candidates = PdfGrid.Rows(page.Glyphs).Where(row =>
        {
            var value = PdfGrid.Key(Joined(row));
            return value.Length > 0 && value.Length % 6 == 0 && value.Length <= 384
                && value == string.Concat(Enumerable.Repeat("123456", value.Length / 6));
        }).ToArray();
        if (candidates.Length != 1 || candidates[0].Count % 6 != 0 || candidates[0].Any(g => g.Text.Length != 1)) throw new PdfParseException("P05", number);
        var periods = candidates[0]; var columns = periods.Count / 6; var headerY = periods[0].Cy;
        var labels = Runs(page.Glyphs.Where(g => g.Cy < headerY - 3 && g.Cy > headerY - page.Height / 10))
            .Where(run => Regex.IsMatch(run.Text, "^(?:[12]年|[1-5]-(?:[1-3]|[A-Z]{2}))$")).OrderBy(run => run.Cx).ToArray();
        if (labels.Length != columns) throw new PdfParseException("P14", number);
        var names = labels.Select(run => run.Text.EndsWith("年", StringComparison.Ordinal) ? "AI_" + run.Text[..1] : run.Text.Replace('-', '_')).ToArray();
        var bodyBottom = page.Lines.Where(l => l.Vertical && l.X1 >= periods[0].Cx-(periods[1].Cx-periods[0].Cx) && l.X1 <= periods[^1].Cx && l.Y2 > headerY)
            .Select(l => l.Y2).DefaultIfEmpty(double.NaN).Max();
        if (!double.IsFinite(bodyBottom)) throw new PdfParseException("P07",number);
        var dates = Runs(page.Glyphs.Where(g => g.Cy > headerY + 5 && g.Cy < bodyBottom))
            .Where(run => run.Cx < periods[0].Cx).Select(run => (Run: run, Day: Date(run.Text, year, false)))
            .Where(p => p.Day is not null).OrderBy(p => p.Run.Cy).ToArray();
        if (dates.Length is < 1 or > 31 || dates.Select(p => p.Day).Distinct().Count() != dates.Length) throw new PdfParseException("P10", number);
        var grid = new PdfGrid(page); var lessons = new List<SpecialLesson>();
        var rows = dates.Select(pair => grid.Box(pair.Run.Cx,pair.Run.Cy)).ToArray();
        var tableBottom=rows[0].Top;
        foreach(var rule in page.Lines.Where(l=>l.Vertical && Math.Abs(l.X1-rows[0].Right)<0.8).OrderBy(l=>l.Y1))
            if(rule.Y1<=tableBottom+0.8 && rule.Y2>tableBottom)tableBottom=rule.Y2;
        var left = periods[0].Cx-(periods[1].Cx-periods[0].Cx)/2;
        var right = periods[^1].Cx+(periods[^1].Cx-periods[^2].Cx)/2;
        var bodyTop = ExamBodyTop(page,periods) ?? throw new PdfParseException("P07",number);
        if (rows.Zip(rows.Skip(1)).Any(pair => Math.Abs(pair.First.Bottom-pair.Second.Top)>1)
            || page.Glyphs.Where(g => left < g.Cx && g.Cx < right && bodyTop < g.Cy && g.Cy < tableBottom)
                .Any(g => !rows.Any(row => row.Top < g.Cy && g.Cy < row.Bottom))) throw new PdfParseException("P10",number);
        if(Math.Abs(rows[0].Top-bodyTop)>1 || Math.Abs(rows[^1].Bottom-tableBottom)>1) throw new PdfParseException("P10",number);
        for (var column=0;column<columns;column++)
        {
            var xs=periods.Skip(column*6).Take(6).Select(g=>g.Cx).ToArray();
            if (xs.Zip(xs.Skip(1)).Any(pair=>pair.First>=pair.Second)
                || labels[column].Cx < xs[0]-(xs[1]-xs[0])/2 || labels[column].Cx > xs[5]+(xs[5]-xs[4])/2) throw new PdfParseException("P14",number);
        }
        foreach (var pair in dates)
        {
            var row = grid.Box(pair.Run.Cx, pair.Run.Cy);
            for (var column = 0; column < names.Length; column++)
            {
                var xs = Enumerable.Range(0, 6).Select(i => periods[column * 6 + i].Cx).ToArray();
                for (var period = 1; period <= 6; period++)
                    foreach (var box in grid.Slices(row, xs[period - 1]))
                    {
                        token.ThrowIfCancellationRequested();
                        if (box.Top < row.Top-0.8 || box.Bottom > row.Bottom+0.8 || periods.Select((g,i)=>(g,i))
                            .Any(p=>p.i/6!=column && box.Left+0.5<p.g.Cx && p.g.Cx<box.Right-0.5)) throw new PdfParseException("P08",number);
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
        foreach (var run in classRuns)
        {
            var grade = grades.MinBy(g => Math.Abs(g.Cy - run.Cy))!;
            if (Math.Abs(grade.Cy - run.Cy) >= step * 2.5) throw new PdfParseException("P15", 1);
            var name = grade.Text == "AI" ? "AI_" + run.Text : grade.Text + "_" + run.Text;
            if (!classes.Add(name)) throw new PdfParseException("P16", 1);
            var row = grid.Box(run.Cx, run.Cy);
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
