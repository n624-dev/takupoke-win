using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;

namespace Takupoke.Infrastructure.Recovery;

// Explicitly loaded specialist; the default Japanese OCR route is unchanged.
// Artifact ownership, hashes and the decision to route a crop belong to the
// caller. This runtime cannot attest a crop's script or document completeness.
internal sealed class OnnxAsciiOcr : IDisposable
{
    private readonly InferenceSession _session;
    private readonly string[] _dictionary;

    internal sealed record Observation(int ValidWidth, int InputWidth,
        IReadOnlyList<OcrRecognitionPiece> Pieces, float RejectProbability,
        bool PaddingSupported, IReadOnlyList<PdfGlyph> Glyphs)
    {
        public string Text => string.Concat(Pieces.Select(p => p.Text));
    }

    public OnnxAsciiOcr(string model, string dictionary)
    {
        _dictionary = JsonSerializer.Deserialize<string[]>(File.ReadAllBytes(dictionary))
            ?? throw new InvalidDataException("英数字OCR辞書がありません。");
        if (_dictionary.Length != 438 || _dictionary[0] != "" || _dictionary[^1] != " " ||
            _dictionary.Skip(1).Any(string.IsNullOrEmpty))
            throw new InvalidDataException("英数字OCR辞書が一致しません。");
        OrtEnv.Instance().DisableTelemetryEvents();
        using var options = new SessionOptions { IntraOpNumThreads = 2, InterOpNumThreads = 1,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
        var session = new InferenceSession(model, options);
        try
        {
            if (session.InputMetadata.Count != 2 || session.OutputMetadata.Count != 2 ||
                !session.InputMetadata.TryGetValue("x", out var x) || x.ElementType != typeof(float) ||
                x.Dimensions.Length != 4 || x.Dimensions[0] > 1 || x.Dimensions[1] != 3 ||
                x.Dimensions[2] != 48 || x.Dimensions[3] > 0 ||
                !session.InputMetadata.TryGetValue("valid_width", out var width) || width.ElementType != typeof(long) ||
                width.Dimensions.Length != 1 || width.Dimensions[0] > 1 ||
                !session.OutputMetadata.TryGetValue("probabilities", out var probabilities) ||
                probabilities.ElementType != typeof(float) || probabilities.Dimensions.Length != 3 ||
                probabilities.Dimensions[0] > 1 || probabilities.Dimensions[2] != 438 ||
                !session.OutputMetadata.TryGetValue("reject_probability", out var reject) ||
                reject.ElementType != typeof(float) || reject.Dimensions.Length != 1 || reject.Dimensions[0] > 1)
                throw new InvalidDataException("英数字OCRモデルの形状が一致しません。");
            _session = session;
        }
        catch { session.Dispose(); throw; }
    }

    public Observation Read(RecoveryRaster raster, RecoveryBox crop, int line, int order,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var input = OcrInputTransform.Recognition(raster, crop, token);
        var width = new DenseTensor<long>(new long[] { input.ValidWidth }, new[] { 1 });
        using var output = _session.Run([NamedOnnxValue.CreateFromTensor("x", input.Tensor),
            NamedOnnxValue.CreateFromTensor("valid_width", width)]);
        token.ThrowIfCancellationRequested();
        var probabilities = output.Single(o => o.Name == "probabilities").AsTensor<float>();
        var reject = output.Single(o => o.Name == "reject_probability").AsTensor<float>();
        if (reject.Dimensions.Length != 1 || reject.Dimensions[0] != 1 ||
            !float.IsFinite(reject[0]) || reject[0] is < 0 or > 1)
            throw new InvalidDataException("英数字OCRの拒否確率が不正です。");
        var pieces = OcrCtcDecoder.Decode(probabilities, _dictionary, token).ToArray();
        var glyphs = new List<PdfGlyph>(); var supported = true;
        foreach (var piece in pieces)
        {
            token.ThrowIfCancellationRequested();
            try { glyphs.Add(input.SourceGlyph(piece.Text, piece.Start, piece.End,
                probabilities.Dimensions[1], line, order + glyphs.Count)); }
            catch (InvalidDataException) { supported = false; }
        }
        // Keep every decoded piece, including unsupported padding, for the
        // caller's diagnostic. A partial glyph list cannot become evidence.
        return new(input.ValidWidth, input.InputWidth, Array.AsReadOnly(pieces),
            reject[0], supported, supported ? glyphs.AsReadOnly() : Array.Empty<PdfGlyph>());
    }

    public void Dispose() => _session.Dispose();
}
