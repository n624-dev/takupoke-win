using Takupoke.Core;

namespace Takupoke.Infrastructure.Storage;

public sealed class PreferencesStore(string root)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _file = Path.Combine(root, "preferences.json");
    public async Task<UserPreferences> LoadAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try { return File.Exists(_file) ? DataCodec.Decode<UserPreferences>(await File.ReadAllBytesAsync(_file, token)).Validated() : new(); }
        finally { _gate.Release(); }
    }
    public async Task SaveAsync(UserPreferences preferences, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try { await DataCodec.AtomicWriteAsync(_file, DataCodec.Encode(preferences.Validated()), token); }
        finally { _gate.Release(); }
    }
}
