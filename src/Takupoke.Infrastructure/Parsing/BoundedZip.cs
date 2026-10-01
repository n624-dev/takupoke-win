using System.IO.Compression;

namespace Takupoke.Infrastructure.Parsing;

/// <summary>Reads bounded entries in memory, validates their CRC, and never extracts paths.</summary>
public sealed class BoundedZip : IDisposable
{
    private readonly MemoryStream _stream;
    private readonly ZipArchive _archive;
    private readonly Dictionary<string, ZipArchiveEntry> _entries = new(StringComparer.Ordinal);
    public IReadOnlyCollection<string> Names => _entries.Keys;
    public BoundedZip(byte[] data, int maximumBytes = 50 * 1024 * 1024, int maximumEntries = 2048,
        int maximumEntryBytes = 32 * 1024 * 1024, int maximumTotalBytes = 64 * 1024 * 1024)
    {
        if (data.Length == 0 || data.Length > maximumBytes) throw new InvalidDataException("資料のサイズが上限を超えています。");
        _stream = new(data, writable: false);
        try
        {
            _archive = new(_stream, ZipArchiveMode.Read, leaveOpen: true);
            long total = 0;
            foreach (var entry in _archive.Entries)
            {
                total += entry.Length;
                var path = entry.FullName;
                var parts = path.Split('/');
                var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
                if (_entries.Count >= maximumEntries || entry.Length > maximumEntryBytes || total > maximumTotalBytes)
                    throw new InvalidDataException("資料の展開サイズが上限を超えています。");
                if (path.StartsWith('/') || path.Contains('\\') || parts.Any(p => p is ".." or ".") || unixType == 0xA000 || !_entries.TryAdd(path, entry))
                    throw new InvalidDataException("資料の構造が不正です。");
            }
        }
        catch { _stream.Dispose(); throw; }
    }
    public bool Contains(string name) => _entries.ContainsKey(name);
    public byte[] Read(string name, int maximumBytes, CancellationToken cancellationToken = default)
    {
        if (!_entries.TryGetValue(name, out var entry) || entry.Name.Length == 0 || entry.Length > maximumBytes)
            throw new InvalidDataException("必要な資料の内容を読み取れません。");
        using var input = entry.Open();
        using var output = new MemoryStream();
        var buffer = new byte[64 * 1024];
        uint crc = 0xFFFFFFFF;
        int count;
        while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (output.Length + count > maximumBytes) throw new InvalidDataException("資料の展開サイズが上限を超えています。");
            output.Write(buffer, 0, count);
            for (var i = 0; i < count; i++) crc = CrcTable[(crc ^ buffer[i]) & 0xFF] ^ (crc >> 8);
        }
        if (output.Length != entry.Length || ~crc != entry.Crc32) throw new InvalidDataException("資料のチェックサムが一致しません。");
        return output.ToArray();
    }
    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(index =>
    {
        var crc = (uint)index;
        for (var bit = 0; bit < 8; bit++) crc = (crc & 1) == 0 ? crc >> 1 : 0xEDB88320 ^ (crc >> 1);
        return crc;
    }).ToArray();
    public void Dispose() { _archive.Dispose(); _stream.Dispose(); }
}
