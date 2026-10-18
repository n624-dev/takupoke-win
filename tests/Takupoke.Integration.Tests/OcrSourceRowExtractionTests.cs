using System.Collections;
using System.Reflection;
using System.Text.Json;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Recovery;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class OcrSourceRowExtractionTests
{
    private static IReadOnlyList<(string Id, PdfGlyph[] Glyphs)> CapturedRows()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "fixtures", "recovery-fictional-v3-header-rows.json")));
        return json.RootElement.GetProperty("rows").EnumerateArray().Select(r =>
            (r.GetProperty("imageID").GetString() + "/" + r.GetProperty("targetID").GetString(),
             JsonSerializer.Deserialize<PdfGlyph[]>(r.GetProperty("glyphs"))!)).ToArray();
    }

    // Call the existing extraction boundary rather than a full corpus replay.
    private static string[] StrictRuns(PdfPageLayout page, bool fromOcr = true)
    {
        var method = typeof(PdfScheduleParser).GetMethod("Runs", BindingFlags.Static | BindingFlags.NonPublic)!;
        var args = method.GetParameters().Length == 1 ? new object[] { page.Glyphs } : [page, page.Glyphs, fromOcr];
        return ((IEnumerable)method.Invoke(null, args)!).Cast<object>()
            .Select(r => (string)r.GetType().GetProperty("Text")!.GetValue(r)!).ToArray();
    }

    private static PdfPageLayout Page(PdfGlyph[] glyphs, params PdfRule[] rules) => new(4060, 2800, glyphs, rules);

    [Fact]
    public void AllFiveCapturedExactNativeDatesStayOneStrictRun()
    {
        var rows = CapturedRows().Where(r => r.Id.StartsWith("exam", StringComparison.Ordinal)).ToArray();
        Assert.Equal(5, rows.Length);
        foreach (var row in rows)
            Assert.Equal(new[] { string.Concat(row.Glyphs.Select(g => g.Text)) }, StrictRuns(Page(row.Glyphs)));
    }

    [Theory]
    [InlineData("crossRow")]
    [InlineData("crossCell")]
    [InlineData("foreignOverlap")]
    [InlineData("reversedOrder")]
    [InlineData("orderGap")]
    [InlineData("differentSourceLine")]
    public void UnsafeMetadataCannotJoinTheCapturedDate(string mutation)
    {
        var row = CapturedRows().First(r => r.Id.StartsWith("exam", StringComparison.Ordinal)).Glyphs;
        var glyphs = row.ToArray(); var rules = Array.Empty<PdfRule>();
        if (mutation == "crossRow") glyphs[2] = glyphs[2] with { Y = glyphs[2].Y + 30 };
        if (mutation == "crossCell") rules = [new(glyphs[2].X - 1, glyphs[2].Y - 1, glyphs[2].X - 1, glyphs[2].Y + glyphs[2].Height + 1)];
        if (mutation == "foreignOverlap") glyphs = glyphs.Append(glyphs[2] with { Text = "X", SourceLine = 99999, SourceOrder = 99999 }).ToArray();
        if (mutation == "reversedOrder") glyphs[2] = glyphs[2] with { SourceOrder = glyphs[1].SourceOrder };
        if (mutation == "orderGap") glyphs[2] = glyphs[2] with { SourceOrder = glyphs[2].SourceOrder + 1 };
        if (mutation == "differentSourceLine") glyphs[2] = glyphs[2] with { SourceLine = 99999 };
        Assert.DoesNotContain("10月1日", StrictRuns(Page(glyphs, rules)));
    }

    [Fact]
    public void VectorSpacingKeepsTheOriginalGapRule()
    {
        var row = CapturedRows().First(r => r.Id.StartsWith("exam", StringComparison.Ordinal)).Glyphs;
        Assert.DoesNotContain("10月1日", StrictRuns(Page(row.Select(g => g with { SourceLine = null, SourceOrder = null }).ToArray())));
    }

    [Fact]
    public void VectorSourceLineMetadataDoesNotEnableOcrGrouping()
    {
        var row = CapturedRows().First(r => r.Id.StartsWith("exam", StringComparison.Ordinal)).Glyphs;
        Assert.Equal(new[] { "10", "月", "1日" }, StrictRuns(Page(row), fromOcr: false));
    }

    [Theory]
    [InlineData("nanGlyph")]
    [InlineData("infiniteGlyph")]
    [InlineData("nanRule")]
    [InlineData("infiniteRule")]
    [InlineData("pointRule")]
    [InlineData("diagonalRule")]
    public void InvalidGeometryCannotEstablishAnOcrRow(string mutation)
    {
        var row = CapturedRows().First(r => r.Id.StartsWith("exam", StringComparison.Ordinal)).Glyphs.ToArray();
        PdfRule[] rules = [];
        if (mutation == "nanGlyph") row[2] = row[2] with { Width = double.NaN };
        if (mutation == "infiniteGlyph") row[2] = row[2] with { X = double.PositiveInfinity };
        if (mutation == "nanRule") rules = [new(double.NaN, 0, double.NaN, 100)];
        if (mutation == "infiniteRule") rules = [new(0, 0, double.PositiveInfinity, 0)];
        if (mutation == "pointRule") rules = [new(0, 0, 0, 0)];
        if (mutation == "diagonalRule") rules = [new(0, 0, 100, 100)];
        var helper = typeof(PdfGrid).GetMethod("OcrHeaderRows", BindingFlags.Static | BindingFlags.NonPublic);
        var grouped = helper is null ? Array.Empty<IReadOnlyList<PdfGlyph>>() :
            ((IEnumerable)helper.Invoke(null, [Page(row, rules), row, (Action<long>)(_ => { })])!)
                .Cast<IReadOnlyList<PdfGlyph>>().ToArray();
        Assert.Empty(grouped);
    }

    private static PdfPageLayout WeekdayTable()
    {
        var captured = CapturedRows().First(r => r.Id == "normal-page-1/day-1").Glyphs;
        // Declared fictional rule/role control; the weekday atoms and their
        // CTC estimates, IDs and existing order remain exactly captured.
        PdfGlyph[] other = [new("令和17年度", 20, 20, 90, 12), new("前期", 140, 20, 24, 12),
            new("時間割", 200, 20, 36, 12), new("1-1", 30, 230, 18, 12), new("1", 150, 150, 6, 12),
            new("科目：", 140, 210, 36, 12), new("架空科", 200, 210, 36, 12),
            new("教員：", 140, 240, 36, 12), new("架空師", 200, 240, 36, 12),
            new("教室：", 140, 270, 36, 12), new("架空室", 200, 270, 36, 12)];
        PdfRule[] rules = [new(20, 88, 904, 88), new(20, 122, 904, 122), new(20, 194, 904, 194),
            new(20, 290, 904, 290), new(20, 88, 20, 290), new(120, 88, 120, 290), new(904, 88, 904, 290)];
        return Page(captured.Concat(other).ToArray(), rules);
    }

    [Fact]
    public void CapturedWeekdayHeaderUsesEveryOriginalEvidenceAtom()
    {
        var page = WeekdayTable();
        var doc = RecoveryDocumentBuilder.Build(new string('a', 64), MaterialKind.Timetable, [page],
            (_, box) => !page.Glyphs.Any(g => box.Contains(new(g.X, g.Y, g.Width, g.Height))), new HashSet<int> { 1 });
        Assert.Equal("月曜日", string.Concat(doc.DayEvidence["1"].Select(id => doc.Sources.Single(s => s.Id == id).Text)));
        Assert.Equal(new[] { "p1s0", "p1s1", "p1s2" }, doc.DayEvidence["1"]);
        foreach (var (g, i) in page.Glyphs.Take(3).Select((g, i) => (g, i)))
        {
            var s = doc.Sources.Single(s => s.Id == $"p1s{i}");
            Assert.Equal(new RecoveryBox(g.X, g.Y, g.Width, g.Height), s.Box);
            Assert.Equal(g.SourceLine, s.SourceLine); Assert.Equal(g.SourceOrder, s.SourceOrder);
        }
    }

    [Theory]
    [InlineData("crossRow")]
    [InlineData("crossCell")]
    [InlineData("foreignOverlap")]
    [InlineData("orderGap")]
    [InlineData("differentSourceLine")]
    public void UnsafeWeekdayCannotGainAWholeRowEvidenceChain(string mutation)
    {
        var page = WeekdayTable(); var glyphs = page.Glyphs.ToArray();
        if (mutation == "crossRow") glyphs[1] = glyphs[1] with { Y = glyphs[1].Y + 30 };
        if (mutation == "crossCell") page = page with { Lines = page.Lines.Append(new PdfRule(glyphs[1].X - 1, 88, glyphs[1].X - 1, 122)).ToArray() };
        if (mutation == "foreignOverlap") glyphs = glyphs.Append(glyphs[1] with { Text = "X", SourceLine = 99999, SourceOrder = 99999 }).ToArray();
        if (mutation == "orderGap") glyphs[1] = glyphs[1] with { SourceOrder = glyphs[1].SourceOrder + 1 };
        if (mutation == "differentSourceLine") glyphs[1] = glyphs[1] with { SourceLine = 99999 };
        page = page with { Glyphs = glyphs };
        try
        {
            var doc = RecoveryDocumentBuilder.Build(new string('a', 64), MaterialKind.Timetable, [page],
                (_, box) => !page.Glyphs.Any(g => box.Contains(new(g.X, g.Y, g.Width, g.Height))), new HashSet<int> { 1 });
            Assert.DoesNotContain(doc.DayEvidence.Values, ids => string.Concat(ids.Select(id => doc.Sources.Single(s => s.Id == id).Text)) == "月曜日");
        }
        catch (InvalidDataException) { /* An input refusal is also fail closed. */ }
    }

    [Fact]
    public async Task ChangedHeaderEvidenceCannotReuseAnAcceptedScope()
    {
        var document = RecoveryParallelProofTests.Build(RecoveryParallelProofTests.TwoPages("wholeformal"));
        var run = await RecoveryEngine.RunAsync(document, "windows", 10, true, [], _ => null);
        Assert.Equal(RecoveryJobState.AwaitingConfirmation, run.State);
        var result = run.Result!;
        var acceptance = new RecoveryAcceptance(document.PdfHash, RecoveryValidator.Fingerprint(result),
            RecoveryValidator.Fingerprint(document), result.Metadata, DateTimeOffset.Parse("2032-04-01T00:00:00Z"));
        Assert.True(RecoveryValidator.CanReuse(acceptance, document, result));
        var days = document.DayEvidence.ToDictionary(p => p.Key, p => p.Value);
        var day = days.First(); days[day.Key] = day.Value.Take(day.Value.Count - 1).ToArray();
        var changed = document with { DayEvidence = days };
        Assert.NotEqual(acceptance.ScopeHash, RecoveryValidator.Fingerprint(changed));
        Assert.False(RecoveryValidator.CanReuse(acceptance, changed, result));
    }
}
