using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;

namespace Takupoke.Infrastructure.Recovery;

internal static class OcrAsciiRefinement
{
    internal sealed record Result(IReadOnlyList<PdfGlyph> Glyphs, IReadOnlyList<double> Confidences, int RecognizerCalls);
    public static Result Refine(RecoveryRaster raster, IReadOnlyList<PdfGlyph> primary,
        IReadOnlyList<double> scores, IReadOnlyDictionary<int, RecoveryBox> crops,
        IReadOnlyList<PdfRule> rules, OnnxAsciiOcr ascii, CancellationToken token = default, bool retainUncertain = false,
        OnnxAsciiOcr? manualCodeAscii = null, bool manualCodeRequiresLetter = false, bool retryLiteralCodes = false)
    {
        if (primary.Count != scores.Count) throw new InvalidDataException("OCRの文字と確信度が一致しません。");
        var right = rules.Where(l => l.Vertical && l.X1 > 2).Select(l => l.X1).DefaultIfEmpty(double.NaN).Min();
        var top = rules.Where(l => l.Vertical && l.X1 > 2).Select(l => Math.Min(l.Y1,l.Y2)).DefaultIfEmpty(double.NaN).Min();
        var glyphs = new List<PdfGlyph>(); var confidences = new List<double>(); var calls = 0;
        var cache = retryLiteralCodes && retainUncertain && manualCodeAscii is not null
            ? new Dictionary<(OnnxAsciiOcr Model, RecoveryBox Crop, int Line), OnnxAsciiOcr.Observation>() : null;
        OnnxAsciiOcr.Observation Read(OnnxAsciiOcr model, RecoveryBox crop, int line)
        {
            if (cache is not null && cache.TryGetValue((model, crop, line), out var previous)) return previous;
            calls++; var observed = model.Read(raster, crop, line, glyphs.Count, token);
            cache?.Add((model, crop, line), observed); return observed;
        }
        // One bounded pixel-work counter for this page. A null mask preserves
        // every original ink pixel, including anything resembling a table rule.
        var codeFrame = retainUncertain && manualCodeAscii is not null ? new OcrInkCropFrame(raster, null, token) : null;
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
            // Separately detected room/course codes can have no Japanese prefix.
            // Route bounded literal alphanumeric/numeric codes; one-digit period
            // headings, prose and punctuation retain the primary observation.
            var standaloneCode = !header && tailStart == 0 && text.Length is >= 2 and <= 32 &&
                text.Any(char.IsAsciiDigit) && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
            RecoveryBox? tailCrop = null;
            if (!header && tailStart > 0 && tailText.Length is >= 2 and <= 32 && tailText.All(c => c is >= ' ' and <= '~'))
                tailCrop = OcrWhiteGapPartition.Tail(raster, crop,
                    native.Take(tailStart).Max(x => x.g.X + x.g.Width), tail.Min(x => x.g.X), token);
            var codeText = standaloneCode ? text : tailText;
            var manualCode = retainUncertain && (standaloneCode || tailCrop is not null) &&
                codeText.Length is >= 2 and <= 32 && codeText.Any(char.IsAsciiDigit) &&
                (!manualCodeRequiresLetter || codeText.Any(char.IsAsciiLetter)) &&
                codeText.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') &&
                !ClassSelection.Candidates.Contains(codeText) && (!double.IsFinite(top) || crop.Y >= top);
            var chosen = false;
            if (header || standaloneCode || tailCrop is not null)
            {
                var sourceCrop = header || standaloneCode ? crop : tailCrop!;
                OnnxAsciiOcr.Observation? observation = null;
                if (manualCode && manualCodeAscii is not null)
                {
                    var expert = Read(manualCodeAscii, codeFrame!.Restrict(sourceCrop), line);
                    if (expert.Pieces.Count > 0 && expert.RejectProbability <= .05f && expert.PaddingSupported &&
                        expert.Text.Length == codeText.Length && expert.Text.Any(char.IsAsciiDigit) &&
                        expert.Text.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
                        observation = expert;
                }
                if (observation is null)
                {
                    // Invalid expert output uses the complete original v1
                    // decision, rather than falling straight back to Japanese.
                    observation = Read(ascii, sourceCrop, line);
                }
                chosen = observation.Pieces.Count > 0 && observation.RejectProbability <= .05f && observation.PaddingSupported &&
                    observation.Text.All(c => c is >= ' ' and <= '~') && (header
                        ? ClassSelection.Candidates.Contains(observation.Text)
                        : observation.Text.Length == (standaloneCode ? text.Length : tailText.Length) &&
                            (observation.Pieces.All(p => p.Confidence >= .8f) ||
                                // Manual capture already requires original-pixel review
                                // for every native score below .8. Preserve these
                                // scores instead of falling back to an often-wrong
                                // high-confidence primary code. Japanese prefixes
                                // stay untouched; a tail needs the existing
                                // original white-gap partition. Prose/headers
                                // retain their existing selection conditions.
                                manualCode && observation.Text.Any(char.IsAsciiDigit) &&
                                observation.Text.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')));
                if (chosen)
                {
                    if (!header && !standaloneCode) foreach (var (glyph,index) in native.Take(tailStart)) Add(glyph, scores[index]);
                    for (var n = 0; n < observation.Glyphs.Count; n++) Add(observation.Glyphs[n], observation.Pieces[n].Confidence);
                }
            }
            if (!chosen) foreach (var (glyph,index) in native) Add(glyph, scores[index]);
        }
        var result = new Result(glyphs.ToArray(), confidences.ToArray(), calls);
        if (cache is not null)
        {
            result = OcrLiteralCodeRetry.Refine(raster, primary, scores, crops, rules, result, codeFrame!,
                (expert, crop, line) => Read(expert ? manualCodeAscii! : ascii, crop, line), token);
            result = result with { RecognizerCalls = calls };
        }
        return result;
        void Add(PdfGlyph glyph, double confidence)
        {
            glyphs.Add(glyph with { SourceOrder = glyphs.Count }); confidences.Add(confidence);
        }
    }
}
