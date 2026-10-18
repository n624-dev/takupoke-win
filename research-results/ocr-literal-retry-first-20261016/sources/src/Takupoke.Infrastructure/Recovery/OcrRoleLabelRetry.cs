using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;

namespace Takupoke.Infrastructure.Recovery;

// A second, bounded recognition of an ALREADY observed inline role label.
// This never supplies missing labels or changes a body value. Its literal,
// confidence and geometry must all come from the new native inference.
internal static class OcrRoleLabelRetry
{
    public static bool Eligible(IReadOnlyList<OcrRecognitionPiece> pieces)
        => pieces.Count >= 4 && pieces[2].Confidence < .8f &&
            pieces[2].Text is ":" or "：" &&
            string.Concat(pieces.Take(2).Select(p => p.Text)) is "科目" or "担当" or "教室" &&
            pieces[0].Text.Length == 1 && pieces[1].Text.Length == 1;

    public static RecoveryBox? PrefixCrop(RecoveryRaster raster, RecoveryBox original,
        IReadOnlyList<PdfGlyph> glyphs, CancellationToken token = default)
    {
        if (glyphs.Count < 4) return null;
        // CTC peaks locate hypotheses, not glyph outlines. An all-white band
        // between peak CENTRES can lie before the end of a peak's context.
        // A new inference supplies its own positions; old intervals are not cut.
        var tail = OcrWhiteGapPartition.Tail(raster, original, glyphs[2].Cx, glyphs[3].Cx, token);
        return tail is null ? null : new(original.X, original.Y, tail.X - original.X, original.Height);
    }

    public static bool Matches(IReadOnlyList<OcrRecognitionPiece> original, IReadOnlyList<OcrRecognitionPiece> retry)
        => original.Count >= 3 && retry.Count == 3 &&
            retry[0].Text == original[0].Text && retry[1].Text == original[1].Text &&
            retry[2].Text is ":" or "：" && retry.All(p => p.Confidence >= .8f);
}
