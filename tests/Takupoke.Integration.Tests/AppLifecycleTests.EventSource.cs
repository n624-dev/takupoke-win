using System.Collections.Concurrent;
using System.Text;
using Takupoke.Core;
using Takupoke.Infrastructure.Api;
using Takupoke.Infrastructure.Storage;
using Takupoke.Win.ViewModels;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed partial class AppLifecycleTests
{
    private sealed class SourceDelayGate : IDisposable
    {
        public sealed record Wait(TimeSpan Delay, TaskCompletionSource Completion);
        private readonly ConcurrentQueue<Wait> _waits = new();
        private readonly SemaphoreSlim _ready = new(0);
        public int Scheduled;
        public int Pending => _waits.Count;
        public Task Delay(TimeSpan delay, CancellationToken token)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waits.Enqueue(new(delay, completion)); Interlocked.Increment(ref Scheduled); _ready.Release();
            return completion.Task.WaitAsync(token);
        }
        public async Task<Wait> Next()
        {
            Assert.True(await _ready.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(_waits.TryDequeue(out var value));
            Assert.InRange(value.Delay, TimeSpan.Zero, TimeSpan.FromSeconds(60));
            return value;
        }
        public void Dispose()
        {
            while (_waits.TryDequeue(out var value)) value.Completion.TrySetCanceled();
            _ready.Dispose();
        }
    }

    private sealed class SourceFollowFixture : IAsyncDisposable
    {
        public readonly Fixture App = new();
        public readonly SourceDelayGate Gate = new();
        public readonly HomeClockProvider Clock = new();
        public readonly EventStatusHandler Handler = new() { Checking = true };
        public readonly HttpClient Network;
        public readonly PublicEventsStore Events;
        public AppViewModel Model = null!;
        private SourceFollowFixture() { Network = new(Handler); Events = new(App.Root); Handler.MetadataClock = Clock; }
        public static async Task<SourceFollowFixture> Create()
        {
            var f = new SourceFollowFixture();
            try
            {
                await f.App.Preferences();
                var hash = NotificationDiff.Digest(Encoding.UTF8.GetBytes(f.Handler.EventBody(2032)));
                await f.Events.SaveAsync(new(f.Clock.Now, new("v1", 2032, new('a',64), "\"fictional-new-source\"",
                    [new("2032-04-01", "2032-04-01", "架空年度別行事", "行事メモ")]), "W/\"" + hash + "\"", "sha256-" + hash));
                f.Model = f.App.Create(f.Events, f.Network, new("https://example.invalid/"), f.Clock, f.Gate.Delay);
                await f.Model.InitializeLocalAsync(); await f.Model.CheckEventSourceAsync(2032);
                Assert.Equal(1, f.Handler.MetadataCalls);
                return f;
            }
            catch { await f.DisposeAsync(); throw; }
        }
        public async Task Step()
        {
            var next = await Gate.Next();
            var updated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task? owned = null;
            void Changed() { owned = Model.CheckEventSourceAsync(2032); updated.TrySetResult(); }
            Model.SnapshotChanged += Changed;
            try
            {
                Clock.Now += next.Delay; next.Completion.TrySetResult();
                await updated.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Assert.IsAssignableFrom<Task>(owned);
            }
            finally { Model.SnapshotChanged -= Changed; }
        }
        public async ValueTask DisposeAsync()
        { await App.DisposeAsync(); Network.Dispose(); Gate.Dispose(); }
    }

    [Fact]
    public async Task CheckingAutomaticallyBecomesMatchedWithoutReplacingOrFetchingEvents()
    {
        await using var f = await SourceFollowFixture.Create();
        var data = f.Model.Data; var saved = DataCodec.Encode(await f.Events.LoadAsync(2032));
        Assert.False(f.Model.Busy); Assert.Contains("確認中", f.Model.EventSourceMessage);
        f.Handler.Checking = false; await f.Step();
        Assert.Equal(2, f.Handler.MetadataCalls); Assert.Equal(0, f.Handler.EventCalls);
        Assert.Null(f.Model.EventSourceMessage); Assert.Contains("登録元と一致", f.Model.EventSourceDescription(2032));
        Assert.Same(data, f.Model.Data); Assert.Equal(saved, DataCodec.Encode(await f.Events.LoadAsync(2032)));
        Assert.Equal(0, f.Gate.Pending); Assert.Equal(1, f.Gate.Scheduled);
    }

    [Fact]
    public async Task CheckingStopsAfterThreeAdditionalRequestsAndKeepsSuccessfulEvidence()
    {
        await using var f = await SourceFollowFixture.Create();
        for (var count = 2; count <= 4; count++) { await f.Step(); Assert.Equal(count, f.Handler.MetadataCalls); }
        Assert.Equal(3, f.Gate.Scheduled); Assert.Equal(0, f.Gate.Pending);
        Assert.Contains("現在の更新状況は未確認", f.Model.EventSourceMessage);
        Assert.Contains("最終成功確認", f.Model.EventSourceDescription(2032));
        f.Model.SetEventSourceChecksActive(false); f.Clock.Now = f.Clock.Now.AddMinutes(2);
        f.Model.SetEventSourceChecksActive(true);
        for (var call = 0; call < 5; call++) await f.Model.CheckEventSourceAsync(2032);
        Assert.Equal(4, f.Handler.MetadataCalls); Assert.Equal(0, f.Gate.Pending);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(120)]
    public async Task ForegroundResumeKeepsTheRemainingRequestBudgetAfterShortOrLongPauses(int seconds)
    {
        await using var f = await SourceFollowFixture.Create();
        await f.Step(); Assert.Equal(2, f.Handler.MetadataCalls);
        f.Model.SetEventSourceChecksActive(false);
        (await f.Gate.Next()).Completion.TrySetResult();
        f.Clock.Now = f.Clock.Now.AddSeconds(seconds);
        f.Model.SetEventSourceChecksActive(true);
        await f.Step(); await f.Step();
        Assert.Equal(4, f.Handler.MetadataCalls); Assert.Equal(0, f.Gate.Pending);
        Assert.Contains("現在の更新状況は未確認", f.Model.EventSourceMessage);
    }

    [Fact]
    public async Task AFailedFollowUpStopsWithoutReplacingEventsOrLastSuccessfulResult()
    {
        await using var f = await SourceFollowFixture.Create();
        var saved = DataCodec.Encode(await f.Events.LoadAsync(2032));
        f.Handler.MetadataFails = true; await f.Step();
        Assert.Equal(2, f.Handler.MetadataCalls); Assert.Equal(0, f.Gate.Pending);
        Assert.Contains("確認できませんでした", f.Model.EventSourceMessage);
        Assert.Contains("最終成功確認", f.Model.EventSourceDescription(2032));
        Assert.Equal(saved, DataCodec.Encode(await f.Events.LoadAsync(2032)));
    }

    [Fact]
    public async Task BackgroundingCancelsAnInFlightAutomaticRequestWithoutPublishingIt()
    {
        await using var f = await SourceFollowFixture.Create();
        var data = f.Model.Data;
        f.Handler.MetadataStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Handler.MetadataRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var wait = await f.Gate.Next(); f.Clock.Now += wait.Delay; wait.Completion.SetResult();
        await f.Handler.MetadataStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var running = f.Model.CheckEventSourceAsync(2032);
        f.Model.SetEventSourceChecksActive(false); await running;
        f.Handler.MetadataRelease.TrySetResult();
        Assert.Equal(2, f.Handler.MetadataCalls); Assert.Equal(0, f.Gate.Pending);
        Assert.Same(data, f.Model.Data);
        await f.Model.CheckEventSourceAsync(2032, force: true); Assert.Equal(2, f.Handler.MetadataCalls);
    }

    [Fact]
    public async Task ManualCheckReplacesThePendingTimerWithoutAddingAnAutomaticRequest()
    {
        await using var f = await SourceFollowFixture.Create();
        f.Handler.Checking = false;
        await f.Model.CheckEventSourceAsync(2032, force: true);
        (await f.Gate.Next()).Completion.TrySetResult();
        Assert.Equal(2, f.Handler.MetadataCalls); Assert.Equal(0, f.Gate.Pending);
        Assert.Null(f.Model.EventSourceMessage);
    }

    [Fact]
    public async Task NewEventBytesCancelTheOldTimerAndMismatchedMetadataCannotScheduleOne()
    {
        await using var f = await SourceFollowFixture.Create();
        f.Handler.Updated = true; f.Handler.WrongVersion = true;
        await f.Model.FetchEventsAsync(2032); await f.Model.CheckEventSourceAsync(2032);
        (await f.Gate.Next()).Completion.TrySetResult();
        Assert.Equal(2, f.Handler.MetadataCalls); Assert.Equal(0, f.Gate.Pending);
        Assert.Equal("架空更新行事", f.Model.EventRecords[2032].Payload.Events[0].Title);
        Assert.Contains("配信版が一致しません", f.Model.EventSourceMessage);
    }
}
