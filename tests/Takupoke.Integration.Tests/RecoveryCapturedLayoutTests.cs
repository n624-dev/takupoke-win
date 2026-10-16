using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class RecoveryCapturedLayoutTests
{
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static RecoveryReadCapture Capture(PdfPageLayout page, bool finish = true)
    {
        var capture = new RecoveryReadCapture(); capture.Begin(1);
        capture.Record(1, RecoveryInputState.Complete, page); if (finish) capture.Finish(); return capture;
    }
    [Theory]
    [InlineData(MaterialKind.Timetable)] [InlineData(MaterialKind.Exam)] [InlineData(MaterialKind.ExamReturn)]
    public async Task CompleteFilledVectorCellsKeepOriginalTextAndCoordinatesWithoutRaster(MaterialKind kind)
    {
        var page = RecoveryPipelineTests.Layout(kind);
        var template = RecoveryDocumentBuilder.Build(Hash, kind, [page], (_, _) => true);
        var glyphs = page.Glyphs.ToList(); var ordinal = 0;
        foreach (var cell in template.Cells.Where(c => c.ConfirmedEmpty))
        {
            foreach (var (label, value, offset) in new[] { ("科目", "架空科目" + ordinal, 5), ("教員", "架空教員" + ordinal, 25), ("教室", "架空教室" + ordinal, 45) })
            {
                glyphs.Add(new(label, cell.Box.X + 4, cell.Box.Y + offset, 4, 8));
                glyphs.Add(new(value, cell.Box.X + 18, cell.Box.Y + offset, value.Length * 2, 8));
            }
            ordinal++;
        }
        page = page with { Glyphs = glyphs };
        var expected = RecoveryDocumentBuilder.Build(Hash, kind, [page], (_, _) => throw new Exception("Filled vector cells do not need blank proof"), allowStructureProposal: true);
        var document = Assert.IsType<RecoveryDocument>(RecoveryCapturedLayoutBuilder.TryBuildWithoutRaster(Hash, kind, Capture(page)));
        Assert.Equal(RecoveryValidator.Fingerprint(expected), RecoveryValidator.Fingerprint(document));
        Assert.All(document.Sources, s => Assert.False(s.FromOcr)); Assert.Null(document.Capture);
        var run = await RecoveryEngine.RunAsync(document, "windows", 10, true, [], _ => null);
        Assert.True(RecoveryValidator.Validate(document, Assert.IsType<RecoveryResult>(run.Result)).CanAdopt);
    }
    [Fact]
    public void BlankCellsStillRequireRasterEvidenceAndIncompleteReaderOutputCannotBeComplete()
    {
        var page = RecoveryPipelineTests.Layout(MaterialKind.Timetable);
        Assert.Null(RecoveryCapturedLayoutBuilder.TryBuildWithoutRaster(Hash, MaterialKind.Timetable, Capture(page)));
        Assert.Null(RecoveryCapturedLayoutBuilder.TryBuildWithoutRaster(Hash, MaterialKind.Timetable, Capture(page, finish: false)));
        var partial = Capture(page); partial.Record(1, RecoveryInputState.Partial, page);
        Assert.Null(RecoveryCapturedLayoutBuilder.TryBuildWithoutRaster(Hash, MaterialKind.Timetable, partial));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => RecoveryCapturedLayoutBuilder.TryBuildWithoutRaster(Hash, MaterialKind.Timetable, partial, cancellation.Token));
    }
    [Fact]
    public void CompleteButInvalidGeometryIsRejectedInsteadOfBeingHiddenByRasterFallback()
    {
        var page = RecoveryPipelineTests.Layout(MaterialKind.Timetable);
        page = page with { Glyphs = page.Glyphs.Select((g, i) => i == 0 ? g with { X = -1 } : g).ToArray() };
        Assert.Throws<PdfParseException>(() => RecoveryCapturedLayoutBuilder.TryBuildWithoutRaster(Hash, MaterialKind.Timetable, Capture(page)));
    }
}
