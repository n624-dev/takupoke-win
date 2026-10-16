using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Recovery;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class RecoveryContainmentIndexTests
{
    private sealed record Source(int Id, RecoveryBox Box);

    [Fact]
    public void IndexedSelectionEqualsCompleteScanIncludingOrderDuplicatesAndBoundaries()
    {
        var random = new Random(7319);
        var values = Enumerable.Range(0, 3000).Select(i => new Source(i,
            new(random.NextDouble() * 2048, random.NextDouble() * 2048,
                .001 + random.NextDouble() * 180, .001 + random.NextDouble() * 180))).ToList();
        var boundary = new Source(3000, new(64, 128, 64, 64));
        values.Insert(1, boundary); values.Add(boundary);
        values.Add(new(3001, new(1455.7052633975009, 805, 95.39151890432763, 8)));
        values.Add(new(3002, new(1e300, 1e300, 1e290, 1e290)));
        var index = new RecoveryContainmentIndex<Source>(values, value => value.Box, _ => { });
        var queries = Enumerable.Range(0, 200).Select(_ => new RecoveryBox(
            random.NextDouble() * 2048, random.NextDouble() * 2048,
            .001 + random.NextDouble() * 500, .001 + random.NextDouble() * 500)).Concat(new[] {
                new RecoveryBox(64, 128, 64, 64), new(63, 127, 65, 65), new(128, 128, 64, 64),
                new(1455.7052633975009, 805, 95.39151890432763, 8), new(1e300, 1e300, 1e290, 1e290),
                new(0, 0, 1e301, 1e301), new(double.NaN, 0, 1, 1)
            });
        foreach (var query in queries)
            Assert.Equal(values.Where(value => query.Contains(value.Box)), index.Contained(query));
    }

    [Fact]
    public void LocalQueryChargesRelevantCandidatesAndPreservesCancellationAndBudgetErrors()
    {
        var values = Enumerable.Range(0, 20000).Select(i => new Source(i, new(i % 200 * 10, i / 200 * 10, 1, 1))).ToArray();
        long comparisons = 0;
        var index = new RecoveryContainmentIndex<Source>(values, value => value.Box, count => comparisons += count);
        comparisons = 0;
        Assert.Equal(values.Where(value => new RecoveryBox(1000, 500, 40, 40).Contains(value.Box)), index.Contained(new(1000, 500, 40, 40)));
        Assert.True(comparisons < 1500, $"Local selection charged {comparisons} comparisons.");
        using var cancellation = new CancellationTokenSource();
        var cancellable = new RecoveryContainmentIndex<Source>(values, value => value.Box,
            _ => cancellation.Token.ThrowIfCancellationRequested());
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => cancellable.Contained(new(0, 0, 2048, 2048)));
        Assert.Throws<InvalidDataException>(() => new RecoveryContainmentIndex<Source>(values,
            value => value.Box, _ => throw RecoveryWorkLimits.Exceeded("bounded")));
    }
}
