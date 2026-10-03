using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Takupoke.Core;
using Takupoke.Core.Recovery;

namespace Takupoke.Infrastructure.Parsing;

public static partial class PdfScheduleParser
{
    public const int TimetableVersion = 15;
    public const int SpecialVersion = 14;
    private const int MaximumRecords = 10000;
    private static string Joined(IEnumerable<PdfGlyph> glyphs) => string.Concat(glyphs.Select(g => g.Text));
    private static string Heading(PdfPageLayout page, double fraction) => PdfGrid.Key(string.Concat(PdfGrid.Rows(page.Glyphs.Where(g => g.Cy < page.Height * fraction)).Select(Joined)));
    private static int Year(string heading, int page)
    {
        var match = Regex.Match(heading, "令和([0-9]{1,2})年度");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var era) || era is < 1 or > 99) throw new PdfParseException("P03", page);
        return 2018 + era;
    }
    private static IReadOnlyList<PdfGlyph> PeriodHeader(PdfPageLayout page, string sequence, int count, double fraction)
    {
        var matching = PdfGrid.Rows(page.Glyphs.Where(g => g.Cy < page.Height * fraction)).Where(row => PdfGrid.Key(Joined(row)) == string.Concat(Enumerable.Repeat(sequence, count))).ToArray();
        if (matching.Length != 1 || matching[0].Count != sequence.Length * count) throw new PdfParseException("P05");
        return matching[0];
    }
    public static TimetableAnalysis Timetable(IReadOnlyList<PdfPageLayout> pages, CancellationToken token = default)
    {
        if (pages.Count != 1) throw new PdfParseException("P04");
        var page = pages[0]; page.Validate(1); token.ThrowIfCancellationRequested();
        var heading = Heading(page, 1.0 / 8); var year = Year(heading, 1);
        if (!heading.Contains("時間割") || heading.Contains("前期") == heading.Contains("後期")) throw new PdfParseException("P04", 1);
        var grid = new PdfGrid(page); var header = PeriodHeader(page, "12345678", 5, 0.2);
        var first = grid.Box(header[0].Cx, header[0].Cy); var classBox = grid.Box(first.Left - 2, first.Bottom + 20);
        var bodyBottom = page.Lines.Where(l => l.Vertical && Math.Abs(l.X1 - classBox.Right) < 0.3).Select(l => l.Y2).DefaultIfEmpty(double.NaN).Max();
        if (!double.IsFinite(bodyBottom)) throw new PdfParseException("P06", 1);
        var lastPeriodBox = grid.Box(header[^1].Cx, header[^1].Cy);
        grid.SetLessonArea(new(first.Left, first.Bottom, lastPeriodBox.Right, bodyBottom));
        var classRows = PdfGrid.Rows(page.Glyphs.Where(g => classBox.Left < g.Cx && g.Cx < classBox.Right && g.Cy > first.Bottom && g.Cy < bodyBottom));
        var lessonRows = classRows.Select(glyphs =>
        {
            token.ThrowIfCancellationRequested();
            var y = glyphs.Average(g => g.Cy); var row = grid.Box((classBox.Left + classBox.Right) / 2, y);
            return row with { Top = Math.Max(row.Top, grid.Box(header[0].Cx, y).Top) };
        }).ToArray();
        grid.SetLessonCells(lessonRows.SelectMany(row => header.SelectMany(period => grid.Slices(row, period.Cx))));
        var classes = new HashSet<string>(); var output = new List<NormalLesson>();
        foreach (var (glyphs, classIndex) in classRows.Select((glyphs, index) => (glyphs, index)))
        {
            token.ThrowIfCancellationRequested();
            var label = PdfGrid.Key(Joined(glyphs));
            if (!Regex.IsMatch(label, "^(?:[1-9]|[A-Z]{2,8})$")) throw new PdfParseException("P14", 1);
            var y = glyphs.Average(g => g.Cy); var row = grid.Box((classBox.Left + classBox.Right) / 2, y);
            var grade = PdfGrid.Key(string.Concat(grid.Text(grid.Box(classBox.Left - 2, y))));
            if (grade != "AI" && !Regex.IsMatch(grade, "^[1-9]$")) throw new PdfParseException("P15", 1);
            var name = ClassSelection.Canonical(grade + "_" + label);
            if (!classes.Add(name)) throw new PdfParseException("P16", 1);
            row = lessonRows[classIndex];
            for (var column = 0; column < header.Count; column++)
                foreach (var box in grid.Slices(row, header[column].Cx))
                {
                    token.ThrowIfCancellationRequested();
                    var position = new PdfFailurePosition(classIndex + 1, column / 8 + 1, column % 8 + 1);
                    IReadOnlyList<string> lines;
                    try { lines = grid.LessonFields(box); }
                    catch (PdfParseException error) { throw new PdfParseException(error.Stage, 1, position); }
                    position = position with { DetectedLines = lines.Count };
                    if (lines.Count == 0) continue;
                    if (lines.Count != 3 || lines[0].Length == 0) throw new PdfParseException("P17", 1, position);
                    if (lines.Any(RecoveryRoleLabels.HasPrefix)) throw new PdfParseException("P17", 1, position);
                    var fields = lines.Concat(Enumerable.Repeat("", 3 - lines.Count)).ToArray();
                    var parts = fields.Select(f => f.Replace('･', '・').Split('・')).ToArray();
                    var parallel = lines.Count == 3 && parts.All(p => p.Length == 2);
                    if (parts[0].Length > 1 && parts[1].Length > 1 && !parallel) throw new PdfParseException("P18", 1, position);
                    if (parallel && parts[0].Any(string.IsNullOrEmpty)) throw new PdfParseException("P19", 1, position);
                    for (var variant = 0; variant < (parallel ? 2 : 1); variant++)
                    {
                        var values = parallel ? parts.Select(p => p[variant]).ToArray() : fields;
                        output.Add(new(name, column / 8 + 1, column % 8 + 1, new(values[0], values[1], CollapseRoomMarks(values[2])), string.Join("\n", lines), 1));
                    }
                    if (output.Count > MaximumRecords) throw new PdfParseException("limit", 1);
                }
        }
        if (classes.Count == 0 || output.Count == 0) throw new PdfParseException("P04", 1);
        return new(year, heading.Contains("前期") ? "前期" : "後期", output);
    }
    public static string CollapseRoomMarks(string room)
    {
        var output = new StringBuilder(); var afterKana = false; char? repeated = null;
        foreach (var c in room)
        {
            if (c is >= '\uFF66' and <= '\uFF9D') { afterKana = true; repeated = null; }
            else if (c is '\uFF9E' or '\uFF9F')
            {
                if (afterKana) { afterKana = false; repeated = c; }
                else if (repeated == c) continue;
                else repeated = null;
            }
            else { afterKana = false; repeated = null; }
            output.Append(c);
        }
        return output.ToString();
    }
    private sealed record Run(string Text, PdfBox Box)
    { public double Cx => (Box.Left + Box.Right) / 2; public double Cy => (Box.Top + Box.Bottom) / 2; }
    private static IReadOnlyList<Run> Runs(IEnumerable<PdfGlyph> glyphs)
    {
        var runs = new List<Run>();
        foreach (var row in PdfGrid.Rows(glyphs))
        {
            var chunks = new List<List<PdfGlyph>>();
            foreach (var g in row)
            {
                if (chunks.Count == 0 || g.X - (chunks[^1][^1].X + chunks[^1][^1].Width) > Math.Max(2, Math.Min(chunks[^1][^1].Height, g.Height) * 0.55)) chunks.Add([]);
                chunks[^1].Add(g);
            }
            foreach (var chunk in chunks)
            {
                var text = PdfGrid.Key(Joined(chunk));
                if (text.Length > 0) runs.Add(new(text, new(chunk.Min(g => g.X), chunk.Min(g => g.Y), chunk.Max(g => g.X + g.Width), chunk.Max(g => g.Y + g.Height))));
            }
        }
        return runs;
    }
    private sealed record PdfTimes(IReadOnlyDictionary<int, TimeRange> Single, IReadOnlyDictionary<string, TimeRange> Consecutive)
    {
        public bool Same(PdfTimes other) => Single.Count == other.Single.Count && Consecutive.Count == other.Consecutive.Count
            && Single.All(p => other.Single.GetValueOrDefault(p.Key) == p.Value) && Consecutive.All(p => other.Consecutive.GetValueOrDefault(p.Key) == p.Value);
    }
    private static PdfTimes ReadTimes(PdfPageLayout page, int count)
    {
        var times = new Dictionary<int, TimeRange>(); var consecutive = new Dictionary<string, TimeRange>();
        TimeRange Range(string start, string end)
        {
            string Clock(string value) => TimeOnly.TryParseExact(value, ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var clock) ? clock.ToString("HH:mm", CultureInfo.InvariantCulture) : throw new PdfParseException("P05");
            var range = new TimeRange(Clock(start), Clock(end)); if (!range.IsValid) throw new PdfParseException("P05"); return range;
        }
        foreach (var row in PdfGrid.Rows(page.Glyphs))
        {
            var value = PdfGrid.Key(Joined(row));
            foreach (Match match in Regex.Matches(value, "([1-8])時限目([0-9]{1,2}:[0-9]{2})[~〜]([0-9]{1,2}:[0-9]{2})"))
            {
                var period = int.Parse(match.Groups[1].Value);
                if (period > count || !times.TryAdd(period, Range(match.Groups[2].Value, match.Groups[3].Value))) throw new PdfParseException("P05");
            }
            foreach (Match match in Regex.Matches(value, "([1-8])[・･]([1-8])時限連続([0-9]{1,2}:[0-9]{2})[~〜]([0-9]{1,2}:[0-9]{2})"))
            {
                var first = int.Parse(match.Groups[1].Value); var last = int.Parse(match.Groups[2].Value);
                if (first >= last || last > count || !consecutive.TryAdd(first + "-" + last, Range(match.Groups[3].Value, match.Groups[4].Value))) throw new PdfParseException("P05");
            }
        }
        if (times.Count != count) throw new PdfParseException("P05");
        for (var period = 2; period <= count; period++)
            if (string.CompareOrdinal(times[period].Start, times[period - 1].End) < 0) throw new PdfParseException("P05");
        return new(times, consecutive);
    }
    private static DateOnly? Date(string text, int schoolYear, bool slash)
    {
        var match = Regex.Match(text, slash ? "^([0-9]{1,2})/([0-9]{1,2})$" : "^([0-9]{1,2})月([0-9]{1,2})日");
        if (!match.Success) return null;
        var month = int.Parse(match.Groups[1].Value); var day = int.Parse(match.Groups[2].Value);
        try { return new(month >= 4 ? schoolYear : schoolYear + 1, month, day); } catch (ArgumentOutOfRangeException) { return null; }
    }
    private static SpecialLesson? SpecialCell(PdfGrid grid, PdfBox box, DateOnly day, string cls, int period, double[] xs, PdfTimes times, int page)
    {
        var lines = grid.LessonFields(box).Select(l => l.Trim()).ToArray();
        if (lines.Length > 0 && lines.Length != 3 || lines.Sum(l => Encoding.UTF8.GetByteCount(l)) > 4096) throw new PdfParseException("P17", page);
        if (lines.Length == 0) return null;
        if (lines.Any(RecoveryRoleLabels.HasPrefix)) throw new PdfParseException("P17", page);
        var covered = xs.Select((x, i) => (x, i)).Where(p => box.Left + 0.5 < p.x && p.x < box.Right - 0.5).Select(p => p.i + 1).ToArray();
        if (covered.Length == 0 || !covered.Contains(period)) throw new PdfParseException("P08", page);
        var first = covered[0]; var last = covered[^1];
        var time = first == last ? times.Single.GetValueOrDefault(period) : times.Consecutive.GetValueOrDefault(first + "-" + last)
            ?? (times.Single.TryGetValue(first, out var a) && times.Single.TryGetValue(last, out var b) ? new(a.Start, b.End) : null);
        return new(day.Iso(), cls, period, first, last, time, lines, page);
    }
}
