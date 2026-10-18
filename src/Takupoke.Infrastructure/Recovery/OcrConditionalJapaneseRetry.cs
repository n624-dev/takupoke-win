namespace Takupoke.Infrastructure.Recovery;

// Conditional recognition, never a character substitution table. Both texts
// and every selected score/interval must come from their actual model calls.
internal static class OcrConditionalJapaneseRetry
{
    internal static bool Japanese(string value) => value.Length == 1 && value[0] is >= '\u3040' and <= '\u30ff' or >= '\u3400' and <= '\u9fff';

    internal static bool Eligible(IReadOnlyList<OcrRecognitionPiece> original)
        => original.Count >= 4 && original[2].Text is ":" or "：" &&
            string.Concat(original.Take(2).Select(p => p.Text)) is "科目" or "担当" or "教室" &&
            original.Skip(3).Any(p => p.Confidence < .8f && Japanese(p.Text));

    internal static IReadOnlyList<int> Replacements(IReadOnlyList<OcrRecognitionPiece> original,
        IReadOnlyList<OcrRecognitionPiece> alternate, bool paddingSupported)
    {
        if (!paddingSupported || !Eligible(original) || original.Count != alternate.Count) return [];
        var changed = new List<int>();
        for (var n = 0; n < original.Count; n++)
        {
            var old = original[n]; var next = alternate[n];
            if (old.Text == next.Text || n == 2 && old.Text is ":" or "：" && next.Text is ":" or "：") continue;
            if (n < 3 || old.Confidence >= .8f || !float.IsFinite(old.Confidence) || !float.IsFinite(next.Confidence) ||
                next.Confidence < .8f || next.Confidence > 1 || !Japanese(old.Text) || !Japanese(next.Text)) return [];
            changed.Add(n);
        }
        return changed;
    }
}
