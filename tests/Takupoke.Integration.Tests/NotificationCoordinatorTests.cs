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
        public Action? AfterAccepted;
        public HashSet<(string Kind, string Fingerprint)> Delivered = [];
        public Task<bool> IsAllowedAsync(CancellationToken token) => Task.FromResult(Allowed);
        public Task<bool> ContainsAsync(string kind, string fingerprint, CancellationToken token) => Task.FromResult(Delivered.Contains((kind, fingerprint)));
        public Task<bool> ShowAsync(string kind, string fingerprint, int count, CancellationToken token)
        {
            Attempts++; Assert.True(count > 0);
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
        await new NotificationCoordinator(store, sink).CheckAsync(new(), new Dictionary<MaterialKind, string>(), Preferences, Today);
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
}
