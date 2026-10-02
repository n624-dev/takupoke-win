using Takupoke.Infrastructure.Api;
using Takupoke.Infrastructure.Storage;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class PublicEventsStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "takupoke-events-" + Guid.NewGuid().ToString("N"));
    private static SavedEvents Saved(int year) => new(DateTimeOffset.UtcNow,
        new("v1", year, new string('a', 64), null, [new($"{year}-04-01", $"{year}-04-01", "架空の行事", "行事メモ")]), "\"fake-tag\"");

    [Theory]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("{\"payload\":{}}")]
    [InlineData("{\"payload\":{\"version\":\"v1\",\"schoolYear\":2026,\"sourcePdfSha256\":null,\"events\":null}}")]
    public async Task DamagedYearDoesNotBlockHealthyYearsAndCanBeReplaced(string malformed)
    {
        var store = new PublicEventsStore(_root);
        await store.SaveAsync(Saved(2025));
        var broken = Path.Combine(_root, "public-events", "2026.json");
        await File.WriteAllTextAsync(broken, malformed);
        var read = await store.LoadAvailableAsync(2026);
        Assert.True(read.Failed); Assert.Null(read.Events);
        Assert.Equal(malformed, await File.ReadAllTextAsync(broken));
        Assert.Equal(2025, (await store.LoadAvailableAsync(2025)).Events!.Payload.SchoolYear);
        await store.SaveAsync(Saved(2026));
        var recovered = await store.LoadAvailableAsync(2026);
        Assert.False(recovered.Failed); Assert.Equal(2026, recovered.Events!.Payload.SchoolYear);
        Assert.Equal(2025, (await store.LoadAsync(2025))!.Payload.SchoolYear);
    }

    [Fact]
    public async Task CancellationIsNotReportedAsDamagedData()
    {
        var store = new PublicEventsStore(_root);
        await store.SaveAsync(Saved(2026));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.LoadAvailableAsync(2026, cancellation.Token));
        Assert.NotNull(await store.LoadAsync(2026));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
