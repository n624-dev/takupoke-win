using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Takupoke.Core;

public sealed record ChangeFingerprint(string Date, string ClassName, string Period, string Fingerprint);
public sealed record PendingNotice(string Fingerprint, int Count);
public sealed record NotificationBaseline(IReadOnlyList<ChangeFingerprint>? Changes = null,
    IReadOnlyDictionary<string, string>? SpecialDigests = null, IReadOnlyDictionary<string, PendingNotice>? Pending = null);

public static class NotificationDiff
{
    public static IReadOnlyList<ChangeFingerprint> Fingerprints(IEnumerable<ScheduleChange> changes) => changes.Select(change =>
    {
        var period = change.DetailPeriods is { } periods ? string.Join(",", periods) : change.Period;
        var fields = new[] { change.ChangeDate, change.DisplayClassName, period, change.BeforeSubject, change.AfterSubject, change.Teacher, change.Room, change.Note };
        return new ChangeFingerprint(change.ChangeDate, change.DisplayClassName, period, Digest(JsonSerializer.SerializeToUtf8Bytes(fields)));
    }).Distinct().OrderBy(f => f.Date, StringComparer.Ordinal).ThenBy(f => f.ClassName, StringComparer.Ordinal).ThenBy(f => f.Period, StringComparer.Ordinal)
        .ThenBy(f => f.Fingerprint, StringComparer.Ordinal).ToArray();
    public static int ChangeCount(IReadOnlyList<ChangeFingerprint>? previous, IReadOnlyList<ChangeFingerprint> next,
        DateOnly today, IReadOnlySet<string> classes)
    {
        if (previous is null) return 0;
        var changed = previous.ToHashSet();
        changed.SymmetricExceptWith(next);
        return changed.Where(c => SchoolDate.TryParse(c.Date, out var day) && day >= today && classes.Contains(c.ClassName))
            .Select(c => (c.Date, c.ClassName, c.Period)).Distinct().Count();
    }
    public static NotificationBaseline Reconcile(NotificationBaseline previous, IReadOnlyList<ChangeFingerprint>? changes,
        IReadOnlyDictionary<string, string> specialDigests, DateOnly today, IReadOnlySet<string> classes, bool notifyChanges, bool notifySpecials)
    {
        var pending = new Dictionary<string, PendingNotice>(previous.Pending ?? new Dictionary<string, PendingNotice>());
        var specials = new Dictionary<string, string>(previous.SpecialDigests ?? new Dictionary<string, string>());
        if (changes is not null)
        {
            var count = ChangeCount(previous.Changes, changes, today, classes);
            if (notifyChanges && count > 0) pending["changes"] = new(Digest(JsonSerializer.SerializeToUtf8Bytes(changes.Select(c => c.Fingerprint).Order(StringComparer.Ordinal))), count);
        }
        foreach (var pair in specialDigests)
        {
            if (notifySpecials && specials.TryGetValue(pair.Key, out var old) && old != pair.Value) pending[pair.Key] = new(pair.Value, 1);
            specials[pair.Key] = pair.Value;
        }
        if (!notifyChanges) pending.Remove("changes");
        if (!notifySpecials) { pending.Remove("exam"); pending.Remove("examReturn"); }
        return new(changes ?? previous.Changes, specials, pending);
    }
    public static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static string Digest(string text) => Digest(Encoding.UTF8.GetBytes(text));
}
