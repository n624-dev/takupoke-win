using Takupoke.Core.Recovery;
using Xunit;

namespace Takupoke.Core.Tests;
public partial class RecoveryTests
{
    [Fact]
    public void SpatialIndexCannotDropAMinuteIntersectionThroughRoundedUnionBounds()
    {
        var (doc, result) = Fixture();
        const double origin = 475.18905785360323, sourceX = 1455.7052633975009, width = 95.39151890432763;
        var edge = origin + (sourceX + width - origin);
        Assert.True(sourceX + width > edge);
        var sources = doc.Sources.Select(s => s with { Box = s.Box with { X = s.Box.X + origin } }).Append(new RecoverySource("cross", "unassigned", 1, "架空の未割当文字", new(sourceX, 805, width, 8))).ToArray();
        var cells = doc.Cells.Select((c, i) => c with { Box = c.Box with { X = i == 39 ? edge : c.Box.X + origin } }).ToArray();
        Assert.Contains("unassignedCellText", RecoveryValidator.Validate(doc with { Sources = sources, Cells = cells }, result).Errors);
    }
    [Fact]
    public void TallSourceFromAnotherCellStillIntersectsFarAwayCells()
    {
        var (doc, result) = Fixture();
        var source = new RecoverySource("cross", "anotherCell", 1, "架空の未割当文字", new(520, 0, 2, 899));
        Assert.Contains("unassignedCellText", RecoveryValidator.Validate(doc with { Sources = doc.Sources.Append(source).ToArray() }, result).Errors);
    }
    [Fact]
    public async Task ExcessiveGeometryWorkStopsBeforeAnyModelIsQueried()
    {
        var (doc, result) = Fixture();
        var cells = Enumerable.Range(0, 10000).Select(i => doc.Cells[0] with { Id = "duplicate-geometry-" + i }).ToArray();
        var provider = new ProbeProvider("windowsLanguageModel", result.Metadata);
        doc = doc with { Cells = cells };
        Assert.Contains("validationLimit", RecoveryValidator.Validate(doc, result).Errors);
        var run = await RecoveryEngine.RunAsync(doc, "windows", 10, true, [provider], _ => null);
        Assert.Equal(RecoveryJobState.Failed, run.State); Assert.Contains("validationLimit", run.Errors);
        Assert.Equal(0, provider.AvailabilityCalls); Assert.Equal(0, provider.RecoveryCalls);
    }
    [Fact]
    public async Task TotalOriginalTextWorkIsBoundedBeforeConcatenationOrModelFallback()
    {
        var (doc, result) = Fixture(); var text = new string('A', 4096);
        var extra = Enumerable.Range(0, 5000).Select(i => new RecoverySource("large-text-" + i, "unassigned", 1, text, new(2000, 2000, 1, 1)));
        doc = doc with { Sources = doc.Sources.Concat(extra).ToArray() };
        Assert.Contains("validationLimit", RecoveryValidator.Validate(doc, result).Errors);
        var provider = new ProbeProvider("windowsLanguageModel", result.Metadata);
        var run = await RecoveryEngine.RunAsync(doc, "windows", 10, true, [provider], _ => null);
        Assert.Equal(RecoveryJobState.Failed, run.State); Assert.Contains("validationLimit", run.Errors);
        Assert.Equal(0, provider.AvailabilityCalls); Assert.Equal(0, provider.RecoveryCalls);
    }
    [Fact]
    public void InvalidSourceCoordinatesAreRejectedBeforeSpatialIndexing()
    {
        var (doc, result) = Fixture();
        foreach (var coordinate in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Assert.Contains("sourceLimit", RecoveryValidator.Validate(doc with { Sources = doc.Sources.Select((s, i) => i == 0 ? s with { Box = s.Box with { X = coordinate } } : s).ToArray() }, result).Errors);
    }
}
