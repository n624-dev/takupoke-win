using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.Logging.Abstractions;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Recovery;
using Takupoke.Infrastructure.Storage;

namespace Takupoke.Win.Platform;
public sealed record FoundryPinnedArtifact(string Path, long Size, string Sha256);
public sealed record FoundryPinnedManifest(string ModelId, string Version, long Size, long MinimumMemory, string MinimumOs, string License, bool Validated, IReadOnlyList<FoundryPinnedArtifact> Artifacts)
{
    public bool WellFormed => Regex.IsMatch(ModelId, "^[A-Za-z0-9][A-Za-z0-9._-]{0,80}:[0-9]{1,8}$") && Regex.IsMatch(Version, "^[0-9]{1,8}$") && Size > 0 && MinimumMemory > 0 && License.Length > 0 && Artifacts.Count is > 0 and <= 128 && Artifacts.Sum(a => a.Size) == Size && Artifacts.Select(a => a.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() == Artifacts.Count && Artifacts.All(a => a.Size > 0 && Regex.IsMatch(a.Sha256, "^[a-f0-9]{64}$") && a.Path.Split('/').All(p => Regex.IsMatch(p, "^[a-zA-Z0-9._-]+$") && p is not "." and not ".."));
    public RecoveryModelManifest ProviderManifest => new(ModelId, Version, "https://ai.azure.com/public-model-catalog", Size, RecoveryValidator.Fingerprint(Artifacts), "foundryLocal", MinimumOs, MinimumMemory, "CPU", License, Validated);
}
/// Fixed-ID SDK download with an app-owned cache and complete artifact whitelist.
/// Active pointer changes only after SHA verification and local structured-output
/// smoke testing. Failed candidates retain the previous usable model.
public sealed class FoundryPinnedModelStore(string root)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private sealed record InstalledRecord(FoundryPinnedManifest Manifest, string CacheDirectory);
    private string CacheRoot => Path.Combine(root, "models", "foundry-cache");
    private string Pointer => Path.Combine(root, "models", "foundry-active.json");
    public static IReadOnlyList<FoundryPinnedManifest> Candidates => DataCodec.Decode<FoundryPinnedManifest[]>(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "Recovery", "foundry-candidates.json")));
    private static bool Approved(FoundryPinnedManifest m) => m.WellFormed && m.Validated && Candidates.Any(c => c.WellFormed && c.Validated && RecoveryValidator.Fingerprint(c) == RecoveryValidator.Fingerprint(m));
    private async Task<ICatalog> CatalogAsync(CancellationToken token)
    {
        Environment.SetEnvironmentVariable("ORT_TELEMETRY_DISABLED", "1");
        if (!FoundryLocalManager.IsInitialized) await FoundryLocalManager.CreateAsync(new Configuration { AppName = "takupoke", AppDataDir = Path.Combine(root, "models", "foundry-state"), ModelCacheDir = CacheRoot, LogsDir = Path.Combine(root, "school", "recovery-runtime-logs"), AdditionalSettings = new Dictionary<string, string> { ["DisableNonessentialTelemetry"] = "true" } }, NullLogger.Instance, token);
        return await FoundryLocalManager.Instance.GetCatalogAsync(token);
    }
    private async Task<IModel> ModelAsync(FoundryPinnedManifest manifest, CancellationToken token)
    { var catalog = await CatalogAsync(token); var model = await catalog.GetModelVariantAsync(manifest.ModelId, token); return model?.Id == manifest.ModelId ? model : throw new InvalidDataException("固定されたモデルの版を確認できません。"); }
    private async Task<bool> VerifyAsync(IModel model, FoundryPinnedManifest manifest, CancellationToken token)
    {
        if (!Approved(manifest)) return false;
        var path = await model.GetPathAsync(token); if (path is null || !Directory.Exists(path) || !Path.GetFullPath(path).StartsWith(Path.GetFullPath(CacheRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
        var actual = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(path, p).Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!actual.SetEquals(manifest.Artifacts.Select(a => a.Path))) return false;
        foreach (var a in manifest.Artifacts) { await using var input = File.OpenRead(Path.Combine(path, a.Path.Replace('/', Path.DirectorySeparatorChar))); if (input.Length != a.Size || Convert.ToHexStringLower(await SHA256.HashDataAsync(input, token)) != a.Sha256) return false; }
        return true;
    }
    // An installed but revoked model remains visible for deletion. Approval is
    // required separately by ActiveAsync/OpenAsync before any inference.
    private async Task<InstalledRecord?> InstalledRecordAsync(CancellationToken token)
    {
        if (!File.Exists(Pointer)) return null;
        var bytes = await File.ReadAllBytesAsync(Pointer, token);
        using var json = JsonDocument.Parse(bytes);
        var record = json.RootElement.TryGetProperty("manifest", out _) ? DataCodec.Decode<InstalledRecord>(bytes) : new(DataCodec.Decode<FoundryPinnedManifest>(bytes), "");
        if (!record.Manifest.WellFormed || record.CacheDirectory.Length > 0 && !OwnedCachePath(record.CacheDirectory))
            throw new InvalidDataException("保存モデルManifestを確認できません。");
        return record;
    }
    private bool OwnedCachePath(string path) => Path.IsPathFullyQualified(path) &&
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(CacheRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public async Task<FoundryPinnedManifest?> InstalledAsync(CancellationToken token) => (await InstalledRecordAsync(token))?.Manifest;
    public async Task<FoundryPinnedManifest?> ActiveAsync(CancellationToken token)
    { var m = await InstalledAsync(token); return m is not null && Approved(m) ? m : null; }
    public async Task<FoundryLocalRecoveryProvider?> OpenAsync(CancellationToken token)
    {
        var active = await ActiveAsync(token); if (active is null) return null;
        var memory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes; if (!active.ProviderManifest.SupportsOs(Environment.OSVersion.Version.ToString()) || memory < active.MinimumMemory) return null;
        var model = await ModelAsync(active, token);
        return new(model, active.ProviderManifest, t => VerifyAsync(model, active, t), memory);
    }
    public async Task InstallAsync(FoundryPinnedManifest manifest, Action<float>? progress, CancellationToken token)
    {
        if (!Approved(manifest) || !manifest.ProviderManifest.SupportsOs(Environment.OSVersion.Version.ToString()) || GC.GetGCMemoryInfo().TotalAvailableMemoryBytes < manifest.MinimumMemory) throw new InvalidDataException("この端末で検証済みのモデルを利用できません。");
        await _gate.WaitAsync(token); IModel? candidate = null; var wasCached = false; var committed = false;
        try
        {
            var previous = await InstalledAsync(token); candidate = await ModelAsync(manifest, token); var originalPath = await candidate.GetPathAsync(token); wasCached = originalPath is not null && Directory.Exists(originalPath);
            await candidate.DownloadAsync(progress, token); if (!await VerifyAsync(candidate, manifest, token)) throw new InvalidDataException("取得したモデルのSHA-256が一致しません。");
            await using (var provider = new FoundryLocalRecoveryProvider(candidate, manifest.ProviderManifest, t => VerifyAsync(candidate, manifest, t), GC.GetGCMemoryInfo().TotalAvailableMemoryBytes))
            {
                var prompt = new RecoveryPromptCell("synthetic-smoke", [new("3_CN", "1", 1)], [new("s", "架空科目A"), new("t", "架空教員B"), new("r", "架空教室C")], [], 1, [new(["s"], ["t"], ["r"])]);
                var output = await provider.RecoverCellAsync(prompt, token);
                if (output.Count != 1 || output[0].Subject.Value != "架空科目A" || output[0].Teacher.Value != "架空教員B" || output[0].Room.Value != "架空教室C" || !output[0].Subject.Evidence.SequenceEqual(["s"]) || !output[0].Teacher.Evidence.SequenceEqual(["t"]) || !output[0].Room.Evidence.SequenceEqual(["r"])) throw new InvalidRecoveryOutputException();
            }
            var installedPath = await candidate.GetPathAsync(token);
            if (installedPath is null || !OwnedCachePath(installedPath)) throw new InvalidDataException("モデルの保存先を確認できません。");
            token.ThrowIfCancellationRequested(); await DataCodec.AtomicWriteAsync(Pointer, DataCodec.Encode(new InstalledRecord(manifest, Path.GetFullPath(installedPath))), token); committed = true;
            if (previous is not null && previous.ModelId != manifest.ModelId)
            { try { var old = await ModelAsync(previous, token); await old.RemoveFromCacheAsync(token); } catch (Exception e) when (e is IOException or InvalidDataException) { /* New verified pointer remains active. */ } }
        }
        finally { try { if (!wasCached && !committed && candidate is not null) await candidate.RemoveFromCacheAsync(CancellationToken.None); } finally { _gate.Release(); } }
    }
    public async Task DeleteAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            var installed = await InstalledRecordAsync(token); if (installed is null) return;
            token.ThrowIfCancellationRequested();
            if (installed.CacheDirectory.Length > 0)
            {
                // The app owns this SDK cache root and records the verified
                // directory at installation. Deletion remains possible if a
                // model is later removed from the catalog or approval list.
                if (Directory.Exists(installed.CacheDirectory)) Directory.Delete(installed.CacheDirectory, true);
            }
            else
            {
                var model = await ModelAsync(installed.Manifest, token);
                await model.RemoveFromCacheAsync(token);
            }
            // Keep the retryable ownership pointer until removal succeeds.
            File.Delete(Pointer);
        }
        finally { _gate.Release(); }
    }

}
public sealed class LazyFoundryRecoveryProvider(FoundryPinnedModelStore models) : ILocalRecoveryProvider, IAsyncDisposable
{
    private FoundryLocalRecoveryProvider? _provider;
    public string Id => "foundryLocal"; public bool LocalOnly => true;
    public RecoveryMetadata Metadata => _provider?.Metadata ?? new(Id, "not-ready", "not-ready", "1.2.4", "1", RecoveryValidator.SchemaVersion, RecoveryValidator.Version, Environment.OSVersion.VersionString);
    public async Task<LocalProviderState> AvailabilityAsync(CancellationToken token) { _provider ??= await models.OpenAsync(token); return _provider is null ? LocalProviderState.DownloadRequired : await _provider.AvailabilityAsync(token); }
    public Task<IReadOnlyList<RecoveryLesson>> RecoverCellAsync(RecoveryPromptCell cell, CancellationToken token) => _provider is not null ? _provider.RecoverCellAsync(cell, token) : throw new InvalidDataException("ローカルモデルを準備してください。");
    public async ValueTask DisposeAsync() { if (_provider is not null) await _provider.DisposeAsync(); }
}
