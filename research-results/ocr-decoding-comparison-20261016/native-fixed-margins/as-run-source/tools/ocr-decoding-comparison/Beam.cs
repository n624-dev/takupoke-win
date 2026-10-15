using Microsoft.ML.OnnxRuntime.Tensors;
using Takupoke.Infrastructure.Recovery;

// Research: fixed beam8/frame8, no vocabulary or language/pattern prior.
// The selected sequence is aligned back to original frame probabilities.
internal static class StudyCtcBeam
{
    private const double Minus = double.NegativeInfinity;
    private sealed class State(int[] ids)
    {
        public int[] Ids { get; } = ids;
        public double Blank = Minus, Nonblank = Minus;
        public double Total => Add(Blank, Nonblank);
    }
    internal static OcrRecognitionPiece[] Decode(Tensor<float> p, string[] dictionary, CancellationToken token)
    {
        var frames = p.Dimensions[1];
        if (frames > 1024) throw new InvalidDataException("Bounded CTC work exceeded.");
        var beam = new[] { new State([]) { Blank = 0 } };
        for (var t = 0; t < frames; t++)
        {
            token.ThrowIfCancellationRequested();
            var frame = t;
            var shortlist = Enumerable.Range(1, dictionary.Length - 1)
                .OrderByDescending(c => p[0, frame, c]).ThenBy(c => c).Take(8).ToArray();
            var next = new Dictionary<string, State>(StringComparer.Ordinal);
            State Get(int[] ids)
            {
                var key = string.Join(',', ids);
                if (!next.TryGetValue(key, out var state)) next.Add(key, state = new(ids));
                return state;
            }
            foreach (var before in beam)
            {
                var same = Get(before.Ids);
                same.Blank = Add(same.Blank, before.Total + Log(p[0, t, 0]));
                var last = before.Ids.Length > 0 ? before.Ids[^1] : -1;
                foreach (var c in shortlist.Append(last).Where(c => c > 0).Distinct())
                {
                    var score = Log(p[0, t, c]);
                    if (c == last) same.Nonblank = Add(same.Nonblank, before.Nonblank + score);
                    if (before.Ids.Length >= 64) continue;
                    var extended = Get([.. before.Ids, c]);
                    extended.Nonblank = Add(extended.Nonblank, (c == last ? before.Blank : before.Total) + score);
                }
            }
            beam = next.Values.OrderByDescending(s => s.Total)
                .ThenBy(s => string.Join(',', s.Ids), StringComparer.Ordinal).Take(8).ToArray();
        }
        return Align(p, beam[0].Ids, dictionary, token);
    }

    private static OcrRecognitionPiece[] Align(Tensor<float> p, int[] ids, string[] dictionary, CancellationToken token)
    {
        if (ids.Length == 0) return [];
        var frames = p.Dimensions[1]; var states = 2 * ids.Length + 1;
        var previous = Enumerable.Repeat(Minus, states).ToArray();
        var back = new short[frames, states];
        previous[0] = Log(p[0, 0, 0]); previous[1] = Log(p[0, 0, ids[0]]);
        int Label(int s) => s % 2 == 0 ? 0 : ids[s / 2];
        for (var t = 1; t < frames; t++)
        {
            token.ThrowIfCancellationRequested(); var next = Enumerable.Repeat(Minus, states).ToArray();
            for (var s = 0; s < states; s++)
            {
                var parent = s; var best = previous[s];
                if (s > 0 && previous[s - 1] > best) { best = previous[s - 1]; parent = s - 1; }
                if (s > 1 && s % 2 == 1 && Label(s) != Label(s - 2) && previous[s - 2] > best)
                { best = previous[s - 2]; parent = s - 2; }
                next[s] = best + Log(p[0, t, Label(s)]); back[t, s] = (short)parent;
            }
            previous = next;
        }
        var state = previous[^1] > previous[^2] ? states - 1 : states - 2;
        if (!double.IsFinite(previous[state])) throw new InvalidDataException("No supported CTC alignment.");
        var start = Enumerable.Repeat(int.MaxValue, ids.Length).ToArray(); var end = new int[ids.Length]; var confidence = new float[ids.Length];
        for (var t = frames - 1; t >= 0; t--)
        {
            if (state % 2 == 1)
            {
                var n = state / 2; start[n] = Math.Min(start[n], t); end[n] = Math.Max(end[n], t + 1);
                confidence[n] = Math.Max(confidence[n], p[0, t, ids[n]]);
            }
            state = back[t, state];
        }
        if (start.Any(s => s == int.MaxValue)) throw new InvalidDataException("Incomplete CTC alignment.");
        return ids.Select((id, n) => new OcrRecognitionPiece(dictionary[id], start[n], end[n], confidence[n])).ToArray();
    }
    private static double Log(float x) => x > 0 ? Math.Log(x) : Minus;
    private static double Add(double a, double b)
    {
        if (double.IsNegativeInfinity(a)) return b;
        if (double.IsNegativeInfinity(b)) return a;
        var high = Math.Max(a, b); return high + Math.Log(1 + Math.Exp(Math.Min(a, b) - high));
    }
}
