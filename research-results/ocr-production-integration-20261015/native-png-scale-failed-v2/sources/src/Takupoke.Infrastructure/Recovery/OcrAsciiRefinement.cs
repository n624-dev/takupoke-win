using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;

namespace Takupoke.Infrastructure.Recovery;

internal static class OcrAsciiRefinement
{
    internal sealed record Result(IReadOnlyList<PdfGlyph> Glyphs, IReadOnlyList<double> Confidences, int RecognizerCalls);
    public static Result Refine(RecoveryRaster raster, IReadOnlyList<PdfGlyph> primary,
        IReadOnlyList<double> scores, IReadOnlyDictionary<int, RecoveryBox> crops,
        IReadOnlyList<PdfRule> rules, OnnxAsciiOcr ascii, CancellationToken token = default)
    {
        if (primary.Count != scores.Count) throw new InvalidDataException("OCRの文字と確信度が一致しません。");
        var right = rules.Where(l => l.Vertical && l.X1 > 2).Select(l => l.X1).DefaultIfEmpty(double.NaN).Min();
        var top = rules.Where(l => l.Vertical && l.X1 > 2).Select(l => Math.Min(l.Y1,l.Y2)).DefaultIfEmpty(double.NaN).Min();
        var glyphs = new List<PdfGlyph>(); var confidences = new List<double>(); var calls = 0;
        foreach (var group in primary.Select((g,i) => (g,i)).GroupBy(x => x.g.SourceLine).OrderBy(g => g.Key))
        {
            token.ThrowIfCancellationRequested();
            if (group.Key is not int line || !crops.TryGetValue(line, out var crop))
                throw new InvalidDataException("OCRの元行範囲がありません。");
            var native = group.OrderBy(x => x.g.SourceOrder).ToArray();
            var text = string.Concat(native.Select(x => x.g.Text));
            var header = double.IsFinite(right) && double.IsFinite(top) && crop.X + crop.Width < right && crop.Y >= top &&
                text.Length > 0 && text.All(c => c <= 127);
            var tailStart = Array.FindLastIndex(native, x => x.g.Text.Any(c => c > 127)) + 1;
            var tail = native.Skip(tailStart).ToArray(); var tailText = string.Concat(tail.Select(x => x.g.Text));
            RecoveryBox? tailCrop = null;
            if (!header && tailStart > 0 && tailText.Length is >= 2 and <= 32 && tailText.All(c => c is >= ' ' and <= '~'))
                tailCrop = OcrWhiteGapPartition.Tail(raster, crop,
                    native.Take(tailStart).Max(x => x.g.X + x.g.Width), tail.Min(x => x.g.X), token);
            var chosen = false;
            if (header || tailCrop is not null)
            {
                calls++; var observation = ascii.Read(raster, header ? crop : tailCrop!, line, glyphs.Count, token);
                chosen = observation.Pieces.Count > 0 && observation.RejectProbability <= .05f && observation.PaddingSupported &&
                    observation.Text.All(c => c is >= ' ' and <= '~') && (header
                        ? ClassSelection.Candidates.Contains(observation.Text)
                        : observation.Pieces.All(p => p.Confidence >= .8f) && observation.Text.Length == tailText.Length);
                if (chosen)
                {
                    if (!header) foreach (var (glyph,index) in native.Take(tailStart)) Add(glyph, scores[index]);
                    for (var n = 0; n < observation.Glyphs.Count; n++) Add(observation.Glyphs[n], observation.Pieces[n].Confidence);
                }
            }
            if (!chosen) foreach (var (glyph,index) in native) Add(glyph, scores[index]);
        }
        return new(glyphs.ToArray(), confidences.ToArray(), calls);
        void Add(PdfGlyph glyph, double confidence)
        {
            glyphs.Add(glyph with { SourceOrder = glyphs.Count }); confidences.Add(confidence);
        }
    }
}
