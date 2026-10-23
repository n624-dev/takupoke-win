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
        public AppViewModel Create(PublicEventsStore? events = null)
        { var model = new AppViewModel(Dispatcher, Protector, Identity, events); _models.Add(model); return model; }
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
