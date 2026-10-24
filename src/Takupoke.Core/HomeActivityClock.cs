namespace Takupoke.Core;

public sealed record HomeActivityInterval(string Id, DateTimeOffset Start, DateTimeOffset End);

/// The app already ticks for retention and status expiry. Home redraws only
/// when the Japanese school day or the set of active cards actually changes.
public sealed class HomeActivityClock
{
    private HomeActivityInterval[] _intervals = [];
    private DateOnly? _day;
    private HashSet<string> _active = [];
    private DateTimeOffset? _lastObserved;
    public DateTimeOffset? NextBoundary { get; private set; }

    public void Configure(DateTimeOffset now, IEnumerable<HomeActivityInterval> intervals)
    {
        _intervals = intervals.Where(i => i.Start < i.End).ToArray();
        Apply(now);
    }
    public void Clear()
    {
        _intervals = []; _active = []; _day = null; _lastObserved = null; NextBoundary = null;
    }
    public bool Tick(DateTimeOffset now)
    {
        if (_lastObserved is not { } previous || NextBoundary is not { } next) return false;
        _lastObserved = now;
        if (now >= previous && now < next) return false;
        var oldDay = _day;
        var oldActive = _active;
        Apply(now);
        return oldDay != _day || !oldActive.SetEquals(_active);
    }
    private void Apply(DateTimeOffset now)
    {
        var day = SchoolDate.InJapan(now);
        _day = day;
        _lastObserved = now;
        _active = _intervals.Where(i => i.Start <= now && now < i.End).Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        NextBoundary = new DateTimeOffset(day.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(9));
        foreach (var edge in _intervals.SelectMany(i => new[] { i.Start, i.End }))
            if (edge > now && edge < NextBoundary) NextBoundary = edge;
    }
}
