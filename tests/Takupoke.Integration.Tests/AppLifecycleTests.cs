using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Takupoke.Core;
using Takupoke.Infrastructure.Materials;
using Takupoke.Infrastructure.Storage;
using Takupoke.Win;
using Takupoke.Win.Platform;
using Takupoke.Win.ViewModels;
using Xunit;

namespace Takupoke.Integration.Tests;

[CollectionDefinition("Application lifecycle", DisableParallelization = true)]
public sealed class AppLifecycleCollection { }

[Collection("Application lifecycle")]
public sealed class AppLifecycleTests
{
    private sealed class Protector : IKeyProtector, IDisposable
    {
        private readonly EnvelopeCipher _cipher = new(RandomNumberGenerator.GetBytes(32));
        public byte[] Protect(byte[] key) => _cipher.Encrypt(key, "fictional-platform-key");
        public byte[] Unprotect(byte[] bytes) => _cipher.Decrypt(bytes, "fictional-platform-key");
        public void Dispose() => _cipher.Dispose();
    }
    private sealed class FakeIdentity : IFileIdentityProvider
    {
        public Action? Before;
        public int Reads;
        public string Identity(FileStream stream)
        {
            Interlocked.Increment(ref Reads);
            Interlocked.Exchange(ref Before, null)?.Invoke();
            return Path.GetFullPath(stream.Name);
        }
    }
    private sealed class EventStatusHandler : HttpMessageHandler
    {
        public HashSet<int> FailedYears { get; } = [2026];
        public int Metadata2026 { get; private set; }
        public int MetadataCalls { get; private set; }
        public TaskCompletionSource? MetadataStarted, MetadataRelease;
        public bool Updated;
        private string Body(int year) => System.Text.Json.JsonSerializer.Serialize(new {
            version = "v1", schoolYear = year, sourcePdfSha256 = new string('a',64), sourcePdfETag = "\"fictional-new-source\"",
            events = new[] { new { startDate = $"{year}-04-01", endDate = $"{year}-04-01", title = Updated ? "架空更新行事" : "架空年度別行事", tag = "行事メモ" } } });
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Assert.Equal("example.invalid", request.RequestUri!.Host);
            Assert.Equal(HttpMethod.Get, request.Method);
            var year = int.Parse(request.RequestUri.Query.Split('=').Last());
            var body = Body(year);
            if (request.RequestUri.AbsolutePath == "/events/source-status")
            {
                MetadataCalls++; if (year == 2026) Metadata2026++;
                MetadataStarted?.TrySetResult();
                if (MetadataRelease is { } release) await release.Task.WaitAsync(token);
                var status = new Takupoke.Infrastructure.Api.EventSourceStatus("v1", year,
                    "sha256-" + NotificationDiff.Digest(System.Text.Encoding.UTF8.GetBytes(body)), new('a',64),
                    "matched", "matched", DateTimeOffset.UtcNow, null, null, false, false);
                return new(System.Net.HttpStatusCode.OK) { Content = new StringContent(
                    System.Text.Encoding.UTF8.GetString(DataCodec.Encode(status)), System.Text.Encoding.UTF8, "application/json") };
            }
            Assert.Equal("/events", request.RequestUri.AbsolutePath);
            if (FailedYears.Contains(year)) return new(System.Net.HttpStatusCode.ServiceUnavailable);
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
            response.Headers.ETag = new("\"" + NotificationDiff.Digest(System.Text.Encoding.UTF8.GetBytes(body)) + "\"", isWeak: true); return response;
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string? _previousMode = Environment.GetEnvironmentVariable("TAKUPOKE_OFFLINE_TEST_MODE");
        private readonly string? _previousRoot = Environment.GetEnvironmentVariable("TAKUPOKE_DATA_ROOT");
        public readonly string Root = Path.Combine(Path.GetTempPath(), "takupoke-app-lifecycle-" + Guid.NewGuid().ToString("N"));
        public readonly Protector Protector = new();
        public readonly FakeIdentity Identity = new();
        public readonly DispatcherQueue Dispatcher = new();
        private readonly List<AppViewModel> _models = [];
        public Fixture()
        {
            Directory.CreateDirectory(Root);
            Environment.SetEnvironmentVariable("TAKUPOKE_OFFLINE_TEST_MODE", "1");
            Environment.SetEnvironmentVariable("TAKUPOKE_DATA_ROOT", Root);
            WindowsNotifications.Delivered.Clear();
        }
        public AppViewModel Create(PublicEventsStore? events = null, HttpClient? network = null,
            Uri? apiBaseUri = null, TimeProvider? clock = null)
        { var model = new AppViewModel(Dispatcher, Protector, Identity, events, network, apiBaseUri, clock); _models.Add(model); return model; }
        public async Task Close(AppViewModel model) { await model.DisposeAsync(); _models.Remove(model); }
        public async Task Preferences(bool tray = false) => await new PreferencesStore(Root).SaveAsync(new()
        { SelectedClasses = ["3_IT"], DefaultSchoolYear = "2032", NotifySpecials = true, KeepInTray = tray });
        public async Task SeedExam(bool accepted)
        {
            await Preferences();
            await using var store = new SchoolDataStore(Root, Protector);
            var lease = await store.BeginAsync(); var now = DateTimeOffset.UtcNow;
            var bytes = "%PDF-fictional-notification-source"u8.ToArray(); var digest = NotificationDiff.Digest(bytes);
            var source = new SourceRecord(Guid.NewGuid().ToString("N"), MaterialKind.Exam, Path.Combine(Root, "fictional.pdf"), "fictional-identity", "fictional.pdf", digest, bytes.Length, now, now, now);
            await store.SaveOriginalAsync(lease, source, bytes);
            var day = SchoolDate.InJapan(now); var times = new Dictionary<int, TimeRange> { [1] = new("09:00", "09:45") };
            var special = new SpecialAnalysis(MaterialKind.Exam, day.SchoolYear(), [day.Iso()], ["3_IT"], times,
                [new(day.Iso(), "3_IT", 1, 1, 1, times[1], ["架空科目A"], 1)]);
            await store.SaveAnalysisAsync(lease, new(source.Id, MaterialKind.Exam, MaterialCoordinator.ParserVersion(MaterialKind.Exam), digest, source.OriginalName, now, day.SchoolYear(), Special: special));
            await store.WriteAsync(lease, "notifications", new NotificationBaseline(SpecialDigests: new Dictionary<string, string> { ["exam"] = digest },
                Pending: new Dictionary<string, PendingNotice> { ["exam"] = new(digest, 1) }));
            if (accepted) WindowsNotifications.Delivered.Add(("exam", digest));
        }
        public void Trigger(bool create)
        {
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(Root, "school", "school.sqlite"), Pooling = false }.ToString());
            db.Open(); using var command = db.CreateCommand();
            command.CommandText = create ? "CREATE TRIGGER reject_notice_ack BEFORE UPDATE ON entry WHEN NEW.key='notifications' BEGIN SELECT RAISE(ABORT,'fictional persistence failure'); END;" : "DROP TRIGGER reject_notice_ack";
            command.ExecuteNonQuery();
        }
        public async ValueTask DisposeAsync()
        {
            Identity.Before = null;
            foreach (var model in _models) await model.DisposeAsync();
            Protector.Dispose();
            Environment.SetEnvironmentVariable("TAKUPOKE_OFFLINE_TEST_MODE", _previousMode);
            Environment.SetEnvironmentVariable("TAKUPOKE_DATA_ROOT", _previousRoot);
            Directory.Delete(Root, recursive: true);
        }
    }
    [Fact]
    public async Task EventMetadataDoesNotOwnBusyAndConcurrentCallersShareOneActualRequest()
    {
        await using var fixture = new Fixture(); await fixture.Preferences();
        var events = new PublicEventsStore(fixture.Root);
        await events.SaveAsync(new(DateTimeOffset.UtcNow, new("v1", 2032, new('a',64), null,
            [new("2032-04-01", "2032-04-01", "架空保存行事", "行事メモ")]), "\"legacy-api\""));
        using var handler = new EventStatusHandler
        {
            MetadataStarted = new(TaskCreationOptions.RunContinuationsAsynchronously),
            MetadataRelease = new(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        using var network = new HttpClient(handler);
        var model = fixture.Create(events, network, new("https://example.invalid/"));
        await model.InitializeAsync();
        await handler.MetadataStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var first = model.CheckEventSourceAsync(2032); var manual = model.CheckEventSourceAsync(2032, force: true);
        Assert.Same(first, manual); Assert.False(model.Busy); Assert.Single(model.EventRecords);
        Assert.Single(model.Data.Events!); Assert.Equal(1, handler.MetadataCalls);
        handler.MetadataRelease.SetResult(); await Task.WhenAll(first, manual);
        Assert.Contains("配信版が一致しません", model.EventSourceMessage);
        await model.FetchEventsAsync(2032); await model.CheckEventSourceAsync(2032);
        Assert.Null(model.EventSourceMessage); Assert.Contains("最終成功確認", model.EventSourceDescription(2032));
    }

    [Fact]
    public async Task DelayedMetadataForOldEventBytesCannotOverrideNewSnapshot()
    {
        await using var fixture = new Fixture(); await fixture.Preferences();
        var events = new PublicEventsStore(fixture.Root);
        await events.SaveAsync(new(DateTimeOffset.UtcNow, new("v1", 2032, new('a',64), null,
            [new("2032-04-01", "2032-04-01", "架空保存行事", "行事メモ")]), "\"legacy-api\""));
        using var handler = new EventStatusHandler
        {
            MetadataStarted = new(TaskCreationOptions.RunContinuationsAsynchronously),
            MetadataRelease = new(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        using var network = new HttpClient(handler);
        var model = fixture.Create(events, network, new("https://example.invalid/"));
        await model.InitializeAsync(); await handler.MetadataStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var old = model.CheckEventSourceAsync(2032);
        handler.Updated = true;
        handler.MetadataStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await model.FetchEventsAsync(2032);
        await handler.MetadataStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(model.Busy); Assert.Equal("架空更新行事", model.EventRecords[2032].Payload.Events[0].Title);
        handler.MetadataRelease.SetResult(); await old; await model.CheckEventSourceAsync(2032);
        Assert.Null(model.EventSourceMessage);
        Assert.Contains("登録元と一致", model.EventSourceDescription(2032));
        Assert.Equal(2, handler.MetadataCalls);
    }

    [Fact]
    public async Task UnrelatedEventFetchKeepsWarningsGeneratedByRealApiAndSourceChecks()
    {
        await using var fixture = new Fixture(); await fixture.Preferences();
        var events = new PublicEventsStore(fixture.Root);
        await events.SaveAsync(new(DateTimeOffset.UtcNow, new("v1", 2026, new string('a',64), "\"fictional-old-source\"",
            [new("2026-04-01", "2026-04-01", "架空保存行事", "行事メモ")]), "\"fictional-old-api\""));
        using var handler = new EventStatusHandler(); using var network = new HttpClient(handler);
        var model = fixture.Create(events, network, new("https://example.invalid/"));
        await model.InitializeAsync();
        Assert.True(await model.CheckSavedEventsAsync());
        await model.CheckEventSourceAsync(2026);
        var sourceWarning = Assert.IsType<string>(model.EventSourceMessage);
        var failure = Assert.IsType<string>(model.EventsUpdateMessage);
        Assert.Contains("2026", failure); Assert.Equal(1, handler.Metadata2026);
        await model.FetchEventsAsync(2032); await model.CheckEventSourceAsync(2032);
        Assert.Equal(sourceWarning, model.EventSourceMessage);
        Assert.Equal(failure, model.EventsUpdateMessage); Assert.Equal(1, handler.Metadata2026);
        handler.FailedYears.Clear(); await model.FetchEventsAsync(2026); await model.CheckEventSourceAsync(2026);
        Assert.Null(model.EventSourceMessage); Assert.Null(model.EventsUpdateMessage); Assert.Equal(2, handler.Metadata2026);
        await model.FetchEventsAsync(2033);
        handler.FailedYears.UnionWith([2026,2033]);
        Assert.True(await model.CheckSavedEventsAsync());
        await model.FetchEventsAsync(2032);
        Assert.Contains("2026", model.EventsUpdateMessage); Assert.Contains("2033", model.EventsUpdateMessage);
        handler.FailedYears.Remove(2026); await model.FetchEventsAsync(2026);
        Assert.DoesNotContain("2026", model.EventsUpdateMessage); Assert.Contains("2033", model.EventsUpdateMessage);
        handler.FailedYears.Clear(); await model.FetchEventsAsync(2033);
        Assert.Null(model.EventsUpdateMessage);
    }

    [Fact]
    public async Task IncompatibleLinksCacheStillDisplaysIndependentSavedLessons()
    {
        await using var fixture = new Fixture(); await fixture.SeedExam(accepted: false);
        await using (var store = new SchoolDataStore(fixture.Root, fixture.Protector))
        {
            var lease = await store.BeginAsync();
            await store.WriteAsync(lease, "api.links", "fictional incompatible links");
        }
        var model = fixture.Create(); await model.InitializeAsync();
        Assert.Equal(4, model.Materials.Count);
        Assert.NotNull(model.Data.Specials); Assert.Single(model.Data.Specials);
        Assert.Null(model.Links);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EventDirectoryEnumerationFailureDoesNotHideLessonsAndCanBeRetried(bool permission)
    {
        await using var fixture = new Fixture(); await fixture.SeedExam(accepted: false);
        var fail = true;
        IEnumerable<string> Enumerate(string directory)
        {
            if (fail) throw permission ? new UnauthorizedAccessException("fictional directory permission") : new IOException("fictional enumeration failure");
            return Directory.EnumerateFiles(directory, "*.json");
        }
        var events = new PublicEventsStore(fixture.Root, Enumerate);
        var year = SchoolDate.InJapan(DateTimeOffset.UtcNow).SchoolYear();
        await events.SaveAsync(new(DateTimeOffset.UtcNow, new("v1", year, new string('a',64), null,
            [new($"{year}-04-01", $"{year}-04-01", "架空行事", "行事メモ")]), null));
        var model = fixture.Create(events); await model.InitializeAsync();
        Assert.Equal(4, model.Materials.Count); Assert.NotNull(model.Data.Specials); Assert.Single(model.Data.Specials);
        Assert.NotNull(model.EventStorageMessage); Assert.Empty(model.EventRecords);
        fail = false;
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "public-events", (year + 1) + ".json"), "{fictional invalid");
        await model.RefreshAsync();
        Assert.Equal(4, model.Materials.Count); Assert.True(model.EventRecords.ContainsKey(year));
        Assert.Contains((year + 1).ToString(), model.EventStorageMessage);
        File.Delete(Path.Combine(fixture.Root, "public-events", (year + 1) + ".json"));
        await model.RefreshAsync();
        Assert.Null(model.EventStorageMessage);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 3)]
    [InlineData(true, 1)]
    [InlineData(true, 3)]
    public async Task ActualNotificationToggleKeepsTheLastEditDuringAnEarlierSave(bool initial, int roundTrips)
    {
        await using var fixture = new Fixture(); await fixture.Preferences();
        var model = fixture.Create(); await model.InitializeAsync();
        await model.SavePreferencesAsync(current => current with { NotifyChanges = initial });
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        WindowsNotifications.Latest!.BeforeAllowed = async token => { entered.TrySetResult(); await release.Task.WaitAsync(token); };
        var control = new ToggleSwitch { IsOn = initial }; var window = new MainWindow(model);
        window.BindToggle(control, current => current.NotifyChanges, (current, value) => current with { NotifyChanges = value, NotificationsSetupCompleted = true });
        var snapshots = 0; model.SnapshotChanged += () => Interlocked.Increment(ref snapshots);
        try
        {
            control.Change(!initial); Assert.Equal(0, snapshots); // Before native loading.
            control.Change(initial); control.IsLoaded = true; control.Change(initial); // Initial/duplicate.
            control.Change(!initial);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            for (var round = 0; round < roundTrips; round++) { control.Change(initial); control.Change(!initial); }
            control.Change(!initial); // Duplicate final event cannot add a write.
            release.TrySetResult();
            await model.SavePreferencesAsync(current => current); // Queue barrier after every real transition.
            Assert.Equal(2 + 2 * roundTrips, snapshots);
            Assert.Equal(!initial, model.Preferences.NotifyChanges);
            Assert.Equal(!initial, (await new PreferencesStore(fixture.Root).LoadAsync()).NotifyChanges);
            await fixture.Close(model); var reopened = fixture.Create(); await reopened.InitializeAsync();
            Assert.Equal(!initial, reopened.Preferences.NotifyChanges);
        }
        finally { release.TrySetResult(); }
    }
    [Fact]
    public async Task CompletingAnEarlierYearSaveKeepsANewerUnsubmittedDraft()
    {
        await using var fixture = new Fixture(); await fixture.Preferences();
        var model = fixture.Create(); await model.InitializeAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        WindowsNotifications.Latest!.BeforeAllowed = async token => { entered.TrySetResult(); await release.Task.WaitAsync(token); };
        var window = new MainWindow(model); window.EditSchoolYear("2032");
        var saving = window.SaveSchoolYear("2032");
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            window.EditSchoolYear("2033");
            release.TrySetResult(); await saving;
            Assert.Equal("2032", model.Preferences.DefaultSchoolYear);
            Assert.Equal("2033", window.SchoolYearDraft);
        }
        finally { release.TrySetResult(); await saving; }
    }

    [Fact]
    public async Task StartupRegistrationRollsBackIfThePreferenceFileCannotBeSaved()
    {
        await using var fixture = new Fixture(); await fixture.Preferences();
        var model = fixture.Create(); await model.InitializeAsync();
        Assert.False(model.Preferences.AutoStart);
        var path = Path.Combine(fixture.Root, "preferences.json"); var backup = path + ".fictional-preserved";
        var registered = false;
        File.Move(path, backup); Directory.CreateDirectory(path);
        try
        {
            await model.SavePlatformPreferencesAsync(current => current with { AutoStart = true },
                value => { registered = value.AutoStart; return Task.CompletedTask; });
            Assert.False(registered);
            Assert.False(model.Preferences.AutoStart);
        }
        finally { Directory.Delete(path); File.Move(backup, path); }
        await model.SavePlatformPreferencesAsync(current => current with { AutoStart = true },
            value => { registered = value.AutoStart; return Task.CompletedTask; });
        Assert.True(registered); Assert.True(model.Preferences.AutoStart);
        await fixture.Close(model); var reopened = fixture.Create(); await reopened.InitializeAsync();
        Assert.True(reopened.Preferences.AutoStart);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartupFailureRestoresTheOldStateOrRetainsAnExplicitWarning(bool rollbackFails)
    {
        await using var fixture = new Fixture(); await fixture.Preferences();
        var model = fixture.Create(); await model.InitializeAsync();
        var path = Path.Combine(fixture.Root, "preferences.json"); var original = await File.ReadAllBytesAsync(path);
        var registered = false; var calls = new List<bool>();
        await model.SavePlatformPreferencesAsync(current => current with { AutoStart = true }, value =>
        {
            calls.Add(value.AutoStart);
            if (!value.AutoStart && rollbackFails) throw new IOException("fictional rollback failure");
            registered = value.AutoStart;
            if (value.AutoStart) throw new IOException("fictional partially applied startup failure");
            return Task.CompletedTask;
        });
        Assert.Equal(new[] { true, false }, calls);
        Assert.Equal(rollbackFails, registered); Assert.False(model.Preferences.AutoStart);
        Assert.NotNull(model.PlatformPreferenceMessage); Assert.Equal(original, await File.ReadAllBytesAsync(path));
        if (rollbackFails) Assert.Contains("Windows側", model.PlatformPreferenceMessage);
        await model.SavePlatformPreferencesAsync(current => current with { AutoStart = true }, value =>
        { registered = value.AutoStart; return Task.CompletedTask; });
        Assert.True(registered); Assert.True(model.Preferences.AutoStart); Assert.Null(model.PlatformPreferenceMessage);
    }
    [Fact]
    public async Task FailedYearSaveKeepsItsDraftAndSuccessfulUnchangedSaveClearsIt()
    {
        await using var fixture = new Fixture(); await fixture.Preferences();
        var model = fixture.Create(); await model.InitializeAsync();
        var window = new MainWindow(model); window.EditSchoolYear("2033");
        var path = Path.Combine(fixture.Root, "preferences.json"); var backup = path + ".fictional-preserved";
        File.Move(path, backup); Directory.CreateDirectory(path);
        try { await window.SaveSchoolYear("2033"); Assert.Equal("2033", window.SchoolYearDraft); Assert.Equal("2032", model.Preferences.DefaultSchoolYear); }
        finally { Directory.Delete(path); File.Move(backup, path); }
        await window.SaveSchoolYear("2033"); Assert.Null(window.SchoolYearDraft); Assert.Equal("2033", model.Preferences.DefaultSchoolYear);
        await fixture.Close(model); var reopened = fixture.Create(); await reopened.InitializeAsync(); Assert.Equal("2033", reopened.Preferences.DefaultSchoolYear);
    }

    private sealed class ReadGate(FakeIdentity identity) : IDisposable
    {
        private readonly ManualResetEventSlim _release = new();
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Hold() => identity.Before = () => { Entered.SetResult(); if (!_release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Fictional OS read was not released."); };
        public void Release() => _release.Set();
        public void Dispose() { _release.Set(); _release.Dispose(); }
    }
    private static async Task Observe(AppViewModel model, Func<bool> condition)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed() { if (condition()) completion.TrySetResult(); }
        model.SnapshotChanged += Changed;
        try { Changed(); await completion.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { model.SnapshotChanged -= Changed; }
    }
    private static async Task<AppViewModel> ReadyChanges(Fixture fixture, bool tray = false)
    {
        await fixture.Preferences(tray);
        var model = fixture.Create(); await model.InitializeAsync();
        var file = Path.Combine(fixture.Root, "fictional.xlsx");
        await File.WriteAllBytesAsync(file, XlsxChangeReaderTests.Workbook());
        await model.SelectAsync(MaterialKind.Changes, file);
        Assert.NotNull(model.Data.Changes);
        return model;
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnlockDuringUnfinishedReadReloadsAfterCancellationWithEitherTraySetting(bool tray)
    {
        await using var fixture = new Fixture(); var model = await ReadyChanges(fixture, tray);
        using var gate = new ReadGate(fixture.Identity); gate.Hold();
        var operation = model.RefreshAsync(); await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await model.SetLockedAsync(true); Assert.Empty(model.Materials); Assert.Null(model.Data.Changes);
        await model.SetLockedAsync(false); Assert.True(model.Busy); Assert.Empty(model.Materials);
        gate.Release(); await operation;
        fixture.Dispatcher.Drain();
        await Observe(model, () => !model.Busy && model.Data.Changes is not null);
        Assert.False(model.Locked); Assert.Equal(tray, model.Preferences.KeepInTray);
        Assert.NotNull(model.Materials[MaterialKind.Changes].Analysis);
    }
    [Fact]
    public async Task FinishingBeforeUnlockAndRemainingLockedNeverApplyTheCancelledSnapshot()
    {
        await using var fixture = new Fixture(); var model = await ReadyChanges(fixture);
        using var gate = new ReadGate(fixture.Identity); gate.Hold();
        var operation = model.RefreshAsync(); await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await model.SetLockedAsync(true); gate.Release(); await operation; fixture.Dispatcher.Drain();
        Assert.True(model.Locked); Assert.False(model.Busy); Assert.Null(model.Data.Changes); Assert.Empty(model.Materials);
        await model.SetLockedAsync(false);
        Assert.NotNull(model.Data.Changes);
    }
    [Fact]
    public async Task ExplicitStopDiscardsTheQueuedUnlockRefreshUntilExplicitResume()
    {
        await using var fixture = new Fixture(); var model = await ReadyChanges(fixture);
        using var gate = new ReadGate(fixture.Identity); gate.Hold();
        var operation = model.RefreshAsync(); await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await model.SetLockedAsync(true); await model.SetLockedAsync(false); model.SuspendAutomaticRefresh();
        gate.Release(); await operation; fixture.Dispatcher.Drain();
        Assert.True(model.AutomaticRefreshPaused); Assert.Null(model.Data.Changes); Assert.Empty(model.Materials);
        await model.ResumeAutomaticRefreshAsync(force: true);
        Assert.False(model.AutomaticRefreshPaused); Assert.NotNull(model.Data.Changes);
    }
    [Fact]
    public async Task QueuedUnlockUsesTheNewRetentionLeaseAndKeepsTheOriginalFile()
    {
        await using var fixture = new Fixture(); var model = await ReadyChanges(fixture);
        using var gate = new ReadGate(fixture.Identity); gate.Hold();
        var operation = model.RefreshAsync(); await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await model.SetLockedAsync(true); await model.SetLockedAsync(false);
        var period = SchoolDataPeriod.FromInstant(DateTimeOffset.UtcNow);
        await DataCodec.AtomicWriteAsync(Path.Combine(fixture.Root, "school-data-period.json"), DataCodec.Encode(new RetentionMarker(1, period.SchoolYear - 1, period.Half)));
        gate.Release(); await operation; fixture.Dispatcher.Drain();
        await Observe(model, () => !model.Busy && model.Materials.Count == 4);
        Assert.Null(model.Data.Changes); Assert.All(model.Materials.Values, value => Assert.Null(value.Source));
        Assert.True(File.Exists(Path.Combine(fixture.Root, "fictional.xlsx")));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SamePeriodStartupReconcilesPendingWithoutClearingAcceptedOsHistory(bool accepted)
    {
        await using var fixture = new Fixture(); await fixture.SeedExam(accepted);
        var model = fixture.Create(); await model.InitializeAsync(); var sink = WindowsNotifications.Latest!;
        Assert.Equal(0, sink.Clears); Assert.Equal(accepted ? 0 : 1, sink.Shows);
        await fixture.Close(model);
        await using var store = new SchoolDataStore(fixture.Root, fixture.Protector);
        Assert.Empty((await store.ReadAsync<NotificationBaseline>(await store.BeginAsync(), "notifications"))!.Pending!);
    }
    [Fact]
    public async Task AcceptedNoticeWithFailedPendingSaveIsAcknowledgedOnRestartWithoutResending()
    {
        await using var fixture = new Fixture(); await fixture.SeedExam(accepted: false);
        var model = fixture.Create(); var sink = WindowsNotifications.Latest!; sink.AfterAccepted = () => fixture.Trigger(true);
        await model.InitializeAsync(); Assert.Equal(1, sink.Shows); Assert.Single(WindowsNotifications.Delivered);
        await fixture.Close(model); fixture.Trigger(false);
        var reopened = fixture.Create(); await reopened.InitializeAsync();
        Assert.Equal(0, WindowsNotifications.Latest!.Clears); Assert.Equal(0, WindowsNotifications.Latest!.Shows);
        await fixture.Close(reopened);
        await using var store = new SchoolDataStore(fixture.Root, fixture.Protector);
        Assert.Empty((await store.ReadAsync<NotificationBaseline>(await store.BeginAsync(), "notifications"))!.Pending!);
    }
    [Fact]
    public async Task ActualRetentionChangeClearsOsHistoryAndOldPending()
    {
        await using var fixture = new Fixture(); await fixture.SeedExam(accepted: true);
        var period = SchoolDataPeriod.FromInstant(DateTimeOffset.UtcNow);
        await DataCodec.AtomicWriteAsync(Path.Combine(fixture.Root, "school-data-period.json"), DataCodec.Encode(new RetentionMarker(1, period.SchoolYear - 1, period.Half)));
        var model = fixture.Create(); await model.InitializeAsync();
        Assert.Equal(1, WindowsNotifications.Latest!.Clears); Assert.Equal(0, WindowsNotifications.Latest!.Shows);
        Assert.Empty(WindowsNotifications.Delivered); Assert.All(model.Materials.Values, value => Assert.Null(value.Source));
    }
    [Fact]
    public async Task RetentionClearStillRunsWhenTheOldOperationIsCancelledBeforeReload()
    {
        await using var fixture = new Fixture(); await fixture.SeedExam(accepted: true);
        var model = fixture.Create(); await model.InitializeAsync(); Assert.Single(WindowsNotifications.Delivered);
        var period = SchoolDataPeriod.FromInstant(DateTimeOffset.UtcNow);
        await DataCodec.AtomicWriteAsync(Path.Combine(fixture.Root, "school-data-period.json"), DataCodec.Encode(new RetentionMarker(1, period.SchoolYear - 1, period.Half)));
        await model.RefreshAsync();
        Assert.Equal(1, WindowsNotifications.Latest!.Clears); Assert.Empty(WindowsNotifications.Delivered);
        Assert.Null(model.Data.Changes); Assert.Empty(model.Materials);
    }
    [Theory]
    [InlineData("opening", 1)]
    [InlineData("opening", 3)]
    [InlineData("weekday", 1)]
    [InlineData("timetable-class", 1)]
    [InlineData("change-class", 1)]
    public async Task ActualSelectionHandlerPreservesRoundTripsDuringNotificationWaitAndRestart(string setting, int roundTrips)
    {
        await using var fixture = new Fixture(); await fixture.Preferences(); var model = fixture.Create(); await model.InitializeAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        WindowsNotifications.Latest!.BeforeAllowed = async token => { entered.TrySetResult(); await release.Task.WaitAsync(token); };
        var control = new ComboBox(); var window = new MainWindow(model);
        if (setting.EndsWith("class")) window.Bind(control, "", () => (true, control.SelectedClass),
            (current, value) => setting == "timetable-class" ? current with { TimetableAnalysisClasses = value.Length == 0 ? [] : [value] } : current with { ChangeAnalysisClasses = value.Length == 0 ? [] : [value] });
        else window.Bind(control, 0, () => (control.SelectedIndex is >= 0 and <= 5, control.SelectedIndex),
            (current, value) => setting == "opening" ? current with { OpeningMode = (LinkOpeningMode)value } : current with { TimetableAnalysisWeekday = value });
        var changes = 0; model.SnapshotChanged += () => Interlocked.Increment(ref changes);
        control.SelectedIndex = 1; control.SelectedClass = "3_IT"; control.Change(); Assert.Equal(0, changes); // Not loaded.
        control.SelectedIndex = 0; control.SelectedClass = ""; control.IsLoaded = true; control.Change(); Assert.Equal(0, changes); // Initial/duplicate.
        control.SelectedIndex = 1; control.SelectedClass = "3_IT"; control.Change();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        control.SelectedIndex = 0; control.SelectedClass = ""; control.Change();
        for (var round = 1; round < roundTrips; round++)
        {
            control.SelectedIndex = 1; control.SelectedClass = "3_IT"; control.Change();
            control.SelectedIndex = 0; control.SelectedClass = ""; control.Change();
        }
        control.Change(); // Duplicate must not enqueue another write.
        release.SetResult(); await Observe(model, () => Volatile.Read(ref changes) == 2 * roundTrips);
        Assert.Equal(0, control.SelectedIndex); Assert.Equal("", control.SelectedClass);
        await fixture.Close(model); var reopened = fixture.Create(); await reopened.InitializeAsync();
        Assert.Equal(LinkOpeningMode.InApp, reopened.Preferences.OpeningMode); Assert.Equal(0, reopened.Preferences.TimetableAnalysisWeekday);
        Assert.Empty(reopened.Preferences.TimetableAnalysisClasses); Assert.Empty(reopened.Preferences.ChangeAnalysisClasses);
    }
    [Fact]
    public async Task SelectionSaveFailureKeepsPreferencesAndCanBeRetriedAfterRebuildingTheControl()
    {
        await using var fixture = new Fixture(); await fixture.Preferences(); var model = fixture.Create(); await model.InitializeAsync();
        var path = Path.Combine(fixture.Root, "preferences.json"); var backup = path + ".fictional-backup";
        var original = await File.ReadAllBytesAsync(path); File.Move(path, backup); Directory.CreateDirectory(path);
        ComboBox Bind()
        {
            var control = new ComboBox { IsLoaded = true };
            new MainWindow(model).Bind(control, 0, () => (control.SelectedIndex is >= 0 and <= 1, control.SelectedIndex),
                (current, value) => current with { OpeningMode = (LinkOpeningMode)value });
            return control;
        }
        var failed = Bind(); failed.SelectedIndex = 1; failed.Change();
        await Observe(model, () => model.Status.Contains("保存できません"));
        Assert.Equal(LinkOpeningMode.InApp, model.Preferences.OpeningMode); Assert.Equal(original, await File.ReadAllBytesAsync(backup));
        Directory.Delete(path); File.Move(backup, path);
        var retried = Bind(); retried.SelectedIndex = 1; retried.Change();
        await Observe(model, () => model.Preferences.OpeningMode == LinkOpeningMode.External);
        await fixture.Close(model); var reopened = fixture.Create(); await reopened.InitializeAsync();
        Assert.Equal(LinkOpeningMode.External, reopened.Preferences.OpeningMode);
    }
    [Fact]
    public async Task CorruptRetentionSuspendsPrivateViewsWithoutClearingOsHistoryUntilConfirmedRecovery()
    {
        await using var fixture = new Fixture(); await fixture.SeedExam(accepted: true);
        var model = fixture.Create(); await model.InitializeAsync();
        Assert.NotNull(model.Data.Specials); Assert.Single(WindowsNotifications.Delivered);
        var marker = Path.Combine(fixture.Root, "school-data-period.json");
        await File.WriteAllTextAsync(marker, "{fictional-corrupt-marker");
        var original = Path.Combine(fixture.Root, "fictional-original.pdf");
        await File.WriteAllTextAsync(original, "fictional original bytes");
        for (var i = 0; i < 3; i++)
        {
            await model.RefreshAsync(); fixture.Dispatcher.Drain();
            Assert.True(model.RetentionRecoveryNeeded); Assert.False(model.Busy);
            Assert.Empty(model.Materials); Assert.Null(model.Data.Specials);
            Assert.Equal(0, WindowsNotifications.Latest!.Clears); Assert.Single(WindowsNotifications.Delivered);
            Assert.Equal("{fictional-corrupt-marker", await File.ReadAllTextAsync(marker));
        }
        await model.RecoverRetentionAsync(); fixture.Dispatcher.Drain();
        await Observe(model, () => !model.Busy && model.Materials.Count == 4);
        Assert.False(model.RetentionRecoveryNeeded); Assert.Equal(1, WindowsNotifications.Latest!.Clears);
        Assert.Empty(WindowsNotifications.Delivered); Assert.All(model.Materials.Values, item => Assert.Null(item.Source));
        Assert.Equal(["3_IT"], model.Preferences.SelectedClasses);
        Assert.Equal("fictional original bytes", await File.ReadAllTextAsync(original));
        await fixture.Close(model); var reopened = fixture.Create(); await reopened.InitializeAsync();
        Assert.False(reopened.RetentionRecoveryNeeded); Assert.Equal(4, reopened.Materials.Count);
    }
    [Theory]
    [InlineData(MaterialKind.Exam)]
    [InlineData(MaterialKind.ExamReturn)]
    public async Task SelectedSpecialWithoutFirstAnalysisRequiresAttentionOnBothScheduleViews(MaterialKind kind)
    {
        await using var fixture = new Fixture(); await fixture.Preferences();
        await using (var store = new SchoolDataStore(fixture.Root, fixture.Protector))
        {
            var lease = await store.BeginAsync(); var now = DateTimeOffset.UtcNow;
            var bytes = "%PDF-fictional-unanalysed"u8.ToArray();
            var source = new SourceRecord(Guid.NewGuid().ToString("N"), kind, Path.Combine(fixture.Root, "fictional-missing.pdf"),
                "fictional-identity", "fictional.pdf", NotificationDiff.Digest(bytes), bytes.Length, now, now, now);
            await store.SaveOriginalAsync(lease, source, bytes);
        }
        var model = fixture.Create(); await model.InitializeAsync();
        var selected = model.Materials[kind];
        Assert.NotNull(selected.Source); Assert.Null(selected.Analysis); Assert.Null(selected.ParseAttempt);
        Assert.True(selected.NeedsAttention(kind, model.Today.SchoolYear()));
        Assert.False(new MaterialSnapshot(null, null, null, null).NeedsAttention(kind, model.Today.SchoolYear()));
        Assert.DoesNotContain("前回", selected.AnalysisStatus(kind, model.Today.SchoolYear()));
    }

}
