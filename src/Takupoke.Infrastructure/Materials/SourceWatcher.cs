namespace Takupoke.Infrastructure.Materials;

/// <summary>Watches only the explicitly registered filenames. Notifications trigger the same serialized refresh as periodic checks.</summary>
public sealed class SourceWatcher : IDisposable
{
    private readonly object _gate = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private Timer? _debounce;
    private bool _disposed;
    public event Action? Changed;
    public void Replace(IEnumerable<string> paths)
    {
        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SourceWatcher));
            var selected = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            // Empty registration is also used for lock, expiry and user cancel.
            if (selected.Length == 0) { _debounce?.Dispose(); _debounce = null; }
            // A saved-data reload re-registers the same sources. Keep an already
            // received change until it reaches the serialized refresh queue.
            // This also lets unavailable folders be retried on registration.
            ClearWatchers();
            foreach (var path in selected)
            {
                FileSystemWatcher? watcher = null;
                try
                {
                    var parent = Path.GetDirectoryName(path); var name = Path.GetFileName(path);
                    if (parent is null || !Directory.Exists(parent) || name.Length == 0) continue;
                    watcher = new FileSystemWatcher(parent, name)
                    { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite };
                    watcher.Changed += OnChanged; watcher.Created += OnChanged; watcher.Deleted += OnChanged; watcher.Renamed += OnChanged;
                    watcher.Error += (_, _) => Signal(); watcher.EnableRaisingEvents = true;
                    _watchers.Add(watcher);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    // Permissions, removable drives, and folder deletion must not block other files
                    // or the saved analysis. Periodic and manual reads still report source failures.
                    watcher?.Dispose();
                }
            }
        }
    }
    private void OnChanged(object sender, FileSystemEventArgs args) => Signal();
    private void Signal()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _debounce ??= new Timer(_ => { Action? action; lock (_gate) action = _disposed ? null : Changed; action?.Invoke(); });
            _debounce.Change(TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
        }
    }
    private void ClearWatchers()
    { foreach (var watcher in _watchers) watcher.Dispose(); _watchers.Clear(); }
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true; _debounce?.Dispose(); _debounce = null; ClearWatchers();
        }
    }
}
