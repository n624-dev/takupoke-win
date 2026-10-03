using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Takupoke.Infrastructure.Storage;

namespace Takupoke.Infrastructure.Recovery;
public sealed record RecoveryArtifact(string Path, string Url, long Size, string Sha256);
public sealed record RecoveryModelBundle(string Id, string Version, string Runtime, string MinimumOs, long MinimumMemory, string License, bool Validated, IReadOnlyList<RecoveryArtifact> Artifacts)
{
    public long Size => Artifacts.Sum(a => a.Size);
    public bool Valid => Validated && Regex.IsMatch(Id, "^[a-zA-Z0-9._-]{1,100}(?::[0-9]{1,8})?$") && Regex.IsMatch(Version, "^[a-zA-Z0-9._-]{1,100}$") && Runtime is "windowsOcr" or "foundryLocal" && Artifacts.Count is > 0 and <= 128 && Artifacts.Select(a => a.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() == Artifacts.Count && !string.IsNullOrWhiteSpace(License) && MinimumMemory > 0 && Artifacts.All(a => a.Size is > 0 and <= 8L * 1024 * 1024 * 1024 && Regex.IsMatch(a.Sha256, "^[a-f0-9]{64}$") && Uri.TryCreate(a.Url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0 && a.Path.Length < 240 && !a.Path.Split('/').Any(p => p is "." or ".." or "" || !Regex.IsMatch(p, "^[a-zA-Z0-9._-]+$")));
}
/// Owns pinned, verified artifact directories. A failed download or smoke test
/// leaves the old active directory intact; no SDK catalog auto-update is used.
public sealed class RecoveryModelBundleStore(string root)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static string DirectoryName(RecoveryModelBundle bundle) => bundle.Runtime + "-" + Regex.Replace(bundle.Id, "[^a-zA-Z0-9._-]", "_") + "-" + bundle.Version + "-" + Takupoke.Core.Recovery.RecoveryValidator.Fingerprint(bundle);
    public async Task<string> InstallAsync(RecoveryModelBundle bundle, Func<string, CancellationToken, Task<Stream>> download,
        Func<string, CancellationToken, Task> smoke, CancellationToken token = default)
    {
        if (!bundle.Valid) throw new InvalidDataException("モデルManifestを確認できません。");
        await _gate.WaitAsync(token);
        var staging = Path.Combine(root, "staging-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, DirectoryName(bundle)); var committed = false; var owns = false;
        try
        {
            Directory.CreateDirectory(root); var pointer = Path.Combine(root, "active." + bundle.Runtime + ".json");
            var old = File.Exists(pointer) ? DataCodec.Decode<RecoveryModelBundle>(await File.ReadAllBytesAsync(pointer, token)) : null;
            if (old is not null && !old.Valid) throw new InvalidDataException("既存モデルManifestを確認できません。");
            if (!Directory.Exists(target))
            {
                Directory.CreateDirectory(staging);
                foreach (var artifact in bundle.Artifacts)
                {
                    token.ThrowIfCancellationRequested(); var file = Path.Combine(staging, artifact.Path.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    await using var input = await download(artifact.Url, token); await using var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[65536]; long size = 0; int n;
                    while ((n = await input.ReadAsync(buffer, token)) > 0) { size += n; if (size > artifact.Size) throw new InvalidDataException("モデルサイズが一致しません。"); hash.AppendData(buffer, 0, n); await output.WriteAsync(buffer.AsMemory(0, n), token); }
                    await output.FlushAsync(token); output.Flush(true);
                    if (size != artifact.Size || Convert.ToHexStringLower(hash.GetHashAndReset()) != artifact.Sha256) throw new InvalidDataException("モデルSHA-256が一致しません。");
                }
                await smoke(staging, token); token.ThrowIfCancellationRequested(); Directory.Move(staging, target); owns = true;
            }
            else { if (!await VerifyAsync(bundle, target, token)) throw new InvalidDataException("保存モデルを確認できません。"); await smoke(target, token); }
            token.ThrowIfCancellationRequested(); await DataCodec.AtomicWriteAsync(pointer, DataCodec.Encode(bundle), token); committed = true;
            if (old is not null) { var oldPath = Path.Combine(root, DirectoryName(old)); if (oldPath != target) try { Directory.Delete(oldPath, true); } catch (IOException) { } }
            return target;
        }
        finally { try { if (Directory.Exists(staging)) Directory.Delete(staging, true); if (owns && !committed && Directory.Exists(target)) Directory.Delete(target, true); } finally { _gate.Release(); } }
    }
    public static async Task<bool> VerifyAsync(RecoveryModelBundle bundle, string path, CancellationToken token = default)
    {
        if (!bundle.Valid) return false;
        foreach (var artifact in bundle.Artifacts) { var file = Path.Combine(path, artifact.Path.Replace('/', Path.DirectorySeparatorChar)); if (!File.Exists(file)) return false; await using var input = File.OpenRead(file); if (input.Length != artifact.Size || Convert.ToHexStringLower(await SHA256.HashDataAsync(input, token)) != artifact.Sha256) return false; }
        return true;
    }
    public async Task<(RecoveryModelBundle Bundle, string Path)?> ActiveAsync(string runtime, CancellationToken token = default)
    {
        if (runtime is not "windowsOcr" and not "foundryLocal") throw new ArgumentException("Unknown runtime.");
        await _gate.WaitAsync(token); try { var pointer = Path.Combine(root, "active." + runtime + ".json"); if (!File.Exists(pointer)) return null; var bundle = DataCodec.Decode<RecoveryModelBundle>(await File.ReadAllBytesAsync(pointer, token)); var path = Path.Combine(root, DirectoryName(bundle)); return bundle.Runtime == runtime && await VerifyAsync(bundle, path, token) ? (bundle, path) : null; } finally { _gate.Release(); }
    }
    public async Task DeleteAsync(string runtime, CancellationToken token = default)
    {
        if (runtime is not "windowsOcr" and not "foundryLocal") throw new ArgumentException("Unknown runtime.");
        await _gate.WaitAsync(token); try { var pointer = Path.Combine(root, "active." + runtime + ".json"); if (!File.Exists(pointer)) return; var bundle = DataCodec.Decode<RecoveryModelBundle>(await File.ReadAllBytesAsync(pointer, token)); if (!bundle.Valid || bundle.Runtime != runtime) throw new InvalidDataException("モデルManifestを確認できません。"); token.ThrowIfCancellationRequested(); File.Delete(pointer); var path = Path.Combine(root, DirectoryName(bundle)); if (Directory.Exists(path)) Directory.Delete(path, true); } finally { _gate.Release(); }
    }
}
