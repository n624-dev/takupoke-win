using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;

namespace Takupoke.Infrastructure.Recovery;

internal static class OcrLiteralCodeRetry
{
    internal static bool Accepts(OnnxAsciiOcr.Observation expert, string observedCode, OnnxAsciiOcr.Observation? witness)
    {
        static bool Supported(OnnxAsciiOcr.Observation o) => o.Pieces.Count > 0 && o.Glyphs.Count == o.Pieces.Count &&
            o.PaddingSupported && float.IsFinite(o.RejectProbability) && o.RejectProbability is >= 0 and <= .05f;
        return observedCode.Length is >= 2 and <= 32 && observedCode.Any(char.IsAsciiLetter) &&
            observedCode.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') &&
            Supported(expert) && expert.Text.Length is >= 2 and <= 32 && expert.Text.Any(char.IsAsciiDigit) &&
            expert.Text.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') &&
            (expert.Text.Length == observedCode.Length || witness is not null && Supported(witness) && witness.Text == expert.Text);
    }

    internal static OcrAsciiRefinement.Result Refine(RecoveryRaster raster, IReadOnlyList<PdfGlyph> primary,
        IReadOnlyList<double> scores, IReadOnlyDictionary<int, RecoveryBox> crops, IReadOnlyList<PdfRule> rules,
        OcrAsciiRefinement.Result baseline, OcrInkCropFrame frame,
        Func<bool, RecoveryBox, int, OnnxAsciiOcr.Observation> read, CancellationToken token)
    {
        var output = new List<PdfGlyph>(); var probabilities = new List<double>();
        var top = rules.Where(r => r.Vertical && r.X1 > 2).Select(r => Math.Min(r.Y1, r.Y2)).DefaultIfEmpty(double.NaN).Min();
        var right = rules.Where(r => r.Vertical && r.X1 > 2).Select(r => r.X1).DefaultIfEmpty(double.NaN).Min();
        var previous = baseline.Glyphs.Select((g, i) => (g, i)).GroupBy(p => p.g.SourceLine).ToDictionary(g => g.Key!.Value, g => g.ToArray());
        foreach (var group in primary.Select((g, i) => (g, i)).GroupBy(p => p.g.SourceLine).OrderBy(g => g.Key))
        {
            token.ThrowIfCancellationRequested(); var line = group.Key!.Value; var crop = crops[line];
            var native = group.OrderBy(p => p.g.SourceOrder).ToArray(); var text = string.Concat(native.Select(p => p.g.Text));
            var header = double.IsFinite(right) && double.IsFinite(top) && crop.X + crop.Width < right && crop.Y >= top && text.All(c => c <= 127);
            var start = Array.FindLastIndex(native, p => p.g.Text.Any(c => c > 127)) + 1;
            var role = native.Length >= 4 && string.Concat(native.Take(2).Select(p => p.g.Text)) is "科目" or "担当" or "教室" && native[2].g.Text is ":" or "：";
            if (role) start = Math.Max(start, 3);
            var code = string.Concat(native.Skip(start).Select(p => p.g.Text));
            var eligible = !header && (!double.IsFinite(top) || crop.Y >= top) && code.Length is >= 2 and <= 32 &&
                code.Any(char.IsAsciiLetter) && code.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') && !ClassSelection.Candidates.Contains(code);
            RecoveryBox? source = null;
            if (eligible) source = start == 0 ? crop : OcrWhiteGapPartition.Tail(raster, crop,
                native.Take(start).Max(p => p.g.X + p.g.Width), native.Skip(start).Min(p => p.g.X), token);
            OnnxAsciiOcr.Observation? chosen = null;
            if (source is not null)
            {
                var expert = read(true, frame.Restrict(source), line);
                var witness = expert.Text.Length == code.Length ? null : read(false, source, line);
                if (Accepts(expert, code, witness)) chosen = expert;
            }
            if (chosen is null) foreach (var p in previous[line]) Add(p.g, baseline.Confidences[p.i]);
            else
            {
                foreach (var p in native.Take(start)) Add(p.g, scores[p.i]);
                for (var n = 0; n < chosen.Glyphs.Count; n++) Add(chosen.Glyphs[n], chosen.Pieces[n].Confidence);
            }
        }
        return new(output, probabilities, 0); // Caller counts actual cached/native calls.
        void Add(PdfGlyph glyph, double probability) { output.Add(glyph with { SourceOrder = output.Count }); probabilities.Add(probability); }
    }
}
