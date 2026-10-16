using System.Security.Cryptography;
using Takupoke.Core.Recovery;

namespace Takupoke.Infrastructure.Recovery;

// Shared deployment contract used by Windows and the native validation tools.
public static class RecoveryOcrModels
{
    public static RecoveryModelBundle Bundle { get; } = new("paddle-v6-english-code", "20261016-v3", "windowsOcr", "10.0.26100", 2L * 1024 * 1024 * 1024, "Apache-2.0", true,
    [new("det.onnx", "https://huggingface.co/PaddlePaddle/PP-OCRv5_mobile_det_onnx/resolve/e6f4fa85f00e168c862bc462aebca69eef9b3d3d/inference.onnx", 4826518, "a431985659dc921974177a95adcfbb90fd9e51989a5e04d70d0b75f597b6e61d"),
     new("rec.onnx", "https://huggingface.co/PaddlePaddle/PP-OCRv6_medium_rec_onnx/resolve/50c7eacafc52fa7bcf4194e8cd08e46f8558504b/inference.onnx", 76554979, "9c09abf0957f7968c7586464b7397b84ad2387a0497a351af40e9acc71b673ba"),
     new("ascii-rec.onnx", "https://raw.githubusercontent.com/n624-dev/takupoke-win/5826ffe2f1f2717deaf5ca03e2506996d4b3241c/model-artifacts/ascii-english-v5-font-balanced-v1/model.onnx", 22183975, "9dd3ed77db4f2af171cf434d200c666033c9bdfe5f33446bc47dbfecac90d703"),
     new("ascii-dictionary.json", "https://raw.githubusercontent.com/n624-dev/takupoke-win/5826ffe2f1f2717deaf5ca03e2506996d4b3241c/model-artifacts/ascii-english-v5-font-balanced-v1/dictionary.json", 3614, "fbf156ed886e4c46075f599651e60a0c38953dce89e10d999c765014477a7368"),
     new("manual-code-rec.onnx", "https://raw.githubusercontent.com/n624-dev/takupoke-win/4ff7d2c4c894c8748d9c6c4182d15dde2e962904/model-artifacts/ascii-english-v5-japanese-fonts-v3-epoch1-exploratory/model.onnx", 22183975, "95bab179e0d6cc75269419078d68696cd4679661f94f6128b858b7a074fe2971"),
     new("japanese-uncertain-rec.onnx", "https://huggingface.co/tobiichioriguchi/japan_PP-OCRv3_mobile_rec_onnx/resolve/1c772b3dd782e6ac4e61aba8bfe9683bee3334fc/inference.onnx", 10089078, "2e397c27461751befc82eca78c9b19e87322ebee34e7316468b21856f9977d64"),
     new("japanese-uncertain-dictionary.json", "https://raw.githubusercontent.com/n624-dev/takupoke-win/fad0717ef25f507632cb13ed7da1f0abab0c994a/model-artifacts/japanese-v3-conditional-expert/dictionary.json", 26140, "0cda0d37debec2f0481bb5bb7bf3223b159d8268d288781eef1668455a811f1d")]);
    public const string DictionarySha256 = "a49d51120786924a642c9e8a14b2a2ab73c019699abf5b2b6970ec7355c0415f";
    public static async Task VerifyDictionaryAsync(string dictionary, CancellationToken token)
    {
        await using var input = File.OpenRead(dictionary);
        if (Convert.ToHexStringLower(await SHA256.HashDataAsync(input, token)) != DictionarySha256)
            throw new InvalidDataException("OCR辞書のSHA-256が一致しません。");
    }
    public static OnnxJapaneseOcr OpenRuntime(string path, string dictionary) => new(Path.Combine(path, "det.onnx"),
        Path.Combine(path, "rec.onnx"), dictionary, 18710, Path.Combine(path, "ascii-rec.onnx"),
        Path.Combine(path, "ascii-dictionary.json"), frameOriginalInk: true,
        manualCodeModel: Path.Combine(path, "manual-code-rec.onnx"), retryUnpaddedInput: true,
        allowContainedRuleContact: true, retainVerifiedWhitespace: true, retryRoleLabels: true, manualCodeRequiresLetter: true,
        retryLiteralCodes: true, uncertainJapaneseModel: Path.Combine(path, "japanese-uncertain-rec.onnx"),
        uncertainJapaneseDictionary: Path.Combine(path, "japanese-uncertain-dictionary.json"));
}
