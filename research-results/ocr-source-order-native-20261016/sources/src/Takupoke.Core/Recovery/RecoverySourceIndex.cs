namespace Takupoke.Core.Recovery;

// Transient indexes: input collections are never assumed immutable between operations.
internal sealed class RecoveryWorkLimitException : Exception;
internal sealed class RecoveryWorkBudget(CancellationToken token)
{
    private long _remaining = 20_000_000;
    public string Concat(IEnumerable<string> values)
    {
        var text = new System.Text.StringBuilder();
        foreach (var value in values) { Step(value.Length); text.Append(value); }
        return text.ToString();
    }
    public void Step(long count = 1)
    {
        token.ThrowIfCancellationRequested();
        if (count < 0 || (_remaining -= count) < 0) throw new RecoveryWorkLimitException();
    }
}
internal sealed class RecoverySourceIndex
{
    public Dictionary<string, RecoverySource> ById { get; } = [];
    public Dictionary<string, int> Order { get; } = [];
    private readonly Dictionary<string, List<RecoverySource>> _cells = [];
    private readonly Dictionary<int, Node> _pages = [];
    private readonly RecoveryWorkBudget _work;
    private sealed record Node(double X, double Y, double RightEdge, double BottomEdge, RecoverySource[]? Sources, Node? Left = null, Node? Right = null);
    public RecoverySourceIndex(RecoveryDocument doc, RecoveryWorkBudget work, bool spatial = false)
    {
        _work = work;
        if (doc.Sources.Count > 100000 || doc.Cells.Count > 20000) throw new RecoveryWorkLimitException();
        var pages = new Dictionary<int, List<RecoverySource>>();
        var ordinal = 0;
        foreach (var source in doc.Sources)
        {
            work.Step(1L + source.Text.Length); ById.TryAdd(source.Id, source); Order.TryAdd(source.Id, ordinal++);
            if (!_cells.TryGetValue(source.CellId, out var cell)) _cells[source.CellId] = cell = [];
            cell.Add(source);
            if (spatial)
            {
                if (!pages.TryGetValue(source.Page, out var page)) pages[source.Page] = page = [];
                page.Add(source);
            }
        }
        foreach (var (page, sources) in pages) _pages[page] = Build(sources.ToArray(), 0, sources.Count);
    }
    public IReadOnlyList<RecoverySource> Cell(string id) => _cells.GetValueOrDefault(id) ?? (IReadOnlyList<RecoverySource>)Array.Empty<RecoverySource>();
    private Node Build(RecoverySource[] sources, int offset, int count)
    {
        _work.Step(count);
        var x = double.PositiveInfinity; var y = x; var right = double.NegativeInfinity; var bottom = right;
        for (var i = offset; i < offset + count; i++)
        {
            var box = sources[i].Box; x = Math.Min(x, box.X); y = Math.Min(y, box.Y);
            right = Math.Max(right, box.X + box.Width); bottom = Math.Max(bottom, box.Y + box.Height);
        }

        if (count <= 16) return new(x, y, right, bottom, sources.AsSpan(offset, count).ToArray());
        var horizontal = right - x >= bottom - y;
        // Select the middle in place. Every partition comparison shares the operation budget;
        // pathological equal/ordered input cannot run unbounded or hide cancellation in Array.Sort.
        double Center(RecoverySource source) => horizontal ? source.Box.X + source.Box.Width / 2 : source.Box.Y + source.Box.Height / 2;
        var middle = offset + count / 2; var lo = offset; var hi = offset + count - 1;
        while (lo < hi)
        {
            _work.Step();
            var first = Center(sources[lo]); var last = Center(sources[hi]); var mid = Center(sources[lo + (hi - lo) / 2]);
            var pivot = Math.Max(Math.Min(first, last), Math.Min(Math.Max(first, last), mid));
            var left = lo; var rightIndex = hi;
            while (left <= rightIndex)
            {
                while (left <= hi) { _work.Step(); if (Center(sources[left]) >= pivot) break; left++; }
                while (rightIndex >= lo) { _work.Step(); if (Center(sources[rightIndex]) <= pivot) break; rightIndex--; }
                if (left <= rightIndex) { (sources[left], sources[rightIndex]) = (sources[rightIndex], sources[left]); left++; rightIndex--; }
            }
            if (middle <= rightIndex) hi = rightIndex;
            else if (middle >= left) lo = left;
            else break;
        }
        var half = count / 2;
        return new(x, y, right, bottom, null, Build(sources, offset, half), Build(sources, offset + half, count - half));
    }
    internal static bool Overlaps(RecoveryBox a, RecoveryBox b) => Math.Min(a.X + a.Width, b.X + b.Width) > Math.Max(a.X, b.X)
        && Math.Min(a.Y + a.Height, b.Y + b.Height) > Math.Max(a.Y, b.Y);
    public IEnumerable<RecoverySource> Intersecting(int page, RecoveryBox box)
    {
        if (!_pages.TryGetValue(page, out var root)) yield break;
        var pending = new Stack<Node>(); pending.Push(root);
        while (pending.TryPop(out var node))
        {
            _work.Step(); if (Math.Min(node.RightEdge, box.X + box.Width) <= Math.Max(node.X, box.X) || Math.Min(node.BottomEdge, box.Y + box.Height) <= Math.Max(node.Y, box.Y)) continue;
            if (node.Sources is { } sources)
            {
                foreach (var source in sources) { _work.Step(); if (Overlaps(source.Box, box)) yield return source; }
            }
            else { pending.Push(node.Left!); pending.Push(node.Right!); }
        }
    }
}
