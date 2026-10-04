using System.Reflection;
using Microsoft.UI.Dispatching;
using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Materials;
using Takupoke.Infrastructure.Recovery;
using Takupoke.Infrastructure.Storage;
using Takupoke.Win.ViewModels;
using Xunit;

namespace Takupoke.Integration.Tests;

[CollectionDefinition("view-model-offline", DisableParallelization = true)]
public sealed class ViewModelOfflineCollection;

[Collection("view-model-offline")]
public sealed class AppViewModelRefreshTests
{
    [Theory]
    [InlineData(4, false, false, true)]
    [InlineData(5, false, false, true)]
    [InlineData(4, true, false, false)]
    [InlineData(5, true, false, false)]
    [InlineData(5, false, true, false)]
    public async Task SavedRecoveryDisplayRequiresConfirmedSemanticAuditAndExactFormalProjection(
        int version, bool compound, bool changedProjection, bool expectedVisible)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Model.SavePreferencesAsync(p => p with { SelectedClasses = ["3_CN"] });
        var store = Field<SchoolDataStore>(fixture.Model, "_school"); var lease = await store.BeginAsync();
        var layout = RecoveryPipelineTests.Layout(MaterialKind.Timetable);
        layout = layout with { Glyphs = layout.Glyphs.Select(g => g.Text == "2026年度" ? g with { Text = lease.Period.SchoolYear + "年度" } :
            g.Text == "前期" ? g with { Text = lease.Period.Half == 1 ? "前期" : "後期" } : g).ToArray() };
        var document = RecoveryDocumentBuilder.Build(fixture.Source.Digest, MaterialKind.Timetable, [layout], (_, _) => true);
        var run = await RecoveryEngine.RunAsync(document, "windows", 10, true, [], _ => null);
        var result = Assert.IsType<RecoveryResult>(run.Result);
        var formal = RecoveryAnalysisConverter.Convert(fixture.Source, document, result, fixture.Analysis.ParsedAt);
        string Compound(string text) => compound ? text.Replace("架空科目A", "架空科目A・架空科目B").Replace("架空教室C", "架空教室C・架空教室D") : text;
        document = document with { Sources = document.Sources.Select(s => s with { Text = Compound(s.Text) }).ToArray() };
        result = result with { Metadata = result.Metadata with { ValidatorVersion = version }, Cells = result.Cells.Select(c => c with {
            Lessons = c.Lessons.Select(l => l with { Subject = l.Subject with { Value = Compound(l.Subject.Value) },
                Room = l.Room with { Value = Compound(l.Room.Value) } }).ToArray() }).ToArray() };
        var acceptance = new RecoveryAcceptance(document.PdfHash, RecoveryValidator.Fingerprint(result),
            RecoveryValidator.Fingerprint(document), result.Metadata, fixture.Analysis.ParsedAt);
        var audit = new RecoveryAudit(document, result, acceptance);
        formal = formal with { Recovery = audit, ParserVersion = version == 4 ? 24 : 25, Timetable = formal.Timetable! with {
            Lessons = formal.Timetable!.Lessons.Select(l => l with { Names = l.Names with {
                Subject = changedProjection ? "架空の改変された本文" : Compound(l.Names.Subject), Room = Compound(l.Names.Room!)
            } }).ToArray() } };
        await store.WriteAsync(lease, "analysis.Timetable", formal);
        var savedFingerprint = RecoveryValidator.Fingerprint(formal);
        var preferences = fixture.Model.Preferences;

        await (Task)typeof(AppViewModel).GetMethod("ReloadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(fixture.Model, [CancellationToken.None, false])!;

        Assert.Equal(expectedVisible, fixture.Model.Data.Timetable is not null);
        Assert.Equal(expectedVisible, fixture.Model.Materials[MaterialKind.Timetable].Analysis is not null);
        if (!expectedVisible) Assert.Contains("確認内容を検証できません", fixture.Model.Materials[MaterialKind.Timetable].ParseAttempt!.Failure);
        Assert.Equal(preferences, fixture.Model.Preferences);
        Assert.Equal(new[] { "3_CN" }, fixture.Model.Preferences.SelectedClasses);
        Assert.Equal(fixture.Source.Id, fixture.Model.Materials[MaterialKind.Timetable].Source!.Id);
        Assert.Equal(savedFingerprint, RecoveryValidator.Fingerprint((await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Timetable"))!));
        Assert.Null(await store.ReadAsync<RecoveryPreview>(lease, "recovery.preview.Timetable"));
    }

    [Fact]
    public async Task KnownLatestAcquisitionFailureBlocksPendingPreviewPdfWithoutChangingLastGood()
    {
        await using var fixture = await Fixture.CreateAsync();
        var store = Field<SchoolDataStore>(fixture.Model, "_school"); var lease = await store.BeginAsync();
        var doc = Takupoke.Infrastructure.Recovery.RecoveryDocumentBuilder.Build(fixture.Source.Digest, MaterialKind.Timetable,
            [RecoveryPipelineTests.Layout(MaterialKind.Timetable)], (_, _) => true);
        var run = await Takupoke.Core.Recovery.RecoveryEngine.RunAsync(doc, "windows", 10, true, [], _ => null);
        var preview = new RecoveryPreview(fixture.Source.Id, lease, doc, Assert.IsType<Takupoke.Core.Recovery.RecoveryResult>(run.Result), DateTimeOffset.UtcNow);
        await store.WriteAsync(lease, "acquisition.Timetable", new MaterialAttempt(DateTimeOffset.UtcNow, "Fictional latest-read failure", false));
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Model.ReadRecoveryPdfAsync(MaterialKind.Timetable, preview));
        Assert.Equal(fixture.Source.Digest, (await store.ReadAsync<MaterialAnalysis>(lease, "analysis.Timetable"))!.SourceDigest);
        Assert.Equal(fixture.Source.Digest, NotificationDiff.Digest(await store.ReadOriginalAsync(lease, fixture.Source.Id)));
    }
    private static T Field<T>(AppViewModel model, string name) =>
        (T)typeof(AppViewModel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(model)!;
    private static Task Run(AppViewModel model, Func<CancellationToken, Task> worker) =>
        (Task)typeof(AppViewModel).GetMethod("RunAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(model, [worker, "fictional held worker", false])!;
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task UnlockBeforeCancelledWorkerJoinsRestoresFormalDataAndSourceMonitoring()
    {
        await using var fixture = await Fixture.CreateAsync();
        var model = fixture.Model;
        await using var worker = new HeldWorker(model);
        Assert.True(model.Busy);
        await model.SetLockedAsync(true);
        await worker.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(model.Materials); Assert.Equal(0, fixture.WatcherCount());
        // KeepInTray is false and the timer substitute never ticks. Unlock must
        // schedule the reload itself, while the cancelled operation is still held.
        await model.SetLockedAsync(false);
        Assert.True(model.Busy); Assert.Empty(model.Materials);
        Assert.True(Field<bool>(model, "_pendingRefresh"));
        await worker.ReleaseAsync();
        Assert.Empty(model.Materials); Assert.True(fixture.Dispatcher.Pending > 0);
        fixture.Dispatcher.Drain();
        await fixture.WaitForIdleAsync();
        var restored = model.Materials[MaterialKind.Timetable];
        Assert.Equal(fixture.Source.Id, restored.Source!.Id);
        Assert.Equal(fixture.Analysis.SourceDigest, restored.Analysis!.SourceDigest);
        Assert.NotNull(model.Data.Timetable); Assert.Equal(1, fixture.WatcherCount());
        var changed = Signal();
        Field<SourceWatcher>(model, "_watcher").Changed += () => changed.TrySetResult();
        File.SetLastWriteTimeUtc(fixture.Source.Path, DateTime.UtcNow.AddSeconds(2));
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        // A real FileSystemWatcher notification performs another automatic check.
        await fixture.WaitForIdleAsync();
        Assert.True(model.Materials[MaterialKind.Timetable].Source!.LastCheckedAt > restored.Source.LastCheckedAt);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CancelOrSuspendAfterDrainWasPostedCannotReviveARequestOnResume(bool suspend)
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var worker = new HeldWorker(fixture.Model);
        await fixture.Model.RefreshAsync();
        Assert.True(Field<bool>(fixture.Model, "_pendingRefresh"));
        await worker.ReleaseAsync();
        Assert.True(fixture.Dispatcher.Pending > 0);
        if (suspend) fixture.Model.SuspendAutomaticRefresh(); else fixture.Model.Cancel();
        await fixture.Model.ResumeAutomaticRefreshAsync(refresh: false);
        var checkedAt = fixture.Model.Materials[MaterialKind.Timetable].Source!.LastCheckedAt;
        fixture.Dispatcher.Drain(); await fixture.WaitForIdleAsync();
        Assert.Equal(checkedAt, fixture.Model.Materials[MaterialKind.Timetable].Source!.LastCheckedAt);
        Assert.False(Field<bool>(fixture.Model, "_pendingRefresh"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CancelOrSuspendClearsRequestQueuedBehindBusyWorker(bool suspend)
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var worker = new HeldWorker(fixture.Model);
        await fixture.Model.RefreshAutomaticallyAsync(force: true);
        Assert.True(Field<bool>(fixture.Model, "_pendingRefresh"));
        if (suspend) fixture.Model.SuspendAutomaticRefresh(); else fixture.Model.Cancel();
        Assert.False(Field<bool>(fixture.Model, "_pendingRefresh"));
        await fixture.Model.ResumeAutomaticRefreshAsync(refresh: false);
        var checkedAt = fixture.Model.Materials[MaterialKind.Timetable].Source!.LastCheckedAt;
        await worker.ReleaseAsync(); await fixture.WaitForIdleAsync();
        Assert.Equal(checkedAt, fixture.Model.Materials[MaterialKind.Timetable].Source!.LastCheckedAt);
    }

    [Theory]
    [InlineData("lock")] [InlineData("suspend")] [InlineData("throttle")]
    public async Task IneligibleAutomaticRequestIsNotQueuedWhileBusy(string reason)
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var worker = new HeldWorker(fixture.Model);
        if (reason == "lock") await fixture.Model.SetLockedAsync(true);
        else if (reason == "suspend") fixture.Model.SuspendAutomaticRefresh();
        else typeof(AppViewModel).GetField("_lastAutomaticCheck", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(fixture.Model, DateTimeOffset.UtcNow);
        await fixture.Model.RefreshAutomaticallyAsync(force: reason != "throttle");
        Assert.False(Field<bool>(fixture.Model, "_pendingRefresh"));
        await worker.ReleaseAsync(); fixture.Dispatcher.Drain();
        Assert.False(fixture.Model.Busy);
        if (reason == "lock") { Assert.Empty(fixture.Model.Materials); Assert.Equal(0, fixture.WatcherCount()); }
    }

    // Always release the artificial native join, including when a regression
    // assertion fails, so fixture disposal cannot hang waiting for that worker.
    private sealed class HeldWorker : IAsyncDisposable
    {
        private readonly TaskCompletionSource _release = Signal();
        private readonly Task _task;
        public TaskCompletionSource CancellationObserved { get; } = Signal();
        public HeldWorker(AppViewModel model) => _task = Run(model, async token =>
        {
            using var registration = token.Register(() => CancellationObserved.TrySetResult());
            await _release.Task;
            token.ThrowIfCancellationRequested();
        });
        public async Task ReleaseAsync() { _release.TrySetResult(); await _task.WaitAsync(TimeSpan.FromSeconds(10)); }
        public async ValueTask DisposeAsync() => await ReleaseAsync();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string? _oldMode = Environment.GetEnvironmentVariable("TAKUPOKE_OFFLINE_TEST_MODE");
        private readonly string? _oldRoot = Environment.GetEnvironmentVariable("TAKUPOKE_DATA_ROOT");
        private readonly string _root = Path.Combine(Path.GetTempPath(), "takupoke-viewmodel-" + Guid.NewGuid().ToString("N"));
        public DispatcherQueue Dispatcher { get; } = new();
        public AppViewModel Model { get; private set; } = null!;
        public SourceRecord Source { get; private set; } = null!;
        public MaterialAnalysis Analysis { get; private set; } = null!;
        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            try
            {
                Directory.CreateDirectory(fixture._root);
                Environment.SetEnvironmentVariable("TAKUPOKE_OFFLINE_TEST_MODE", "1");
                Environment.SetEnvironmentVariable("TAKUPOKE_DATA_ROOT", fixture._root);
                fixture.Model = new(fixture.Dispatcher);
                var store = Field<SchoolDataStore>(fixture.Model, "_school"); var lease = await store.BeginAsync();
                var bytes = "%PDF-entirely-fictional-viewmodel"u8.ToArray();
                var path = Path.Combine(fixture._root, "fictional-source.pdf"); await File.WriteAllBytesAsync(path, bytes);
                var now = DateTimeOffset.UtcNow.AddMinutes(-2);
                fixture.Source = new("fictional-viewmodel", MaterialKind.Timetable, path, "fictional-viewmodel-source", "fictional-source.pdf",
                    NotificationDiff.Digest(bytes), bytes.Length, now, now, now);
                fixture.Analysis = new(fixture.Source.Id, fixture.Source.Kind, MaterialCoordinator.ParserVersion(MaterialKind.Timetable),
                    fixture.Source.Digest, fixture.Source.OriginalName, now, lease.Period.SchoolYear,
                    Timetable: new(lease.Period.SchoolYear, lease.Period.Half == 1 ? "前期" : "後期", []));
                await store.SaveOriginalAsync(lease, fixture.Source, bytes); await store.SaveAnalysisAsync(lease, fixture.Analysis);
                await new PreferencesStore(fixture._root).SaveAsync(new UserPreferences { KeepInTray = false });
                await fixture.Model.InitializeAsync();
                Assert.False(fixture.Model.Busy); Assert.NotNull(fixture.Model.Data.Timetable);
                Assert.Equal(1, fixture.WatcherCount());
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }
        public int WatcherCount() => ((System.Collections.ICollection)typeof(SourceWatcher)
            .GetField("_watchers", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(Field<SourceWatcher>(Model, "_watcher"))!).Count;
        public async Task WaitForIdleAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            do { Dispatcher.Drain(); if (!Model.Busy && Dispatcher.Pending == 0) return; await Task.Delay(10, timeout.Token); } while (true);
        }
        public async ValueTask DisposeAsync()
        {
            if (Model is not null) await Model.DisposeAsync();
            Environment.SetEnvironmentVariable("TAKUPOKE_OFFLINE_TEST_MODE", _oldMode);
            Environment.SetEnvironmentVariable("TAKUPOKE_DATA_ROOT", _oldRoot);
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }
}
