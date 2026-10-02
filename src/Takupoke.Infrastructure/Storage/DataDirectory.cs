namespace Takupoke.Infrastructure.Storage;

public sealed record DataDirectoryResult(string Root, string? Message = null);

/// <summary>Renames the complete data directory without rewriting keys, records, or originals.</summary>
public static class DataDirectory
{
    public static DataDirectoryResult Resolve(string localApplicationData)
    {
        var parent = Path.GetFullPath(localApplicationData);
        var current = Path.Combine(parent, "takupoke");
        var previous = Path.Combine(parent, "TakupokeWin");
        if (!Directory.Exists(previous)) return new(current);
        try
        {
            if (Directory.Exists(current))
            {
                if (Directory.EnumerateFileSystemEntries(current).Any())
                {
                    if (!Directory.EnumerateFileSystemEntries(previous).Any()) return new(current);
                    return new(previous, "新旧両方の保存先にデータがあります。上書きを避けるため、従来の保存先を使用しています。設定の保存先を確認してください。");
                }
                // Remove only an empty destination. A concurrent write causes this to fail safely.
                Directory.Delete(current, recursive: false);
            }
            Directory.Move(previous, current);
            return new(current);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new(previous, "保存先を移行できなかったため、従来の保存先を引き続き使用しています。設定の保存先を確認してください。");
        }
    }
}
