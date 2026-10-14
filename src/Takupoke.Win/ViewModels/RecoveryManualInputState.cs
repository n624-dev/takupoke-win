namespace Takupoke.Win.ViewModels;

// Window-owned input state. No edited text replaces acquisition sources.
internal sealed class RecoveryManualInputState
{
    private string? _identity;
    private readonly Dictionary<string,string> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string,string> _original = new(StringComparer.Ordinal);
    private int _reviewLimit = 3;
    private int _editLimit = 3;
    private readonly HashSet<string> _readOnly = new(StringComparer.Ordinal);
    private readonly HashSet<string> _acknowledged = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string,string> Values => _values;
    public bool IsBoundTo(string identity) => _identity == identity;
    public bool IsAcknowledged(string key) => _acknowledged.Contains(key);
    public int ChangedCount => _values.Count(pair => !string.Equals(pair.Value, _original[pair.Key], StringComparison.Ordinal));
    public bool Ready => _identity is not null && _values.Count > 0 && _values.Count <= _reviewLimit && ChangedCount <= _editLimit &&
        _values.All(pair => _acknowledged.Contains(pair.Key) &&
            !string.IsNullOrWhiteSpace(pair.Value) && pair.Value.Length <= 256);

    public void Bind(string? identity, IReadOnlyDictionary<string,string>? original, int manualSchemaVersion = 1,
        IReadOnlyDictionary<string,string>? nativeOriginal = null, IReadOnlyCollection<string>? readOnlyKeys = null)
    {
        if (identity == _identity && identity is not null) return;
        Clear();
        if (manualSchemaVersion is not (1 or 2 or 3)) return;
        _reviewLimit = manualSchemaVersion is 2 or 3 ? 128 : 3;
        _editLimit = manualSchemaVersion == 3 ? 128 : 3;
        if (identity is null || original is not { Count: > 0 } || original.Count > _reviewLimit) return;
        if (nativeOriginal is not null && !nativeOriginal.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(original.Keys)) return;
        if (readOnlyKeys is { Count: > 0 } && (manualSchemaVersion is not (2 or 3) || readOnlyKeys.Any(key => !original.ContainsKey(key)))) return;
        _identity = identity;
        if (readOnlyKeys is not null) _readOnly.UnionWith(readOnlyKeys);
        foreach (var pair in original) { _values.Add(pair.Key, pair.Value); _original.Add(pair.Key, nativeOriginal?[pair.Key] ?? pair.Value); }
        // Prefilled uncertain OCR is never implicitly confirmed.
    }
    public void Edit(string key, string value)
    {
        if (!_values.ContainsKey(key)) throw new InvalidOperationException();
        // Rebinding/IME synchronization can publish an identical value. Only
        // an actual ordinal text change revokes the acknowledgement.
        if (string.Equals(_values[key], value, StringComparison.Ordinal)) return;
        if (_readOnly.Contains(key)) throw new InvalidOperationException("見出しは原画像との照合だけが可能です。");
        _values[key] = value; _acknowledged.Remove(key);
    }
    public void Acknowledge(string key, bool acknowledged)
    {
        if (!_values.ContainsKey(key)) throw new InvalidOperationException();
        if (acknowledged) _acknowledged.Add(key); else _acknowledged.Remove(key);
    }
    public IReadOnlyDictionary<string,string> Submission()
    {
        if (!Ready) throw new InvalidOperationException("すべての項目を原本と照合してください。");
        return new Dictionary<string,string>(_values, StringComparer.Ordinal);
    }
    public void Clear()
    {
        _identity = null; _values.Clear(); _original.Clear(); _readOnly.Clear(); _acknowledged.Clear(); _reviewLimit = 3; _editLimit = 3;
    }
}
