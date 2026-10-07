using Takupoke.Core.Recovery;

namespace Takupoke.Infrastructure.Recovery;

// Per-operation candidate index only. Exact containment remains authoritative;
// the result retains every occurrence in the caller's original array order.
internal sealed class RecoveryContainmentIndex<T>
{
    private const double BucketSize = 64;
    private readonly Dictionary<(double X, double Y), List<(int Order, T Value, RecoveryBox Box)>> _buckets = [];
    private readonly Action<long> _charge;

    internal RecoveryContainmentIndex(IReadOnlyList<T> values, Func<T, RecoveryBox> bounds, Action<long> charge)
    {
        _charge = charge;
        for (var order = 0; order < values.Count; order++)
        {
            charge(1);
            var value = values[order]; var box = bounds(value);
            if (!box.Valid) throw new InvalidDataException("原文の文字位置を確認できません。");
            var key = (Math.Floor(box.X / BucketSize), Math.Floor(box.Y / BucketSize));
            if (!_buckets.TryGetValue(key, out var bucket)) _buckets[key] = bucket = [];
            bucket.Add((order, value, box));
        }
    }

    internal T[] Contained(RecoveryBox box)
    {
        _charge(1);
        if (!box.Valid) return [];
        var firstX = Math.Floor(box.X / BucketSize); var lastX = Math.Floor((box.X + box.Width) / BucketSize);
        var firstY = Math.Floor(box.Y / BucketSize); var lastY = Math.Floor((box.Y + box.Height) / BucketSize);
        var selected = new List<(int Order, T Value)>();
        // Visit existing buckets, so extreme but finite coordinates cannot
        // create an unbounded integer/double range or stall on key + 1 == key.
        foreach (var (key, bucket) in _buckets)
        {
            _charge(1);
            if (key.X < firstX || key.X > lastX || key.Y < firstY || key.Y > lastY) continue;
            foreach (var item in bucket)
            {
                _charge(1);
                if (box.Contains(item.Box)) selected.Add((item.Order, item.Value));
            }
        }
        var count = selected.Count;
        _charge((long)count * (1 + (count > 1 ? (int)Math.Ceiling(Math.Log2(count)) : 0)));
        var result = selected.OrderBy(item => item.Order).Select(item => item.Value).ToArray();
        _charge(1);
        return result;
    }
}
