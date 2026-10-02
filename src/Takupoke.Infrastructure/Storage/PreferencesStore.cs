using Takupoke.Core;
using System.Text.Json.Nodes;

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
        try
        {
            var saved = JsonNode.Parse(DataCodec.Encode(preferences.Validated()))!.AsObject();
            if (preferences.MainColor == "default") saved.Remove("mainColor");
            await DataCodec.AtomicWriteAsync(_file, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(saved, DataCodec.Options), token);
        }
        finally { _gate.Release(); }
    }
}
