using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Takupoke.Core;

public sealed record ChangeFingerprint(string Date, string ClassName, string Period, string Fingerprint);
public sealed record ChangeNoticeTarget(string Date, string ClassName, string Period);
public sealed record PendingNotice(string Fingerprint, int Count, IReadOnlyList<ChangeNoticeTarget>? ChangeTargets = null, string? Revision = null);
public sealed record NotificationBaseline(IReadOnlyList<ChangeFingerprint>? Changes = null,
    IReadOnlyDictionary<string, string>? SpecialDigests = null, IReadOnlyDictionary<string, PendingNotice>? Pending = null);

public enum NotificationSourceState { Unavailable, Absent, Available }

public static class NotificationDiff
{
    public static IReadOnlyList<ChangeFingerprint> Fingerprints(IEnumerable<ScheduleChange> changes) => changes.Select(change =>
    {
        var period = change.DetailPeriods is { } periods ? string.Join(",", periods) : change.Period;
        var fields = new List<string> { change.ChangeDate, change.DisplayClassName, period, change.BeforeSubject, change.AfterSubject, change.Teacher, change.Room, change.Note };
        var oldKind = change.Note.Normalize(NormalizationForm.FormKC).Trim();
        if (oldKind is not ("休講" or "補講")) oldKind = "変更";
        if (oldKind != change.KindLabel) fields.Add(change.KindLabel);
        return new ChangeFingerprint(change.ChangeDate, change.DisplayClassName, period, Digest(JsonSerializer.SerializeToUtf8Bytes(fields)));
    }).Distinct().OrderBy(f => f.Date, StringComparer.Ordinal).ThenBy(f => f.ClassName, StringComparer.Ordinal).ThenBy(f => f.Period, StringComparer.Ordinal)
        .ThenBy(f => f.Fingerprint, StringComparer.Ordinal).ToArray();
    public static int ChangeCount(IReadOnlyList<ChangeFingerprint>? previous, IReadOnlyList<ChangeFingerprint> next,
        DateOnly today, IReadOnlySet<string> classes) => ChangedTargets(previous, next, today, classes).Count;
    private static IReadOnlyList<ChangeNoticeTarget> ChangedTargets(IReadOnlyList<ChangeFingerprint>? previous, IReadOnlyList<ChangeFingerprint> next,
        DateOnly today, IReadOnlySet<string> classes)
    {
        if (previous is null) return [];
        var changed = previous.ToHashSet();
        changed.SymmetricExceptWith(next);
        return EligibleTargets(changed.Select(c => new ChangeNoticeTarget(c.Date, c.ClassName, c.Period)), today, classes);
    }
    private static IReadOnlyList<ChangeNoticeTarget> EligibleTargets(IEnumerable<ChangeNoticeTarget> targets, DateOnly today, IReadOnlySet<string> classes) =>
        targets.Where(c => SchoolDate.TryParse(c.Date, out var day) && day >= today && classes.Contains(c.ClassName)).Distinct()
            .OrderBy(c => c.Date, StringComparer.Ordinal).ThenBy(c => c.ClassName, StringComparer.Ordinal).ThenBy(c => c.Period, StringComparer.Ordinal).ToArray();
    public static NotificationBaseline Reconcile(NotificationBaseline previous, IReadOnlyList<ChangeFingerprint>? changes,
        IReadOnlyDictionary<string, string> specialDigests, DateOnly today, IReadOnlySet<string> classes, bool notifyChanges, bool notifySpecials,
        IReadOnlyDictionary<string, NotificationSourceState>? availability = null)
    {
        NotificationSourceState State(string kind) => availability?.GetValueOrDefault(kind) ??
            (kind == "changes" ? changes is null ? NotificationSourceState.Unavailable : NotificationSourceState.Available
                : specialDigests.ContainsKey(kind) ? NotificationSourceState.Available : NotificationSourceState.Unavailable);
        var pending = new Dictionary<string, PendingNotice>(previous.Pending ?? new Dictionary<string, PendingNotice>());
        var specials = new Dictionary<string, string>(previous.SpecialDigests ?? new Dictionary<string, string>());
        if (pending.TryGetValue("changes", out var notice))
        {
            // Older saved notices lack the targets needed to determine whether a retry
            // is still relevant. Keep the baseline, but safely discard those notices.
            var targets = EligibleTargets(notice.ChangeTargets ?? [], today, classes);
            if (!notifyChanges || State("changes") == NotificationSourceState.Absent || targets.Count == 0
                || State("changes") == NotificationSourceState.Available && changes is not null && (previous.Changes is null || (notice.Revision ?? notice.Fingerprint) != ChangeRevision(previous.Changes))) pending.Remove("changes");
            else pending["changes"] = notice with { Count = targets.Count, ChangeTargets = targets };
        }
        if (State("changes") == NotificationSourceState.Available && changes is not null)
        {
            var targets = ChangedTargets(previous.Changes, changes, today, classes);
            if (notifyChanges && targets.Count > 0)
            {
                // Collect unsent slots across updates, counting a repeatedly changed slot once.
                var retained = pending.GetValueOrDefault("changes")?.ChangeTargets ?? [];
                targets = EligibleTargets(retained.Concat(targets), today, classes);
                pending["changes"] = new(ChangeRevision(changes), targets.Count, targets, ChangeRevision(changes));
            }
            else if (pending.TryGetValue("changes", out var retained))
            {
                // Unrelated edits advance the validation baseline without changing
                // the OS tag for a notice that may already have been accepted.
                pending["changes"] = retained with { Revision = ChangeRevision(changes) };
            }
        }
        foreach (var kind in new[] { "exam", "examReturn" })
            if (State(kind) == NotificationSourceState.Absent) { pending.Remove(kind); specials.Remove(kind); }
        foreach (var pair in specialDigests)
        {
            if (State(pair.Key) != NotificationSourceState.Available) continue;
            if (notifySpecials && specials.TryGetValue(pair.Key, out var old) && old != pair.Value) pending[pair.Key] = new(pair.Value, 1);
            else if (pending.GetValueOrDefault(pair.Key)?.Fingerprint != pair.Value) pending.Remove(pair.Key);
            specials[pair.Key] = pair.Value;
        }
        if (!notifyChanges) pending.Remove("changes");
        if (!notifySpecials) { pending.Remove("exam"); pending.Remove("examReturn"); }
        return new(State("changes") == NotificationSourceState.Absent ? null : changes ?? previous.Changes, specials, pending);
    }
    public static string ChangeRevision(IReadOnlyList<ChangeFingerprint> changes) =>
        Digest(JsonSerializer.SerializeToUtf8Bytes(changes.Select(c => c.Fingerprint).Order(StringComparer.Ordinal)));
    public static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static string Digest(string text) => Digest(Encoding.UTF8.GetBytes(text));
}
