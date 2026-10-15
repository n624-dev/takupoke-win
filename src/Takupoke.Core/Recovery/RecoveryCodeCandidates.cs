namespace Takupoke.Core.Recovery;

// Optional typing aids, never OCR evidence or authority to adopt a result.
// Only a supplied room-code roster can contribute literal candidates.
public static class RecoveryCodeCandidates
{
    public const int MaximumRules = 4096;
    public const int MaximumCandidates = 3;

    public static IReadOnlyList<string> Find(string original, IReadOnlyList<MappingRule> rooms,
        IReadOnlyCollection<string> classes, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!Code(original) || rooms.Count > MaximumRules || classes.Count == 0) return [];
        var candidates = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var rule in rooms)
        {
            token.ThrowIfCancellationRequested();
            // A merged cell must not borrow a name limited to just one class.
            if (rule.Classes is not null && !classes.All(rule.Classes.Contains)) continue;
            Add(rule.Alias); Add(rule.FullName);
        }
        return candidates.OrderBy(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(MaximumCandidates).Select(pair => pair.Key).ToArray();

        void Add(string candidate)
        {
            if (!Code(candidate) || candidate.Length != original.Length) return;
            var changes = 0;
            for (var i = 0; i < original.Length; i++)
            {
                if (candidate[i] == original[i]) continue;
                if (++changes > 2 || !Confusable(original[i], candidate[i])) return;
            }
            if (changes > 0) candidates.TryAdd(candidate, changes);
        }
    }

    private static bool Code(string value) => value.Length is >= 2 and <= 32 &&
        value.Any(char.IsAsciiDigit) && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static bool Confusable(char a, char b) =>
        "Il1".Contains(a) && "Il1".Contains(b) || "O0".Contains(a) && "O0".Contains(b);
}
