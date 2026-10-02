using Takupoke.Infrastructure.Api;

namespace Takupoke.Infrastructure.Storage;

public sealed class PublicEventsStore(string root)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _directory = Path.Combine(Path.GetFullPath(root), "public-events");
    private string PathFor(int year) => year is >= 1900 and <= 9998 ? Path.Combine(_directory, year + ".json") : throw new ArgumentOutOfRangeException(nameof(year));
    public IReadOnlyList<int> SavedYears() => Directory.Exists(_directory) ? Directory.EnumerateFiles(_directory, "*.json")
        .Select(f => int.TryParse(Path.GetFileNameWithoutExtension(f), out var value) ? value : 0).Where(y => y is >= 1900 and <= 9998).Order().ToArray() : [];
    public async Task<SavedEvents?> LoadAsync(int year, CancellationToken token = default)
    {
        var file = PathFor(year);
        await _gate.WaitAsync(token);
        try
        {
            if (!File.Exists(file)) return null;
            if (new FileInfo(file).Length > 2_000_000) throw new InvalidDataException("保存した学校行事のサイズを確認できません。");
            var saved = DataCodec.Decode<SavedEvents>(await File.ReadAllBytesAsync(file, token));
            if (saved.Payload is null) throw new InvalidDataException("学校行事の保存情報を確認できません。");
            saved.Payload.Validated(year);
            if (saved.ApiETag is not null && !ApiPayloads.ValidETag(saved.ApiETag)) throw new InvalidDataException("学校行事の保存情報を確認できません。");
            return saved;
        }
        finally { _gate.Release(); }
    }
    public async Task<(SavedEvents? Events, bool Failed)> LoadAvailableAsync(int year, CancellationToken token = default)
    {
        try { return (await LoadAsync(year, token), false); }
        catch (Exception error) when (error is System.Text.Json.JsonException or InvalidDataException
            or ApiException or IOException or UnauthorizedAccessException)
        {
            token.ThrowIfCancellationRequested();
            // A damaged public cache must not prevent local lessons or a fresh GET.
            // Preserve its bytes until a validated replacement is saved atomically.
            return (null, true);
        }
    }
    public async Task SaveAsync(SavedEvents events, CancellationToken token = default)
    {
        events.Payload.Validated(events.Payload.SchoolYear);
        await _gate.WaitAsync(token);
        try { await DataCodec.AtomicWriteAsync(PathFor(events.Payload.SchoolYear), DataCodec.Encode(events), token); }
        finally { _gate.Release(); }
    }
}
