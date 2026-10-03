using System.Security.Cryptography;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Recovery;

namespace Takupoke.Win.Platform;
/// Only model artifact URLs reach this transport. School data never enters it.
public sealed class WindowsRecoveryModels(string root)
{
    public FoundryPinnedModelStore Foundry { get; } = new(root);
    private readonly RecoveryModelBundleStore _store = new(Path.Combine(root, "models", "recovery"));
    public static RecoveryModelBundle OcrBundle { get; } = new("paddle-ppocrv5-mobile", "official-20260526", "windowsOcr", "10.0.26100", 1024L * 1024 * 1024, "Apache-2.0", true,
    [new("det.onnx", "https://huggingface.co/PaddlePaddle/PP-OCRv5_mobile_det_onnx/resolve/e6f4fa85f00e168c862bc462aebca69eef9b3d3d/inference.onnx", 4826518, "a431985659dc921974177a95adcfbb90fd9e51989a5e04d70d0b75f597b6e61d"),
     new("rec.onnx", "https://huggingface.co/PaddlePaddle/PP-OCRv5_mobile_rec_onnx/resolve/ed152b8b495f84de93cda5709d768548a9127622/inference.onnx", 16534782, "da72dc72ca4dc220df0dfde68c1dedc31c58d3e76a25871122e5056227d50092")]);
    private static string DictionaryPath => Path.Combine(AppContext.BaseDirectory, "Assets", "Recovery", "ocr-characters.json");
    private static async Task VerifyDictionary(CancellationToken token) { await using var input = File.OpenRead(DictionaryPath); if (Convert.ToHexStringLower(await SHA256.HashDataAsync(input, token)) != "d858428fbe0ada92b439a5f277b51bc1b318fd985d80946a3ce859d7f2446a92") throw new InvalidDataException("OCR辞書のSHA-256が一致しません。"); }
    public Task CleanupBeforeProvidersAsync(CancellationToken token) => _store.CleanupAbandonedDirectoriesAsync(new HashSet<string>(), token);
    public Task<(RecoveryModelBundle Bundle, string Path)?> OcrInstalledAsync(CancellationToken token) => _store.InstalledAsync("windowsOcr", token);
    public Task<(RecoveryModelBundle Bundle, string Path)?> OcrStateAsync(CancellationToken token) => _store.ActiveAsync("windowsOcr", token);
    public async Task InstallOcrAsync(Action<long, long>? progress, CancellationToken token)
    {
        if (GC.GetGCMemoryInfo().TotalAvailableMemoryBytes < OcrBundle.MinimumMemory) throw new InvalidDataException("OCRを動かすためのメモリを確保できません。");
        await VerifyDictionary(token);
        using var http = new HttpClient(); http.Timeout = Timeout.InfiniteTimeSpan;
        Task<Stream> Download(string url, CancellationToken cancellation) => http.GetStreamAsync(url, cancellation);
        progress?.Invoke(0, OcrBundle.Size);
        await _store.InstallAsync(OcrBundle, Download, async (path, cancellation) =>
        {
            await VerifyDictionary(cancellation);
            await Task.Run(() =>
            {
                using var runtime = new OnnxJapaneseOcr(Path.Combine(path, "det.onnx"), Path.Combine(path, "rec.onnx"), DictionaryPath);
                cancellation.ThrowIfCancellationRequested(); runtime.SmokeTest(cancellation);
            }, cancellation);
        }, token);
        progress?.Invoke(OcrBundle.Size, OcrBundle.Size);
    }
    public Task DeleteOcrAsync(CancellationToken token) => _store.DeleteAsync("windowsOcr", token);
    public async Task<OnnxJapaneseOcr> OpenOcrAsync(CancellationToken token)
    {
        await VerifyDictionary(token); var active = await _store.ActiveAsync("windowsOcr", token);
        if (active is null || active.Value.Bundle != OcrBundle && RecoveryValidator.Fingerprint(active.Value.Bundle) != RecoveryValidator.Fingerprint(OcrBundle)) throw new InvalidDataException("画像PDFを読むには設定から約21 MBの日本語OCRモデルをダウンロードしてください。学校資料は外部へ送信されません。");
        if (GC.GetGCMemoryInfo().TotalAvailableMemoryBytes < OcrBundle.MinimumMemory) throw new InvalidDataException("OCRを動かすためのメモリを確保できません。");
        return await Task.Run(() =>
        {
            var runtime = new OnnxJapaneseOcr(Path.Combine(active.Value.Path, "det.onnx"), Path.Combine(active.Value.Path, "rec.onnx"), DictionaryPath);
            try { token.ThrowIfCancellationRequested(); return runtime; }
            catch { runtime.Dispose(); throw; }
        }, token);
    }
    public Task<IReadOnlyList<ILocalRecoveryProvider>> ProvidersAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyList<ILocalRecoveryProvider>>([new WindowsLanguageRecoveryProvider(), new LazyFoundryRecoveryProvider(Foundry)]); }
}
