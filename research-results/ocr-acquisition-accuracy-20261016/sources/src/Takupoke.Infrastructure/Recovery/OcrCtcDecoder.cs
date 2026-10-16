using Microsoft.ML.OnnxRuntime.Tensors;

namespace Takupoke.Infrastructure.Recovery;

internal sealed record OcrRecognitionPiece(string Text, int Start, int End, float Confidence);

// Shared by production and isolated model comparisons. This is greedy CTC,
// with no vocabulary, normalization, confidence repair or output correction.
internal static class OcrCtcDecoder
{
    internal static IReadOnlyList<OcrRecognitionPiece> Decode(Tensor<float> probabilities,
        IReadOnlyList<string> dictionary, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (dictionary.Count < 2 || dictionary[0] != "" ||
            probabilities.Dimensions.Length != 3 || probabilities.Dimensions[0] != 1 ||
            probabilities.Dimensions[1] <= 0 || probabilities.Dimensions[2] != dictionary.Count)
            throw new InvalidDataException("OCR認識モデルの出力形状が一致しません。");

        // ONNX returns a row-major DenseTensor. Other Tensor layouts retain
        // logical indexing rather than interpreting their buffers as rows.
        ReadOnlySpan<float> values = probabilities is DenseTensor<float> dense && !dense.IsReversedStride
            ? dense.Buffer.Span : [];
        var pieces = new List<OcrRecognitionPiece>();
        var previous = -1;
        var work = 0;
        for (var t = 0; t < probabilities.Dimensions[1]; t++)
        {
            var best = 0;
            var bestScore = -1f;
            for (var c = 0; c < dictionary.Count; c++)
            {
                // Validate EVERY probability, including losing tokens. Fold
                // the former complete validation and argmax scans into one.
                if (++work % 4096 == 0) token.ThrowIfCancellationRequested();
                var score = values.IsEmpty ? probabilities[0, t, c] : values[t * dictionary.Count + c];
                if (!float.IsFinite(score) || score is < 0 or > 1)
                    throw new InvalidDataException("OCR認識の確信度が不正です。");
                if (score > bestScore) { bestScore = score; best = c; }
            }
            if (best != 0 && best != previous)
                pieces.Add(new(dictionary[best], t, t + 1, bestScore));
            else if (best != 0 && pieces.Count > 0)
            {
                var last = pieces[^1];
                pieces[^1] = last with { End = t + 1, Confidence = Math.Max(last.Confidence, bestScore) };
            }
            previous = best;
        }
        token.ThrowIfCancellationRequested();
        return pieces;
    }
}
