using Takupoke.Infrastructure.Api;

namespace Takupoke.Infrastructure.Storage;

public sealed class PublicEventsStore(string root, Func<string, IEnumerable<string>>? enumerateFiles = null)
{
    // A valid 1 MB UTF8 response can expand when System.Text.Json escapes
    // UTF16 units. At most 2000 rows x 200 title units plus bounded metadata.
    private const int MaximumSavedBytes = 3_000_000;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _directory = Path.Combine(Path.GetFullPath(root), "public-events");
    private string PathFor(int year) => year is >= 1900 and <= 9998 ? Path.Combine(_directory, year + ".json") : throw new ArgumentOutOfRangeException(nameof(year));
    public IReadOnlyList<int> SavedYears()
    {
        try
        {
            return (enumerateFiles?.Invoke(_directory) ?? Directory.EnumerateFiles(_directory, "*.json"))
                .Select(f => int.TryParse(Path.GetFileNameWithoutExtension(f), out var value) ? value : 0)
                .Where(y => y is >= 1900 and <= 9998).Order().ToArray();
        }
        catch (DirectoryNotFoundException) when (!File.Exists(_directory)) { return []; }
    }
    public (IReadOnlyList<int> Years, bool Failed) LoadSavedYearsAvailable(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        try { var years = SavedYears(); token.ThrowIfCancellationRequested(); return (years, false); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { token.ThrowIfCancellationRequested(); return ([], true); }
    }
    public async Task<SavedEvents?> LoadAsync(int year, CancellationToken token = default)
    {
        var file = PathFor(year);
        await _gate.WaitAsync(token);
        try
        {
            if (!File.Exists(file)) return null;
            if (new FileInfo(file).Length > MaximumSavedBytes) throw new InvalidDataException("保存した学校行事のサイズを確認できません。");
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
        if (events.ApiETag is not null && !ApiPayloads.ValidETag(events.ApiETag)) throw new InvalidDataException("学校行事の保存情報を確認できません。");
        var bytes = DataCodec.Encode(events);
        if (bytes.Length > MaximumSavedBytes) throw new InvalidDataException("保存した学校行事のサイズを確認できません。");
        await _gate.WaitAsync(token);
        try { await DataCodec.AtomicWriteAsync(PathFor(events.Payload.SchoolYear), bytes, token); }
        finally { _gate.Release(); }
    }

    public async Task<SavedEventSourceStatus?> LoadSourceStatusAsync(int year, CancellationToken token = default)
    {
        _ = PathFor(year);
        var file = Path.Combine(_directory, $"source-status-{year}.json");
        await _gate.WaitAsync(token);
        try
        {
            if (!File.Exists(file)) return null;
            var info = new FileInfo(file);
            if ((info.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0 || info.Length > 18_432)
                throw new InvalidDataException("元PDFの状態cacheを確認できません。");
            var bytes = await File.ReadAllBytesAsync(file, token);
            if (bytes.Length > 18_432) throw new InvalidDataException("元PDFの状態cacheを確認できません。");
            return DataCodec.Decode<SavedEventSourceStatus>(bytes).Validated(year);
        }
        finally { _gate.Release(); }
    }
    public async Task SaveSourceStatusAsync(SavedEventSourceStatus value, CancellationToken token = default)
    {
        value.Validated(value.Response.SchoolYear);
        _ = PathFor(value.Response.SchoolYear);
        var bytes = DataCodec.Encode(value);
        if (bytes.Length > 18_432) throw new InvalidDataException("元PDFの状態cacheを確認できません。");
        await _gate.WaitAsync(token);
        try { await DataCodec.AtomicWriteAsync(Path.Combine(_directory, $"source-status-{value.Response.SchoolYear}.json"), bytes, token); }
        finally { _gate.Release(); }
    }
}
