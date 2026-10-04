using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class RecoveryClockOrderTests
{
    private static PdfPageLayout Page(IReadOnlyList<string> classes, bool explicitDay = false)
    {
        const int dayCount = 5;
        var width = 70 + dayCount * 600;
        var bottom = 80 + classes.Count * 60;
        var glyphs = new List<PdfGlyph>(); var rules = new List<PdfRule>();
        void Text(string value, double x, double y) => glyphs.Add(new(value, x, y, Math.Max(2, value.Length * 2), 8));
        Text("2026年度", 2, 10); Text("試験時間割", 100, 10);
        foreach (var y in new[] { 40, 60, 80 }) rules.Add(new(0, y, width, y));
        rules.Add(new(0, 40, 0, bottom)); rules.Add(new(70, 40, 70, bottom));
        for (var day = 0; day < dayCount; day++)
        {
            Text($"10月{day + 1}日", 80 + day * 600, 45);
            rules.Add(new(70 + (day + 1) * 600, 40, 70 + (day + 1) * 600, bottom));
            for (var period = 0; period < 6; period++)
            {
                var x = 70 + (day * 6 + period) * 100;
                Text((period + 1).ToString(), x + 10, 65); rules.Add(new(x, 60, x, bottom));
            }
        }
        for (var row = 0; row < classes.Count; row++)
        {
            Text(classes[row], 5, 100 + row * 60);
            rules.Add(new(0, 80 + (row + 1) * 60, width, 80 + (row + 1) * 60));
        }
        Text("試験時間割", 0, bottom + 20);
        var clocks = new[] { "08:50〜09:35", "09:35〜10:20", "10:30〜11:15", "11:15〜12:00", "12:50〜13:35", "13:35〜14:20" };
        for (var day = 0; day < (explicitDay ? dayCount : 1); day++)
        for (var period = 0; period < 6; period++)
        {
            var x = 100 + day * 600 + period * 100;
            if (explicitDay) Text($"10月{day + 1}日", x, bottom + 40);
            Text((period + 1).ToString(), x, bottom + 60);
            Text(clocks[period], x, bottom + 90);
        }
        return new(width + 20, bottom + 150, glyphs, rules);
    }

    [Fact]
    public async Task RepeatedClockChartsKeepHeadersInOriginalOrderAcrossPages()
    {
        var all = RecoveryValidator.SpecialClasses;
        var pages = new[] { Page(all.Take(8).ToArray()), Page(all.Skip(8).ToArray()) };
        var document = RecoveryDocumentBuilder.Build(new string('a', 64), MaterialKind.Exam, pages, (_, _) => true);
        var order = document.Sources.Select((source, index) => (source.Id, index)).ToDictionary(v => v.Id, v => v.index);
        foreach (var ids in document.PeriodEvidence.Values.Concat(document.DayEvidence.Values))
            Assert.Equal(ids.OrderBy(id => order[id]), ids);
        Assert.Equal(510, document.RequiredSlots.Count);
        Assert.Empty(RecoveryValidator.InputErrors(document));
        var result = await RecoveryEngine.RunAsync(document, "windows", 10, true, [], _ => null);
        Assert.Equal(RecoveryJobState.AwaitingConfirmation, result.State);
        Assert.NotNull(result.Result);
        Assert.True(RecoveryValidator.Validate(document, result.Result!).CanAdopt);
    }

    [Fact]
    public async Task ExplicitDayClockHeadersKeepDayAndPeriodInventoryOrderAcrossPages()
    {
        var all = RecoveryValidator.SpecialClasses;
        var pages = new[] { Page(all.Take(8).ToArray(), true), Page(all.Skip(8).ToArray(), true) };
        var document = RecoveryDocumentBuilder.Build(new string('a', 64), MaterialKind.Exam, pages, (_, _) => true);
        var order = document.Sources.Select((source, index) => (source.Id, index)).ToDictionary(v => v.Id, v => v.index);
        foreach (var ids in document.PeriodEvidence.Values.Concat(document.DayEvidence.Values))
            Assert.Equal(ids.OrderBy(id => order[id]), ids);
        Assert.Equal(510, document.RequiredSlots.Count);
        Assert.Empty(RecoveryValidator.InputErrors(document));
        var result = await RecoveryEngine.RunAsync(document, "windows", 10, true, [], _ => null);
        Assert.Equal(RecoveryJobState.AwaitingConfirmation, result.State);
        Assert.NotNull(result.Result);
        Assert.True(RecoveryValidator.Validate(document, result.Result!).CanAdopt);
    }
}
