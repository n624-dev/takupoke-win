namespace Takupoke.Win.ViewModels;

// Window-owned input state. No edited text replaces acquisition sources.
internal sealed class RecoveryManualInputState
{
    private string? _identity;
    private readonly Dictionary<string,string> _values = new(StringComparer.Ordinal);
    private readonly HashSet<string> _acknowledged = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string,string> Values => _values;
    public bool IsBoundTo(string identity) => _identity == identity;
    public bool IsAcknowledged(string key) => _acknowledged.Contains(key);
    public bool Ready => _identity is not null && _values.Count is > 0 and <= 3 &&
        _values.All(pair => _acknowledged.Contains(pair.Key) &&
            !string.IsNullOrWhiteSpace(pair.Value) && pair.Value.Length <= 256);

    public void Bind(string? identity, IReadOnlyDictionary<string,string>? original)
    {
        if (identity == _identity && identity is not null) return;
        Clear();
        if (identity is null || original is not { Count: > 0 and <= 3 }) return;
        _identity = identity;
        foreach (var pair in original) _values.Add(pair.Key, pair.Value);
        // Prefilled uncertain OCR is never implicitly confirmed.
    }
    public void Edit(string key, string value)
    {
        if (!_values.ContainsKey(key)) throw new InvalidOperationException();
        // Rebinding/IME synchronization can publish an identical value. Only
        // an actual ordinal text change revokes the acknowledgement.
        if (string.Equals(_values[key], value, StringComparison.Ordinal)) return;
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
        _identity = null; _values.Clear(); _acknowledged.Clear();
    }
}
