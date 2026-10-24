using System.Net;
using System.Net.Http.Headers;
using Takupoke.Infrastructure.Api;
using Takupoke.Infrastructure.Storage;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class EventSourceCheckerTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.Parse("2032-05-01T03:04:05.842Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls, Unexpected;
        public HttpStatusCode HttpStatus = HttpStatusCode.OK;
        public EventSourceStatus Status = Matched();
        public byte[]? WireOverride;
        public TaskCompletionSource? Started, Release;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            if (request.Method != HttpMethod.Get || request.RequestUri!.Host != "example.invalid" ||
                request.RequestUri.AbsolutePath != "/events/source-status" || request.RequestUri.Query != "?schoolYear=2032" ||
                request.Headers.Authorization is not null || request.Headers.IfNoneMatch.Count != 0)
            { Unexpected++; throw new InvalidOperationException("Unexpected metadata URL/method/headers"); }
            Started?.TrySetResult(); if (Release is { } wait) await wait.Task.WaitAsync(token);
            return new(HttpStatus) { Content = new ByteArrayContent(WireOverride ?? DataCodec.Encode(Status))
                { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } } };
        }
    }
    private static EventSourceStatus Matched() => new("v1", 2032, "sha256-" + new string('b', 64), new('a', 64),
        "matched", "matched", DateTimeOffset.Parse("2032-05-01T03:04:05.842Z"), null, null, false, false);
    private static SavedEvents Events(string? pdfETag = null, string? version = null) => new(DateTimeOffset.UnixEpoch,
        new("v1", 2032, new('a', 64), pdfETag, [new("2032-04-01", "2032-04-01", "架空行事A", "行事")]),
        "W/\"" + new string('b', 64) + "\"", version);
    private sealed class Fixture : IDisposable
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(), "event-status-" + Guid.NewGuid().ToString("N"));
        public readonly Handler Handler = new();
        public readonly Clock Clock = new();
        public readonly HttpClient Http;
        public readonly PublicEventsStore Store;
        public EventSourceChecker Checker => new(new(Http, new("https://example.invalid/")), Store, Clock);
        public Fixture() { Directory.CreateDirectory(Root); Http = new(Handler); Store = new(Root); }
        public void Dispose() { Assert.Equal(0, Handler.Unexpected); Http.Dispose(); Directory.Delete(Root, true); }
    }
    [Theory]
    [InlineData("matched", null)]
    [InlineData("source_differs", "内容は確認待ち")]
    [InlineData("unknown", "未確認")]
    [InlineData("unavailable", "確認できませんでした")]
    public async Task MetadataChecksOnlyApiAndDistinguishesResultsWithoutUsingPdfEtag(string status, string? warning)
    {
        using var f = new Fixture();
        f.Handler.Status = Matched() with { Status = status, LastSuccessfulStatus = status == "source_differs" ? status : "matched" };
        var result = await f.Checker.CheckAsync(Events("\"different-pdf-etag\""));
        Assert.False(result.Failed); Assert.Equal(1, f.Handler.Calls);
        if (warning is null) Assert.Null(result.Warning(Events(), f.Clock.Now));
        else Assert.Contains(warning, result.Warning(Events(), f.Clock.Now));
        Assert.Contains("最終成功確認", result.Description(Events(), f.Clock.Now));
        Assert.Equal("\"different-pdf-etag\"", Events("\"different-pdf-etag\"").Payload.SourcePdfETag);
    }
    [Fact]
    public async Task VersionHashAndYearMustMatchBeforeAnObservationCanApply()
    {
        using var f = new Fixture();
        var result = await f.Checker.CheckAsync(Events());
        Assert.Null(result.Warning(Events(), f.Clock.Now));
        Assert.NotNull(result.Warning(Events() with { Payload = Events().Payload with { SchoolYear = 2033 } }, f.Clock.Now));
        Assert.NotNull(result.Warning(Events() with { Payload = Events().Payload with { SourcePdfSha256 = new('c',64) } }, f.Clock.Now));
        Assert.NotNull(result.Warning(Events(version: "sha256-" + new string('c',64)), f.Clock.Now));
        Assert.NotNull(result.Warning(Events() with { ApiETag = "\"arbitrary\"" }, f.Clock.Now));
        Assert.DoesNotContain("最終成功確認", result.Description(Events() with { ApiETag = null }, f.Clock.Now));
    }
    [Fact]
    public async Task TwelveHourPersistentCacheCoalescesWaitersForceBypassesAndFailureKeepsPreviousSuccess()
    {
        using var f = new Fixture(); var checker = f.Checker;
        await f.Store.SaveAsync(Events());
        var eventsFile = Path.Combine(f.Root, "public-events", "2032.json");
        var originalBytes = await File.ReadAllBytesAsync(eventsFile);
        f.Handler.Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Handler.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = checker.CheckAsync(Events()); await f.Handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = checker.CheckAsync(Events()); f.Handler.Release.SetResult();
        var result = await first; Assert.Equal(result, await second); Assert.Equal(1, f.Handler.Calls);
        Assert.Equal(result, await f.Checker.CheckAsync(Events())); Assert.Equal(1, f.Handler.Calls);
        f.Clock.Now = f.Clock.Now.AddHours(12).AddSeconds(-1);
        await checker.CheckAsync(Events()); Assert.Equal(1, f.Handler.Calls);
        f.Clock.Now = f.Clock.Now.AddSeconds(1);
        await checker.CheckAsync(Events()); Assert.Equal(2, f.Handler.Calls);
        f.Handler.HttpStatus = HttpStatusCode.ServiceUnavailable;
        var failed = await checker.CheckAsync(Events(), force: true);
        Assert.True(failed.Failed); Assert.Equal(result.Saved?.Response.LastSuccessAt, failed.Saved?.Successful?.LastSuccessAt);
        Assert.True((await checker.CheckAsync(Events())).Failed); Assert.Equal(3, f.Handler.Calls);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(eventsFile));
        f.Handler.HttpStatus = HttpStatusCode.OK;
        await checker.CheckAsync(Events(), force: true); Assert.Equal(4, f.Handler.Calls);
    }
    [Fact]
    public async Task CheckingUsesSixtySecondsAndUnavailableOldEdgesPreserveNewestSuccessfulResult()
    {
        using var f = new Fixture(); var checker = f.Checker;
        f.Handler.Status = Matched() with { Checking = true };
        await checker.CheckAsync(Events());
        f.Clock.Now = f.Clock.Now.AddSeconds(59); await checker.CheckAsync(Events()); Assert.Equal(1, f.Handler.Calls);
        f.Clock.Now = f.Clock.Now.AddSeconds(1); await checker.CheckAsync(Events()); Assert.Equal(2, f.Handler.Calls);
        f.Handler.Status = Matched() with { Status = "unavailable", LastSuccessfulStatus = null, LastSuccessAt = null, Checking = false };
        var unavailable = await checker.CheckAsync(Events(), force: true);
        Assert.NotNull(unavailable.Saved?.Successful?.LastSuccessAt);
        var restored = await f.Store.LoadSourceStatusAsync(2032); Assert.Equal(unavailable.Saved, restored);
        f.Handler.Status = Matched() with { LastSuccessAt = Matched().LastSuccessAt!.Value.AddHours(-1) };
        var old = await checker.CheckAsync(Events(), force: true);
        Assert.NotNull(old.Saved?.PreviousSuccess); Assert.Contains("以前の結果", old.Warning(Events(), f.Clock.Now));
    }
    [Fact]
    public async Task InvalidMetadataAndCorruptAdvisoryCacheCannotDestroyAnnualEvents()
    {
        using var f = new Fixture(); await f.Store.SaveAsync(Events());
        var file = Path.Combine(f.Root, "public-events", "source-status-2032.json");
        await File.WriteAllTextAsync(file, "{fictional-broken");
        foreach (var invalid in new[] { Matched() with { SchoolYear = 2033 }, Matched() with { DataVersion = "wrong" },
            Matched() with { AdoptedSourcePdfSha256 = "wrong" }, Matched() with { Status = "invented" },
            Matched() with { LastSuccessAt = null }, Matched() with { LastSuccessfulStatus = "unavailable" } })
        {
            f.Handler.Status = invalid; Assert.True((await f.Checker.CheckAsync(Events(), force: true)).Failed);
            Assert.Equal(DataCodec.Encode(Events()), DataCodec.Encode(await f.Store.LoadAsync(2032)));
        }
        Assert.Equal(new[] {2032}, f.Store.SavedYears());
        f.Handler.Status = Matched(); Assert.False((await f.Checker.CheckAsync(Events())).Failed);
        Assert.NotNull(await f.Store.LoadSourceStatusAsync(2032));
    }
    [Fact]
    public async Task MissingCheckingOrStaleCannotBecomeAnApparentlyFreshMatch()
    {
        using var f = new Fixture(); await f.Store.SaveAsync(Events());
        foreach (var field in new[] { "checking", "stale" })
        {
            var properties = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,System.Text.Json.JsonElement>>(DataCodec.Encode(Matched()))!;
            properties.Remove(field); f.Handler.WireOverride = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(properties);
            var result = await f.Checker.CheckAsync(Events(), force: true);
            Assert.True(result.Failed); Assert.Null(result.Saved);
            Assert.NotNull(result.Warning(Events(), f.Clock.Now));
            Assert.Equal(DataCodec.Encode(Events()), DataCodec.Encode(await f.Store.LoadAsync(2032)));
        }
    }

    [Fact]
    public async Task CallerCancellationDoesNotReplaceAHealthyObservation()
    {
        using var f = new Fixture(); var checker = f.Checker;
        var first = await checker.CheckAsync(Events());
        f.Handler.Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Handler.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancel = new CancellationTokenSource();
        var request = checker.CheckAsync(Events(), true, cancel.Token);
        await f.Handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.Equal(first.Saved, await f.Store.LoadSourceStatusAsync(2032));
    }
}
