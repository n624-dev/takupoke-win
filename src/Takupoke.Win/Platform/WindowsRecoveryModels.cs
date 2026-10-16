using System.Security.Cryptography;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Recovery;

namespace Takupoke.Win.Platform;
/// Only model artifact URLs reach this transport. School data never enters it.
public sealed class WindowsRecoveryModels(string root)
{
    public FoundryPinnedModelStore Foundry { get; } = new(root);
    private readonly RecoveryModelBundleStore _store = new(Path.Combine(root, "models", "recovery"));
    public static RecoveryModelBundle OcrBundle => RecoveryOcrModels.Bundle;
    private static string DictionaryPath => Path.Combine(AppContext.BaseDirectory, "Assets", "Recovery", "ocr-characters-v6.json");
    private static Task VerifyDictionary(CancellationToken token) => RecoveryOcrModels.VerifyDictionaryAsync(DictionaryPath, token);
    public Task CleanupBeforeProvidersAsync(CancellationToken token) => _store.CleanupAbandonedDirectoriesAsync(new HashSet<string>(), token);
    public Task<(RecoveryModelBundle Bundle, string Path)?> OcrInstalledAsync(CancellationToken token) => _store.InstalledAsync("windowsOcr", token);
    public async Task<(RecoveryModelBundle Bundle, string Path)?> OcrStateAsync(CancellationToken token)
    {
        var active = await _store.ActiveAsync("windowsOcr", token);
        return active is { } value && RecoveryValidator.Fingerprint(value.Bundle) == RecoveryValidator.Fingerprint(OcrBundle) ? active : null;
    }
    private static OnnxJapaneseOcr OpenRuntime(string path) => RecoveryOcrModels.OpenRuntime(path, DictionaryPath);
    public async Task InstallOcrAsync(Action<long, long>? progress, CancellationToken token, Action<RecoveryInstallProgress>? phase = null)
    {
        if (GC.GetGCMemoryInfo().TotalAvailableMemoryBytes < OcrBundle.MinimumMemory) throw new InvalidDataException("OCRを動かすためのメモリを確保できません。");
        await VerifyDictionary(token);
        using var http = new HttpClient(); http.Timeout = Timeout.InfiniteTimeSpan;
        Task<Stream> Download(string url, CancellationToken cancellation) => http.GetStreamAsync(url, cancellation);
        await _store.InstallAsync(OcrBundle, Download, async (path, cancellation) =>
        {
            await VerifyDictionary(cancellation);
            await Task.Run(() =>
            {
                using var runtime = OpenRuntime(path);
                cancellation.ThrowIfCancellationRequested(); runtime.SmokeTest(cancellation);
            }, cancellation);
        }, token, update =>
        {
            if (update.Phase == RecoveryInstallPhase.Download) progress?.Invoke(update.DownloadedBytes, update.TotalBytes);
            phase?.Invoke(update);
        });
    }
    public Task DeleteOcrAsync(CancellationToken token) => _store.DeleteAsync("windowsOcr", token);
    public async Task<OnnxJapaneseOcr> OpenOcrAsync(CancellationToken token)
    {
        await VerifyDictionary(token); var active = await OcrStateAsync(token);
        if (active is null || active.Value.Bundle != OcrBundle && RecoveryValidator.Fingerprint(active.Value.Bundle) != RecoveryValidator.Fingerprint(OcrBundle)) throw new InvalidDataException("画像PDFを読むには設定から日本語・英数字OCRモデルを取得または更新してください。学校資料は外部へ送信されません。");
        if (GC.GetGCMemoryInfo().TotalAvailableMemoryBytes < OcrBundle.MinimumMemory) throw new InvalidDataException("OCRを動かすためのメモリを確保できません。");
        return await Task.Run(() =>
        {
            var runtime = OpenRuntime(active.Value.Path);
            try { token.ThrowIfCancellationRequested(); return runtime; }
            catch { runtime.Dispose(); throw; }
        }, token);
    }
    public Task<IReadOnlyList<ILocalRecoveryProvider>> ProvidersAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyList<ILocalRecoveryProvider>>([new WindowsLanguageRecoveryProvider(), new LazyFoundryRecoveryProvider(Foundry)]); }
}
