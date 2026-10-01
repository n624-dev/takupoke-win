using System.Security.Cryptography;
using Takupoke.Core;

namespace Takupoke.Infrastructure.Materials;

public enum SourceFailure { Unavailable, Replaced, Changing, Invalid, Limit }
public sealed class SourceException(SourceFailure failure) : Exception(failure switch
{
    SourceFailure.Replaced => "選択した原本とは別のファイルになっています。資料を選び直してください。",
    SourceFailure.Changing => "資料が書き換え中または同期中です。少し待ってから再確認してください。",
    SourceFailure.Invalid => "選択した資料の形式を確認できませんでした。",
    SourceFailure.Limit => "資料のサイズが上限を超えています。",
    _ => "原本を読み取れません。移動・削除・アクセス権・OneDriveの保持設定を確認してください。"
}) { public SourceFailure Failure { get; } = failure; }
public interface IFileIdentityProvider { string Identity(FileStream stream); }
public sealed record SourceContent(byte[] Bytes, string Identity, DateTimeOffset ModifiedAt) : IDisposable
{ public void Dispose() => CryptographicOperations.ZeroMemory(Bytes); }
public sealed class FileSourceReader(IFileIdentityProvider identity)
{
    public const int MaximumBytes = 50 * 1024 * 1024;
    public async Task<SourceContent> ReadAsync(string path, MaterialKind kind, string? expectedIdentity, CancellationToken token = default)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            var file = new FileInfo(path); file.Refresh();
            if ((file.Attributes & FileAttributes.Directory) != 0) throw new SourceException(SourceFailure.Invalid);
            // Share reads only: a writer cannot modify this handle while we copy it.
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var initialIdentity = identity.Identity(input);
            if (string.IsNullOrEmpty(initialIdentity) || expectedIdentity is not null && initialIdentity != expectedIdentity) throw new SourceException(SourceFailure.Replaced);
            var initialLength = input.Length; var initialTime = file.LastWriteTimeUtc;
            if (initialLength is < 1 or > MaximumBytes) throw new SourceException(SourceFailure.Limit);
            var bytes = new byte[(int)initialLength];
            try
            {
                await input.ReadExactlyAsync(bytes, token);
                if (input.Length != initialLength || await input.ReadAsync(new byte[1], token) != 0) throw new SourceException(SourceFailure.Changing);
                file.Refresh();
                await using var current = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (identity.Identity(current) != initialIdentity || file.Length != initialLength || file.LastWriteTimeUtc != initialTime) throw new SourceException(SourceFailure.Changing);
                if (kind == MaterialKind.Changes ? bytes.Length < 4 || !bytes.AsSpan(0, 4).SequenceEqual(new byte[] { 0x50, 0x4b, 0x03, 0x04 })
                    : bytes.Length < 5 || !bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8)) throw new SourceException(SourceFailure.Invalid);
                return new(bytes, initialIdentity, new(initialTime, TimeSpan.Zero));
            }
            catch { CryptographicOperations.ZeroMemory(bytes); throw; }
        }
        catch (OperationCanceledException) { throw; }
        catch (SourceException) { throw; }
        catch (IOException) { throw new SourceException(SourceFailure.Unavailable); }
        catch (UnauthorizedAccessException) { throw new SourceException(SourceFailure.Unavailable); }
    }
}
