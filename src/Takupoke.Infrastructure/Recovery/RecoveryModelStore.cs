using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Storage;

namespace Takupoke.Infrastructure.Recovery;

/// The transport only receives a pinned model URL. It has no PDF/prompt/result parameter.
public sealed class RecoveryModelStore(string root)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async Task<string> InstallAsync(RecoveryModelManifest manifest, string runtime, long availableMemory,
        bool osSupported, Func<string, CancellationToken, Task<Stream>> openModel,
        Func<string, CancellationToken, Task> prepareAndSmokeTest, CancellationToken token = default)
    {
        if (!osSupported || !manifest.IsUsable(runtime, availableMemory)) throw new InvalidDataException("検証済みモデルと端末条件を確認できません。");
        await _gate.WaitAsync(token);
        var staging = Path.Combine(root, "staging-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, runtime + "-" + manifest.StorageKey + "-" + manifest.Version + "-" + manifest.Sha256 + ".model");
        var ownsTarget = false; var committed = false;
        try
        {
            Directory.CreateDirectory(root);
            var pointer = Path.Combine(root, "active." + runtime + ".json");
            var previous = File.Exists(pointer) ? DataCodec.Decode<RecoveryModelManifest>(await File.ReadAllBytesAsync(pointer, token)) : null;
            if (previous is not null && !previous.IsUsable(runtime, long.MaxValue)) throw new InvalidDataException("保存モデルの情報を確認できません。");
            if (!File.Exists(target))
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using var input = await openModel(manifest.Url, token);
                await using (var output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous))
                {
                    var buffer = new byte[64 * 1024]; long total = 0;
                    int read;
                    while ((read = await input.ReadAsync(buffer, token)) > 0)
                    {
                        total += read; if (total > manifest.Size) throw new InvalidDataException("モデルのサイズが一致しません。");
                        hash.AppendData(buffer, 0, read); await output.WriteAsync(buffer.AsMemory(0, read), token);
                    }
                    await output.FlushAsync(token); output.Flush(true);
                    if (total != manifest.Size || Convert.ToHexStringLower(hash.GetHashAndReset()) != manifest.Sha256) throw new InvalidDataException("モデルのハッシュが一致しません。");
                }
                await prepareAndSmokeTest(staging, token); token.ThrowIfCancellationRequested(); File.Move(staging, target); ownsTarget = true;
            }
            else
            {
                await using var existing = File.OpenRead(target);
                if (existing.Length != manifest.Size || Convert.ToHexStringLower(await SHA256.HashDataAsync(existing, token)) != manifest.Sha256) throw new InvalidDataException("保存モデルのハッシュが一致しません。");
                await prepareAndSmokeTest(target, token);
            }
            // A failed preparation/download never alters the previous active pointer.
            await DataCodec.AtomicWriteAsync(Path.Combine(root, "active." + runtime + ".json"), DataCodec.Encode(manifest), token);
            committed = true;
            if (previous is not null)
            {
                var old = Path.Combine(root, runtime + "-" + previous.StorageKey + "-" + previous.Version + "-" + previous.Sha256 + ".model");
                try { if (old != target && File.Exists(old)) File.Delete(old); } catch (IOException) { /* Valid new pointer is already committed. */ }
            }
            return target;
        }
        finally { try { if (File.Exists(staging)) File.Delete(staging); if (ownsTarget && !committed && File.Exists(target)) File.Delete(target); } finally { _gate.Release(); } }
    }
    /// Call before providers are started, retaining all models leased by live runtimes.
    public async Task CleanupAbandonedFilesAsync(IReadOnlySet<string> inUse, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (!Directory.Exists(root)) return;
            var protectedFiles = inUse.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var runtime in new[] { "coreAI", "llamaCpp", "liteRtLm", "foundryLocal" })
            {
                var pointer = Path.Combine(root, "active." + runtime + ".json");
                if (File.Exists(pointer))
                {
                    var m = DataCodec.Decode<RecoveryModelManifest>(await File.ReadAllBytesAsync(pointer, token));
                    if (!m.IsUsable(runtime, long.MaxValue)) throw new InvalidDataException("保存モデルの情報を確認できません。");
                    protectedFiles.Add(Path.GetFullPath(Path.Combine(root, runtime + "-" + m.StorageKey + "-" + m.Version + "-" + m.Sha256 + ".model")));
                }
            }
            foreach (var file in Directory.EnumerateFiles(root))
            {
                token.ThrowIfCancellationRequested();
                var owned = Regex.IsMatch(Path.GetFileName(file), @"^(?:staging-[a-f0-9]{32}|active\.(?:coreAI|llamaCpp|liteRtLm|foundryLocal)\.json\.[a-f0-9]{32}\.tmp|(?:coreAI|llamaCpp|liteRtLm|foundryLocal)-[A-Za-z0-9._-]{1,81}-[A-Za-z0-9._-]{1,81}-[a-f0-9]{64}\.model)$");
                if (owned && !protectedFiles.Contains(Path.GetFullPath(file)) && (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0) File.Delete(file);
            }
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(string runtime, CancellationToken token = default)
    {
        if (runtime is not ("coreAI" or "llamaCpp" or "liteRtLm" or "foundryLocal")) throw new ArgumentException("未対応のRuntimeです。");
        await _gate.WaitAsync(token);
        try
        {
            var pointer = Path.Combine(root, "active." + runtime + ".json");
            if (!File.Exists(pointer)) return;
            var manifest = DataCodec.Decode<RecoveryModelManifest>(await File.ReadAllBytesAsync(pointer, token));
            if (!manifest.IsUsable(runtime, long.MaxValue)) throw new InvalidDataException("保存モデルの情報を確認できません。");
            File.Delete(pointer);
            var target = Path.Combine(root, runtime + "-" + manifest.StorageKey + "-" + manifest.Version + "-" + manifest.Sha256 + ".model");
            if (File.Exists(target)) File.Delete(target);
        }
        finally { _gate.Release(); }
    }
}
