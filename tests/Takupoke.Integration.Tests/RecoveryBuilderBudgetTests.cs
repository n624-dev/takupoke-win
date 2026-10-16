using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Recovery;
using Takupoke.Infrastructure.Storage;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class RecoveryBuilderBudgetTests
{
    private static RecoveryDocument Build(RecoveryBuilderBudgetFixture fixture, CancellationToken token = default) =>
        RecoveryDocumentBuilder.Build(new string('a', 64), MaterialKind.ExamReturn, fixture.Pages,
            (page, box) => fixture.Scanners[page - 1](box), Enumerable.Range(1, 5).ToHashSet(), token,
            allowStructureProposal: true);

    [Fact]
    public async Task FiveOriginalDateTablesPreserveAll680SlotsAndFormalValuesWithinBudget()
    {
        using var fixture = RecoveryBuilderBudgetFixture.Load();
        var document = Build(fixture);
        Assert.Equal(680, document.RequiredSlots.Count);
        Assert.Equal(680, document.Cells.SelectMany(c => c.Slots).Distinct().Count());
        Assert.Equal(1409, document.Sources.Count);
        Assert.Empty(RecoveryValidator.InputErrors(document));
        var run = await RecoveryEngine.RunAsync(document, "windows", 10, true, [], _ => null);
        Assert.Equal(RecoveryJobState.AwaitingConfirmation, run.State);
        Assert.NotNull(run.Result);
        Assert.True(RecoveryValidator.Validate(document, run.Result!).CanAdopt);
        var source = new SourceRecord("fictional", MaterialKind.ExamReturn, "fictional.pdf", "fictional", "fictional.pdf",
            new string('a', 64), 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);
        var formal = RecoveryAnalysisConverter.Convert(source, document, run.Result!, DateTimeOffset.UnixEpoch);
        var expected = fixture.Original.RootElement.GetProperty("formalOracle");
        var special = Assert.IsType<SpecialAnalysis>(formal.Special);
        Assert.Equal(expected.GetProperty("schoolYear").GetInt32(), formal.SchoolYear);
        var gold = expected.GetProperty("special");
        Assert.True(gold.GetProperty("coveredClasses").EnumerateArray().Select(v => v.GetString()!).ToHashSet().SetEquals(special.CoveredClasses));
        Assert.True(gold.GetProperty("coveredDates").EnumerateArray().Select(v => v.GetString()!).ToHashSet().SetEquals(special.CoveredDates));
        var lesson = Assert.Single(special.Lessons); var wanted = gold.GetProperty("lessons")[0]; var names = wanted.GetProperty("names");
        Assert.Equal(new LessonNames(names.GetProperty("subject").GetString()!, names.GetProperty("teacher").GetString()!, names.GetProperty("room").GetString()!), lesson.Names);
        Assert.Equal(wanted.GetProperty("date").GetString(), lesson.Date); Assert.Equal(wanted.GetProperty("className").GetString(), lesson.ClassName);
        Assert.Equal(wanted.GetProperty("period").GetInt32(), lesson.Period);
        Assert.Equal(wanted.GetProperty("spanStart").GetInt32(), lesson.SpanStart); Assert.Equal(wanted.GetProperty("spanEnd").GetInt32(), lesson.SpanEnd);
        var recorded = wanted.GetProperty("recordedTime");
        Assert.Equal(new TimeRange(recorded.GetProperty("start").GetString()!, recorded.GetProperty("end").GetString()!), lesson.RecordedTime);
        Assert.Equal(40, special.DatePeriodTimes!.Count);
        foreach (var clock in gold.GetProperty("datePeriodClocks").EnumerateArray())
            Assert.Equal(new TimeRange(clock.GetProperty("start").GetString()!, clock.GetProperty("end").GetString()!),
                special.PeriodTime(DateOnly.Parse(clock.GetProperty("date").GetString()!), clock.GetProperty("period").GetInt32()));
    }

    [Fact]
    public void DuplicateSlotsAcrossPagesStillReject()
    {
        using var fixture = RecoveryBuilderBudgetFixture.Load();
        var error = Assert.Throws<InvalidDataException>(() => RecoveryDocumentBuilder.Build(new string('a', 64), MaterialKind.ExamReturn,
            [fixture.Pages[0], fixture.Pages[0]], (_, box) => fixture.Scanners[0](box), allowStructureProposal: true));
        Assert.Equal("時間割の同じ位置に複数のセル候補があります。", error.Message);
    }

    [Fact]
    public void CancellationDuringBlankInspectionStillStopsConstruction()
    {
        using var fixture = RecoveryBuilderBudgetFixture.Load();
        using var cancellation = new CancellationTokenSource(); var scans = 0;
        Assert.Throws<OperationCanceledException>(() => RecoveryDocumentBuilder.Build(new string('a', 64), MaterialKind.ExamReturn, fixture.Pages,
            (_, _) => { scans++; cancellation.Cancel(); return true; }, token: cancellation.Token, allowStructureProposal: true));
        Assert.True(scans > 0);
    }
}
