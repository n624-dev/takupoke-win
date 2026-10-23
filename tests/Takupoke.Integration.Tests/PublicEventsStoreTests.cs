using System.Text;
using Takupoke.Infrastructure.Api;
using Takupoke.Infrastructure.Storage;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class PublicEventsStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "takupoke-events-" + Guid.NewGuid().ToString("N"));
    private static SavedEvents Saved(int year) => new(DateTimeOffset.UtcNow,
        new("v1", year, new string('a', 64), null, [new($"{year}-04-01", $"{year}-04-01", "架空の行事", "行事メモ")]), "\"fake-tag\"");

    [Fact]
    public async Task AcceptedUtf8PayloadSurvivesAnEscapedSavedEnvelope()
    {
        var title = string.Concat(Enumerable.Repeat("😀", 100));
        var candidate = new EventsPayload("v1", 2032, new string('a', 64), null,
            Enumerable.Range(0,1800).Select(_ => new EventRow("2032-04-01", "2032-04-01", title, "行事メモ")).ToArray());
        var raw = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(DataCodec.Encode(candidate)).Replace("\\uD83D\\uDE00", "😀", StringComparison.OrdinalIgnoreCase));
        Assert.True(raw.Length < 1_000_000);
        var accepted = ApiPayloads.Events(raw, 2032);
        var store = new PublicEventsStore(_root);
        await store.SaveAsync(new(DateTimeOffset.UtcNow, accepted, "\"fictional\""));
        var read = await new PublicEventsStore(_root).LoadAvailableAsync(2032);
        Assert.False(read.Failed); Assert.NotNull(read.Events);
        Assert.Equal(DataCodec.Encode(accepted), DataCodec.Encode(read.Events.Payload));
        var path = Path.Combine(_root, "public-events", "2032.json");
        var preserved = await File.ReadAllBytesAsync(path);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(read.Events with { ApiETag = "invalid" }));
        Assert.Equal(preserved, await File.ReadAllBytesAsync(path));
    }

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
