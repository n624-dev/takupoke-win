using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Takupoke.Infrastructure.Parsing;

namespace Takupoke.Infrastructure.Recovery;

internal sealed class OnnxJapaneseAlternative : IDisposable
{
    private readonly InferenceSession _session;
    private readonly string[] _dictionary;
    internal sealed record Observation(IReadOnlyList<OcrRecognitionPiece> Pieces, int TimeCount,
        bool PaddingSupported, IReadOnlyList<PdfGlyph> Glyphs);

    internal OnnxJapaneseAlternative(string model, string dictionary)
    {
        _dictionary = JsonSerializer.Deserialize<string[]>(File.ReadAllBytes(dictionary)) ?? throw new InvalidDataException("日本語補助OCR辞書がありません。");
        // Preserve the native dictionary, including its two distinct space tokens.
        if (_dictionary.Length != 4401 || _dictionary[0] != "" || _dictionary[^1] != " " || _dictionary.Skip(1).Any(string.IsNullOrEmpty))
            throw new InvalidDataException("日本語補助OCR辞書が一致しません。");
        OrtEnv.Instance().DisableTelemetryEvents();
        using var options = new SessionOptions { IntraOpNumThreads = 2, InterOpNumThreads = 1,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL, LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
        var session = new InferenceSession(model, options);
        try
        {
            if (session.InputMetadata.Count != 1 || !session.InputMetadata.TryGetValue("x", out var input) ||
                input.ElementType != typeof(float) || input.Dimensions.Length != 4 || input.Dimensions[1] != 3 || input.Dimensions[2] != 48 ||
                session.OutputMetadata.Count != 1 || session.OutputMetadata.Values.Single().ElementType != typeof(float) ||
                session.OutputMetadata.Values.Single().Dimensions.Length != 3 || session.OutputMetadata.Values.Single().Dimensions[2] != 4401)
                throw new InvalidDataException("日本語補助OCRモデルの形状が一致しません。");
            _session = session;
        }
        catch { session.Dispose(); throw; }
    }

    internal Observation Read(OcrInputTransform.RecognitionInput input, int line, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var output = _session.Run([NamedOnnxValue.CreateFromTensor("x", input.Tensor)]);
        token.ThrowIfCancellationRequested();
        var probability = output.Single().AsTensor<float>();
        var pieces = OcrCtcDecoder.Decode(probability, _dictionary, token).ToArray();
        var glyphs = new List<PdfGlyph>(); var supported = true;
        foreach (var p in pieces)
        {
            token.ThrowIfCancellationRequested();
            try { glyphs.Add(input.SourceGlyph(p.Text, p.Start, p.End, probability.Dimensions[1], line, glyphs.Count)); }
            catch (InvalidDataException) { supported = false; }
        }
        return new(pieces, probability.Dimensions[1], supported, supported ? glyphs : []);
    }

    public void Dispose() => _session.Dispose();
}
