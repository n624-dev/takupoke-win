using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Recovery;
using Takupoke.Infrastructure.Storage;
using Takupoke.Win.ViewModels;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class RecoveryPreviewDisplayTests
{
    private static async Task<RecoveryPreview> Preview()
    {
        var document = RecoveryDocumentBuilder.Build(new string('a', 64), MaterialKind.Timetable,
            [RecoveryPipelineTests.Layout(MaterialKind.Timetable)], (_, _) => true);
        var run = await RecoveryEngine.RunAsync(document, "windows", 10, true, [], _ => null);
        Assert.Empty(run.Errors);
        return new("fictional-source", new(1, new(2026, 1)), document, Assert.IsType<RecoveryResult>(run.Result), DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task DisplayOwnsImmutableValuesAfterOriginalPreviewCollectionsChange()
    {
        var preview = await Preview();
        var classes = preview.Document.Classes.ToArray(); var days = preview.Document.Days.ToArray();
        var cells = preview.Document.Cells.ToArray(); var slots = cells[0].Slots.ToArray();
        cells[0] = cells[0] with { Slots = slots };
        var resultCells = preview.Result.Cells.ToArray(); var lessons = resultCells[0].Lessons.ToArray();
        resultCells[0] = resultCells[0] with { Lessons = lessons };
        var times = preview.Document.Times.ToDictionary(); var spanTimes = preview.Document.SpanTimes.ToDictionary();
        preview = preview with { Document = preview.Document with { Classes = classes, Days = days, Cells = cells, Times = times, SpanTimes = spanTimes },
            Result = preview.Result with { Cells = resultCells } };
        var display = Assert.IsType<RecoveryPreviewDisplay>(RecoveryPreviewDisplay.Create(preview, CancellationToken.None));
        var subject = display.Cells[0].Lessons[0].Subject; var slot = display.Cells[0].Slots[0];
        Assert.Equal(40, display.Cells.Length); Assert.Equal(RecoveryValidator.Fingerprint(preview.Result), display.ResultHash);
        classes[0] = "架空改変クラス"; days[0] = "9"; slots[0] = new("架空改変クラス", "9", 8);
        lessons[0] = lessons[0] with { Subject = lessons[0].Subject with { Value = "架空改変科目" } };
        times["1:1"] = "架空改変時刻"; spanTimes["1:1-2"] = "架空改変連続時刻";
        Assert.Equal("3_CN", display.Classes[0]); Assert.Equal("1", display.Days[0]);
        Assert.Equal(slot, display.Cells[0].Slots[0]); Assert.Equal(subject, display.Cells[0].Lessons[0].Subject);
        Assert.Empty(display.Times); Assert.Empty(display.SpanTimes);
    }

    [Fact]
    public async Task DisplayRejectsInvalidOrOldValidatorPreviewAndHonorsCancellation()
    {
        var preview = await Preview();
        Assert.Null(RecoveryPreviewDisplay.Create(preview with { Result = preview.Result with { PdfHash = new string('b', 64) } }, CancellationToken.None));
        Assert.Null(RecoveryPreviewDisplay.Create(preview with { Result = preview.Result with { Metadata = preview.Result.Metadata with { ValidatorVersion = 0 } } }, CancellationToken.None));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => RecoveryPreviewDisplay.Create(preview, cancellation.Token));
    }
}
