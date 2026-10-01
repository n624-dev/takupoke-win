using Takupoke.Core;
using Takupoke.Infrastructure.Storage;

namespace Takupoke.Infrastructure.Notifications;

public interface INotificationSink
{
    Task<bool> IsAllowedAsync(CancellationToken token);
    Task<bool> ContainsAsync(string kind, string fingerprint, CancellationToken token);
    Task<bool> ShowAsync(string kind, string fingerprint, int count, CancellationToken token);
    Task ClearAsync(CancellationToken token);
    Task ClearKindAsync(string kind, CancellationToken token);
}
public sealed class NotificationCoordinator(SchoolDataStore store, INotificationSink sink)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async Task CheckAsync(ScheduleData data, IReadOnlyDictionary<MaterialKind, string> acceptedDigests, UserPreferences preferences, DateOnly today, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            var lease = await store.BeginAsync(token);
            var saved = await store.ReadAsync<NotificationBaseline>(lease, "notifications", token) ?? new();
            var specialDigests = acceptedDigests.Where(p => p.Key is MaterialKind.Exam or MaterialKind.ExamReturn)
                .ToDictionary(p => p.Key == MaterialKind.Exam ? "exam" : "examReturn", p => p.Value);
            var allowed = await sink.IsAllowedAsync(token);
            var notifyChanges = allowed && preferences.NotifyChanges;
            var notifySpecials = allowed && preferences.NotifySpecials;
            var next = NotificationDiff.Reconcile(saved, data.Changes is null ? null : NotificationDiff.Fingerprints(data.Changes), specialDigests,
                today, preferences.SelectedClasses.ToHashSet(), notifyChanges, notifySpecials);
            await store.WriteAsync(lease, "notifications", next, token);
            if (!notifyChanges) await sink.ClearKindAsync("changes", token);
            if (!notifySpecials) { await sink.ClearKindAsync("exam", token); await sink.ClearKindAsync("examReturn", token); }
            foreach (var pending in next.Pending ?? new Dictionary<string, PendingNotice>())
            {
                token.ThrowIfCancellationRequested();
                // Retry the same stable tag; reconcile accepted notifications before attempting another send.
                if (!await sink.ContainsAsync(pending.Key, pending.Value.Fingerprint, token)
                    && !await sink.ShowAsync(pending.Key, pending.Value.Fingerprint, pending.Value.Count, token)) continue;
                var remaining = new Dictionary<string, PendingNotice>(next.Pending!); remaining.Remove(pending.Key);
                // Once Windows accepted the notice, acknowledge it even if the user cancelled
                // during the OS call. The lease still rejects lock/retention invalidation.
                next = next with { Pending = remaining }; await store.WriteAsync(lease, "notifications", next, CancellationToken.None);
            }
        }
        finally { _gate.Release(); }
    }
    public Task ClearAsync(CancellationToken token = default) => sink.ClearAsync(token);
}
