using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;
using Takupoke.Infrastructure.Recovery;

// Development candidates only. Never reads expected literals, rosters or gold.
internal static class StudyRefinement
{
    internal sealed record Policy(string Name, bool DiscoverLetters, bool RecoverLength, bool Symbols, bool RequireAgreement);
    internal static readonly Policy[] Policies = [
        new("letters", true, false, false, false),
        new("length", false, true, false, false),
        new("letters-length", true, true, false, false),
        new("letters-length-agreement", true, true, false, true),
        new("letters-length-symbols", true, true, true, false),
        new("letters-length-symbols-agreement", true, true, true, true)
    ];

    internal static OcrAsciiRefinement.Result Retry(RecoveryRaster raster, IReadOnlyList<PdfGlyph> primary,
        IReadOnlyList<double> scores, IReadOnlyDictionary<int, RecoveryBox> crops, IReadOnlyList<PdfRule> rules,
        OcrAsciiRefinement.Result baseline, Policy policy,
        Func<int, RecoveryBox, int, OnnxAsciiOcr.Observation> read, CancellationToken token)
    {
        var result = new List<PdfGlyph>(); var confidence = new List<double>(); var calls = 0;
        var top = rules.Where(r => r.Vertical && r.X1 > 2).Select(r => Math.Min(r.Y1, r.Y2)).DefaultIfEmpty(double.NaN).Min();
        var right = rules.Where(r => r.Vertical && r.X1 > 2).Select(r => r.X1).DefaultIfEmpty(double.NaN).Min();
        var original = primary.Select((g, i) => (g, i)).GroupBy(p => p.g.SourceLine).OrderBy(g => g.Key);
        var previous = baseline.Glyphs.Select((g, i) => (g, i)).GroupBy(p => p.g.SourceLine).ToDictionary(g => g.Key!.Value, g => g.ToArray());
        var frame = new OcrInkCropFrame(raster, null, token);
        foreach (var group in original)
        {
            token.ThrowIfCancellationRequested(); var line = group.Key!.Value; var crop = crops[line];
            var native = group.OrderBy(p => p.g.SourceOrder).ToArray();
            var text = string.Concat(native.Select(p => p.g.Text));
            var header = double.IsFinite(right) && double.IsFinite(top) && crop.X + crop.Width < right && crop.Y >= top && text.All(c => c <= 127);
            var start = Array.FindLastIndex(native, p => p.g.Text.Any(c => c > 127 && !(policy.Symbols && c == '×'))) + 1;
            // An already observed ASCII colon is also a physical role separator;
            // this supplies no missing role or expected body text.
            var role = native.Length >= 4 && string.Concat(native.Take(2).Select(p => p.g.Text)) is "科目" or "担当" or "教室" && native[2].g.Text is ":" or "：";
            if (policy.DiscoverLetters && role) start = Math.Max(start, 3);
            var code = string.Concat(native.Skip(start).Select(p => p.g.Text));
            var hasDigit = code.Any(char.IsAsciiDigit);
            var eligible = !header && (!double.IsFinite(top) || crop.Y >= top) && code.Length is >= 2 and <= 32 &&
                code.Any(char.IsAsciiLetter) && (hasDigit || policy.DiscoverLetters) &&
                code.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' || policy.Symbols && c == '×') &&
                !ClassSelection.Candidates.Contains(code) && (start == 0 || start < native.Length);
            RecoveryBox? source = null;
            if (eligible)
                source = start == 0 ? crop : OcrWhiteGapPartition.Tail(raster, crop,
                    native.Take(start).Max(p => p.g.X + p.g.Width), native.Skip(start).Min(p => p.g.X), token);
            OnnxAsciiOcr.Observation? chosen = null;
            if (source is not null)
            {
                calls++; var expert = read(3, frame.Restrict(source), line);
                var valid = expert.Pieces.Count > 0 && expert.PaddingSupported && expert.RejectProbability <= .05f &&
                    expert.Text.Length is >= 2 and <= 32 && expert.Text.Any(char.IsAsciiDigit) &&
                    expert.Text.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') &&
                    (policy.RecoverLength || expert.Text.Length == code.Length);
                var extension = !hasDigit || code.Contains('×') || expert.Text.Length != code.Length;
                if (valid && policy.RequireAgreement && extension)
                {
                    calls++; var witness = read(1, source, line);
                    valid = witness.PaddingSupported && witness.RejectProbability <= .05f && witness.Text == expert.Text;
                }
                if (valid) chosen = expert;
            }
            if (chosen is null)
                foreach (var p in previous[line]) Add(p.g, baseline.Confidences[p.i]);
            else
            {
                foreach (var p in native.Take(start)) Add(p.g, scores[p.i]);
                for (var i = 0; i < chosen.Glyphs.Count; i++) Add(chosen.Glyphs[i], chosen.Pieces[i].Confidence);
            }
        }
        return new(result, confidence, calls);
        void Add(PdfGlyph glyph, double score)
        {
            result.Add(glyph with { SourceOrder = result.Count }); confidence.Add(score);
        }
    }
}
