namespace Takupoke.Infrastructure.Materials;

/// <summary>Watches only the explicitly registered filenames. Notifications trigger the same serialized refresh as periodic checks.</summary>
public sealed class SourceWatcher : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private Timer? _debounce;
    private bool _disposed;
    public event Action? Changed;
    public void Replace(IEnumerable<string> paths)
    {
        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SourceWatcher));
            var selected = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (selected.Count == 0) { Clear(); return; }
            // Keep live registrations and their pending debounce. Retry only
            // paths that never registered or whose watcher reported an error.
            foreach (var removed in _watchers.Keys.Where(path => !selected.Contains(path)).ToArray())
            { _watchers[removed].Dispose(); _watchers.Remove(removed); }
            foreach (var path in selected.Where(path => !_watchers.ContainsKey(path)))
            {
                FileSystemWatcher? watcher = null;
                try
                {
                    var parent = Path.GetDirectoryName(path); var name = Path.GetFileName(path);
                    if (parent is null || !Directory.Exists(parent) || name.Length == 0) continue;
                    watcher = new FileSystemWatcher(parent, name)
                    { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite };
                    watcher.Changed += OnChanged; watcher.Created += OnChanged; watcher.Deleted += OnChanged; watcher.Renamed += OnChanged;
                    watcher.Error += (sender, _) =>
                    {
                        lock (_gate)
                        {
                            if (_disposed || !_watchers.TryGetValue(path, out var owner) || !ReferenceEquals(owner, sender)) return;
                            _watchers.Remove(path); owner.Dispose(); Signal();
                        }
                    };
                    _watchers.Add(path, watcher);
                    watcher.EnableRaisingEvents = true;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    // Permissions, removable drives, and folder deletion must not block other files
                    // or the saved analysis. Periodic and manual reads still report source failures.
                    _watchers.Remove(path); watcher?.Dispose();
                }
            }
        }
    }
    private void OnChanged(object sender, FileSystemEventArgs args)
    {
        lock (_gate) { if (!_disposed && _watchers.Values.Any(owner => ReferenceEquals(owner, sender))) Signal(); }
    }
    private void Signal()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _debounce ??= new Timer(_ => { Action? action; lock (_gate) action = _disposed ? null : Changed; action?.Invoke(); });
            _debounce.Change(TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
        }
    }
    private void Clear()
    { _debounce?.Dispose(); _debounce = null; foreach (var watcher in _watchers.Values) watcher.Dispose(); _watchers.Clear(); }
    public void Dispose() { lock (_gate) { _disposed = true; Clear(); } }
}
