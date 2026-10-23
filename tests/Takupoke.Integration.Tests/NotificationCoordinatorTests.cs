using System.Security.Cryptography;
using Takupoke.Core;
using Takupoke.Infrastructure.Notifications;
using Takupoke.Infrastructure.Storage;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class NotificationCoordinatorTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "takupoke-notification-tests-" + Guid.NewGuid().ToString("N"));
    private readonly Protector _protector = new();
    private sealed class Protector : IKeyProtector, IDisposable
    {
        private readonly EnvelopeCipher _cipher = new(RandomNumberGenerator.GetBytes(32));
        public byte[] Protect(byte[] key) => _cipher.Encrypt(key, "fake-key");
        public byte[] Unprotect(byte[] bytes) => _cipher.Decrypt(bytes, "fake-key");
        public void Dispose() => _cipher.Dispose();
    }
    private sealed class Sink : INotificationSink
    {
        public bool Allowed = true, Accept = true;
        public int Attempts;
        public List<(string Kind, string Fingerprint, int Count)> Sends = [];
        public Action? AfterAccepted;
        public HashSet<(string Kind, string Fingerprint)> Delivered = [];
        public Task<bool> IsAllowedAsync(CancellationToken token) => Task.FromResult(Allowed);
        public Task<bool> ContainsAsync(string kind, string fingerprint, CancellationToken token) => Task.FromResult(Delivered.Contains((kind, fingerprint)));
        public Task<bool> ShowAsync(string kind, string fingerprint, int count, CancellationToken token)
        {
            Attempts++; Assert.True(count > 0); Sends.Add((kind, fingerprint, count));
            if (Accept) { Delivered.Add((kind, fingerprint)); AfterAccepted?.Invoke(); }
            return Task.FromResult(Accept);
        }
        public Task ClearAsync(CancellationToken token) { Delivered.Clear(); return Task.CompletedTask; }
        public Task ClearKindAsync(string kind, CancellationToken token) { Delivered.RemoveWhere(p => p.Kind == kind); return Task.CompletedTask; }
    }
    private static readonly DateOnly Today = SchoolDate.InJapan(DateTimeOffset.UtcNow);
    private static readonly UserPreferences Preferences = new() { SelectedClasses = ["3_IT"], NotifyChanges = true, NotifySpecials = true };
    private static ScheduleData Changes(string subject) => new(Changes: [new(Today.ToString("yyyy-MM-dd"), "3_IT", "1", "架空科目", subject, "", "", "", "架空原文", "")]);
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() { _protector.Dispose(); if (Directory.Exists(_root)) Directory.Delete(_root, true); return Task.CompletedTask; }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task UnrelatedUpdatesRetainPendingTargetsAndTheSameOsTagAcrossRestart(bool pastDay, bool alreadyAccepted)
    {
        ScheduleData State(string selected, string unrelated) => new(Changes: [
            new(Today.Iso(), "3_IT", "1", "架空変更前A", selected, "", "", "", "", ""),
            new((pastDay ? Today.AddDays(-1) : Today).Iso(), pastDay ? "3_IT" : "2_CN", "2", "架空変更前B", unrelated, "", "", "", "", "")]);
        var sink = new Sink { Accept = false }; string tag;
        await using (var store = new SchoolDataStore(_root, _protector))
        {
            var coordinator = new NotificationCoordinator(store, sink);
            await coordinator.CheckAsync(State("架空A", "架空X"), new Dictionary<MaterialKind, string>(), Preferences, Today);
            await coordinator.CheckAsync(State("架空B", "架空X"), new Dictionary<MaterialKind, string>(), Preferences, Today);
            tag = Assert.Single((await store.ReadAsync<NotificationBaseline>(await store.BeginAsync(), "notifications"))!.Pending!).Value.Fingerprint;
            await coordinator.CheckAsync(State("架空B", "架空Y"), new Dictionary<MaterialKind, string>(), Preferences, Today);
            Assert.Equal(2, sink.Attempts);
            Assert.All(sink.Sends, sent => Assert.Equal(tag, sent.Fingerprint));
        }
        if (alreadyAccepted) sink.Delivered.Add(("changes", tag));
        sink.Accept = true;
        await using var reopened = new SchoolDataStore(_root, _protector);
        await new NotificationCoordinator(reopened, sink).CheckAsync(State("架空B", "架空Y"), new Dictionary<MaterialKind, string>(), Preferences, Today);
        Assert.Equal(alreadyAccepted ? 2 : 3, sink.Attempts);
        Assert.Contains(("changes", tag), sink.Delivered);
        Assert.All(sink.Sends, sent => Assert.Equal(tag, sent.Fingerprint));
        Assert.Empty((await reopened.ReadAsync<NotificationBaseline>(await reopened.BeginAsync(), "notifications"))!.Pending!);
    }

    [Fact]
    public async Task FirstImportAndClassSwitchDoNotNotifyButChangedSelectedSlotDoes()
    {
        await using var store = new SchoolDataStore(_root, _protector); var sink = new Sink(); var coordinator = new NotificationCoordinator(store, sink);
        await coordinator.CheckAsync(Changes("架空科目A"), new Dictionary<MaterialKind, string> { [MaterialKind.Exam] = "fake-first" }, Preferences, Today);
        Assert.Equal(0, sink.Attempts);
        await coordinator.CheckAsync(Changes("架空科目A"), new Dictionary<MaterialKind, string>(), Preferences with { SelectedClasses = ["2_CN"] }, Today);
        Assert.Equal(0, sink.Attempts);
        await coordinator.CheckAsync(Changes("架空科目B"), new Dictionary<MaterialKind, string> { [MaterialKind.Exam] = "fake-next" }, Preferences, Today);
        Assert.Equal(2, sink.Attempts);
        await coordinator.CheckAsync(Changes("架空科目B"), new Dictionary<MaterialKind, string>(), Preferences, Today);
        Assert.Equal(2, sink.Attempts);
    }
    [Fact]
    public async Task FailedSendSurvivesStoreRestartAndIsSentOnlyOnce()
    {
        var sink = new Sink { Accept = false };
        await using (var store = new SchoolDataStore(_root, _protector))
        {
            var coordinator = new NotificationCoordinator(store, sink);
            await coordinator.CheckAsync(Changes("架空A"), new Dictionary<MaterialKind, string>(), Preferences, Today);
            await coordinator.CheckAsync(Changes("架空B"), new Dictionary<MaterialKind, string>(), Preferences, Today);
            Assert.Single((await store.ReadAsync<NotificationBaseline>(await store.BeginAsync(), "notifications"))!.Pending!);
        }
        sink.Accept = true;
        await using var reopened = new SchoolDataStore(_root, _protector);
        var retry = new NotificationCoordinator(reopened, sink);
        await retry.CheckAsync(Changes("架空B"), new Dictionary<MaterialKind, string>(), Preferences, Today);
        await retry.CheckAsync(Changes("架空B"), new Dictionary<MaterialKind, string>(), Preferences, Today);
        Assert.Equal(2, sink.Attempts);
        Assert.Empty((await reopened.ReadAsync<NotificationBaseline>(await reopened.BeginAsync(), "notifications"))!.Pending!);
    }
    [Fact]
    public async Task PreviouslyAcceptedOsNoticeIsAcknowledgedWithoutResending()
    {
        await using var store = new SchoolDataStore(_root, _protector); var sink = new Sink();
        await store.WriteAsync(await store.BeginAsync(), "notifications", new NotificationBaseline(Pending: new Dictionary<string, PendingNotice> { ["exam"] = new("fake-accepted", 1) }));
        sink.Delivered.Add(("exam", "fake-accepted"));
        await new NotificationCoordinator(store, sink).CheckAsync(new(), new Dictionary<MaterialKind, string> { [MaterialKind.Exam] = "fake-accepted" }, Preferences, Today);
        Assert.Equal(0, sink.Attempts);
        Assert.Empty((await store.ReadAsync<NotificationBaseline>(await store.BeginAsync(), "notifications"))!.Pending!);
    }
    [Fact]
    public async Task OsPermissionDisabledUpdatesBaselineAndClearsPendingAndDelivered()
    {
        await using var store = new SchoolDataStore(_root, _protector); var sink = new Sink { Allowed = false }; var coordinator = new NotificationCoordinator(store, sink);
        sink.Delivered.Add(("changes", "fake-old"));
        await store.WriteAsync(await store.BeginAsync(), "notifications", new NotificationBaseline(Pending: new Dictionary<string, PendingNotice> { ["changes"] = new("fake-pending", 1) }));
        await coordinator.CheckAsync(Changes("架空A"), new Dictionary<MaterialKind, string>(), Preferences, Today);
        Assert.Empty(sink.Delivered); Assert.Equal(0, sink.Attempts);
        Assert.Empty((await store.ReadAsync<NotificationBaseline>(await store.BeginAsync(), "notifications"))!.Pending!);
        sink.Allowed = true;
        await coordinator.CheckAsync(Changes("架空A"), new Dictionary<MaterialKind, string>(), Preferences, Today);
        Assert.Equal(0, sink.Attempts);
    }
    [Fact]
    public async Task CancellationAfterOsAcceptsDoesNotLeaveNoticePending()
    {
        await using var store = new SchoolDataStore(_root, _protector); var sink = new Sink(); var coordinator = new NotificationCoordinator(store, sink);
        await coordinator.CheckAsync(Changes("架空A"), new Dictionary<MaterialKind, string>(), Preferences, Today);
        using var cancellation = new CancellationTokenSource(); sink.AfterAccepted = cancellation.Cancel;
        await coordinator.CheckAsync(Changes("架空B"), new Dictionary<MaterialKind, string>(), Preferences, Today, cancellation.Token);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Empty((await store.ReadAsync<NotificationBaseline>(await store.BeginAsync(), "notifications"))!.Pending!);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedChangeSendIsDiscardedAfterClassSwitchOrTheTargetDayPasses(bool switchClass)
    {
        var sink = new Sink { Accept = false };
        await using (var store = new SchoolDataStore(_root, _protector))
        {
            var coordinator = new NotificationCoordinator(store, sink);
            await coordinator.CheckAsync(Changes("架空A"), new Dictionary<MaterialKind, string>(), Preferences, Today);
            await coordinator.CheckAsync(Changes("架空B"), new Dictionary<MaterialKind, string>(), Preferences, Today);
            Assert.Equal(1, sink.Attempts);
            var pending = (await store.ReadAsync<NotificationBaseline>(await store.BeginAsync(), "notifications"))!.Pending!["changes"];
            Assert.Equal(new ChangeNoticeTarget(Today.ToString("yyyy-MM-dd"), "3_IT", "1"), Assert.Single(pending.ChangeTargets!));
        }
        sink.Accept = true;
        await using var reopened = new SchoolDataStore(_root, _protector);
        var retry = new NotificationCoordinator(reopened, sink);
        var preferences = switchClass ? Preferences with { SelectedClasses = ["2_CN"] } : Preferences;
        var day = switchClass ? Today : Today.AddDays(1);
        await retry.CheckAsync(Changes("架空B"), new Dictionary<MaterialKind, string>(), preferences, day);
        Assert.Equal(1, sink.Attempts);
        Assert.Empty(sink.Delivered);
        Assert.Empty((await reopened.ReadAsync<NotificationBaseline>(await reopened.BeginAsync(), "notifications"))!.Pending!);
        // Returning to the original selection also does not turn unchanged data into an update.
        await retry.CheckAsync(Changes("架空B"), new Dictionary<MaterialKind, string>(), Preferences, day);
        Assert.Equal(1, sink.Attempts);
    }
    [Fact]
    public async Task RetryRecountsOnlyRemainingSelectedSlotsAndUsesTheOriginalOsTag()
    {
        await using var store = new SchoolDataStore(_root, _protector);
        var sink = new Sink { Accept = false }; var coordinator = new NotificationCoordinator(store, sink);
        var classes = Preferences with { SelectedClasses = ["3_IT", "2_CN"] };
        ScheduleData Both(string subject) => Changes(subject) with
        {
            Changes = [Changes(subject).Changes![0], Changes(subject).Changes![0] with { ClassName = "2_CN" }]
        };
        await coordinator.CheckAsync(Both("架空A"), new Dictionary<MaterialKind, string>(), classes, Today);
        await coordinator.CheckAsync(Both("架空B"), new Dictionary<MaterialKind, string>(), classes, Today);
        Assert.Equal(2, Assert.Single(sink.Sends).Count);
        sink.Accept = true;
        await coordinator.CheckAsync(Both("架空B"), new Dictionary<MaterialKind, string>(), Preferences, Today);
        Assert.Equal(1, sink.Sends[1].Count);
        Assert.Equal(sink.Sends[0].Fingerprint, sink.Sends[1].Fingerprint);
        await coordinator.CheckAsync(Both("架空B"), new Dictionary<MaterialKind, string>(), Preferences, Today);
        Assert.Equal(2, sink.Attempts);
    }
    [Fact]
    public async Task LegacyChangeNoticeWithoutTargetsIsDiscardedAndDoesNotBreakNewNotifications()
    {
        await using var store = new SchoolDataStore(_root, _protector);
        var legacy = DataCodec.Decode<NotificationBaseline>("""
            {"pending":{"changes":{"fingerprint":"fake-legacy","count":1}}}
            """u8.ToArray());
        await store.WriteAsync(await store.BeginAsync(), "notifications", legacy);
        var sink = new Sink(); var coordinator = new NotificationCoordinator(store, sink);
        await coordinator.CheckAsync(Changes("架空A"), new Dictionary<MaterialKind, string>(), Preferences, Today);
        Assert.Equal(0, sink.Attempts);
        Assert.Empty((await store.ReadAsync<NotificationBaseline>(await store.BeginAsync(), "notifications"))!.Pending!);
        await coordinator.CheckAsync(Changes("架空B"), new Dictionary<MaterialKind, string>(), Preferences, Today);
        Assert.Equal(1, sink.Attempts);
    }
    [Fact]
    public async Task FailedSpecialPdfNoticeRemainsEligibleAfterClassAndDayChanges()
    {
        await using var store = new SchoolDataStore(_root, _protector);
        var sink = new Sink { Accept = false }; var coordinator = new NotificationCoordinator(store, sink);
        await coordinator.CheckAsync(new(), new Dictionary<MaterialKind, string> { [MaterialKind.Exam] = "fake-first" }, Preferences, Today);
        await coordinator.CheckAsync(new(), new Dictionary<MaterialKind, string> { [MaterialKind.Exam] = "fake-next" }, Preferences, Today);
        Assert.Equal(1, sink.Attempts);
        sink.Accept = true;
        await coordinator.CheckAsync(new(), new Dictionary<MaterialKind, string> { [MaterialKind.Exam] = "fake-next" }, Preferences with { SelectedClasses = [] }, Today.AddDays(1));
        Assert.Equal(2, sink.Attempts);
        Assert.Equal("exam", sink.Sends[1].Kind);
        await coordinator.CheckAsync(new(), new Dictionary<MaterialKind, string> { [MaterialKind.Exam] = "fake-next" }, Preferences, Today.AddDays(1));
        Assert.Equal(2, sink.Attempts);
    }

    [Theory]
    [InlineData("cer")]
    [InlineData("cre")]
    [InlineData("ecr")]
    [InlineData("erc")]
    [InlineData("rce")]
    [InlineData("rec")]
    public async Task PersistedThreeKindPendingSurvivesEveryPartialLoadOrderWithoutStaleDelivery(string order)
    {
        var sink = new Sink { Accept = false };
        var digests = new Dictionary<MaterialKind, string> { [MaterialKind.Exam] = "old-exam", [MaterialKind.ExamReturn] = "old-return" };
        await using (var store = new SchoolDataStore(_root, _protector))
        {
            var coordinator = new NotificationCoordinator(store, sink);
            await coordinator.CheckAsync(Changes("架空A"), digests, Preferences, Today);
            digests[MaterialKind.Exam] = "new-exam"; digests[MaterialKind.ExamReturn] = "new-return";
            await coordinator.CheckAsync(Changes("架空B"), digests, Preferences, Today);
            Assert.Equal(3, (await store.ReadAsync<NotificationBaseline>(await store.BeginAsync(), "notifications"))!.Pending!.Count);
        }
        await using var reopened = new SchoolDataStore(_root, _protector);
        sink = new Sink(); var retry = new NotificationCoordinator(reopened, sink);
        await retry.CheckAsync(new(), new Dictionary<MaterialKind, string>(), Preferences, Today);
        Assert.Equal(0, sink.Attempts);
        Assert.Equal(3, (await reopened.ReadAsync<NotificationBaseline>(await reopened.BeginAsync(), "notifications"))!.Pending!.Count);
        var loaded = new HashSet<char>();
        foreach (var kind in order)
        {
            loaded.Add(kind);
            var available = new Dictionary<MaterialKind, string>();
            if (loaded.Contains('e')) available[MaterialKind.Exam] = "new-exam";
            if (loaded.Contains('r')) available[MaterialKind.ExamReturn] = "new-return";
            await retry.CheckAsync(loaded.Contains('c') ? Changes("架空B") : new(), available, Preferences, Today);
            Assert.Equal(loaded.Count, sink.Attempts);
            Assert.Equal(3 - loaded.Count, (await reopened.ReadAsync<NotificationBaseline>(await reopened.BeginAsync(), "notifications"))!.Pending!.Count);
        }
        await retry.CheckAsync(Changes("架空B"), digests, Preferences, Today);
        Assert.Equal(3, sink.Attempts); Assert.Equal(3, sink.Delivered.Count);
    }
}
