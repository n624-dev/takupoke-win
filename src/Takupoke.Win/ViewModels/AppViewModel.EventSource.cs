using Takupoke.Infrastructure.Api;

namespace Takupoke.Win.ViewModels;

public sealed partial class AppViewModel
{
    private readonly Func<TimeSpan, CancellationToken, Task> _sourceDelay;
    private bool _eventSourceChecksActive = true;
    private readonly Dictionary<int, SourceCheck> _sourceFollowUps = [];
    private readonly Dictionary<int, (EventSourceTarget Target, int Remaining)> _sourceBudgets = [];

    public void SetEventSourceChecksActive(bool active)
    {
        _eventSourceChecksActive = active;
        if (!active) CancelEventSourceChecks();
        else foreach (var year in EventRecords.Keys.ToArray()) _ = CheckEventSourceAsync(year);
    }

    private void CancelEventSourceChecks()
    {
        foreach (var waiting in _sourceFollowUps.Values.ToArray()) waiting.Cancellation.Cancel();
        _sourceFollowUps.Clear();
        foreach (var request in _sourceChecks.Values.ToArray()) request.Cancellation.Cancel();
    }

    private void PruneEventSourceChecks()
    {
        bool Current(int year, EventSourceTarget target) => EventRecords.TryGetValue(year, out var value) && EventSourceTarget.From(value) == target;
        foreach (var (year, budget) in _sourceBudgets.ToArray()) if (!Current(year, budget.Target)) _sourceBudgets.Remove(year);
        foreach (var (year, waiting) in _sourceFollowUps.ToArray()) if (!Current(year, waiting.Target))
        { _sourceFollowUps.Remove(year); waiting.Cancellation.Cancel(); }
        foreach (var (year, request) in _sourceChecks.ToArray()) if (!Current(year, request.Target)) request.Cancellation.Cancel();
    }

    public Task CheckEventSourceAsync(int year, bool force = false)
    {
        if (!_eventSourceChecksActive || _session.IsCancellationRequested || !EventRecords.TryGetValue(year, out var saved)) return Task.CompletedTask;
        var target = EventSourceTarget.From(saved);
        if (_sourceChecks.TryGetValue(year, out var running) && running.Target == target && !running.Cancellation.IsCancellationRequested) return running.Task;
        if (!force && _sourceBudgets.TryGetValue(year, out var budget) && budget.Target == target &&
            _sourceObservations.GetValueOrDefault(year) is { Failed: false, Saved.Response.Checking: true } observation && observation.Saved.Response.Matches(saved))
        {
            if (!_sourceFollowUps.ContainsKey(year)) ScheduleEventSourceFollowUp(year, target, budget.Remaining);
            return Task.CompletedTask;
        }
        return CheckEventSourceRequestAsync(year, force, 3);
    }

    private void ScheduleEventSourceFollowUp(int year, EventSourceTarget target, int remaining)
    {
        if (!_eventSourceChecksActive || remaining <= 0 || _session.IsCancellationRequested) return;
        if (_sourceFollowUps.Remove(year, out var prior)) prior.Cancellation.Cancel();
        var id = Guid.NewGuid(); var epoch = PrivateEpoch;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_session.Token);
        var token = cancellation.Token;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _sourceFollowUps[year] = new(id, target, cancellation, completion.Task);
        var now = _clock.GetUtcNow();
        var elapsed = now - (_sourceObservations.GetValueOrDefault(year)?.Saved?.ReceivedAt ?? now);
        var delay = elapsed < TimeSpan.Zero ? TimeSpan.FromSeconds(60) : TimeSpan.FromSeconds(Math.Max(0, 60 - elapsed.TotalSeconds));
        _ = Follow();
        async Task Follow()
        {
            try
            {
                await _sourceDelay(delay, token);
                if (token.IsCancellationRequested || !_eventSourceChecksActive || epoch != PrivateEpoch ||
                    !_sourceFollowUps.TryGetValue(year, out var current) || current.Id != id ||
                    !EventRecords.TryGetValue(year, out var saved) || EventSourceTarget.From(saved) != target) return;
                _sourceFollowUps.Remove(year);
                _sourceBudgets[year] = (target, remaining - 1);
                await CheckEventSourceRequestAsync(year, force: true, remaining - 1);
            }
            catch (OperationCanceledException) { }
            catch { /* A failed advisory timer cannot modify saved events. */ }
            finally
            {
                if (_sourceFollowUps.TryGetValue(year, out var current) && current.Id == id) _sourceFollowUps.Remove(year);
                cancellation.Dispose(); completion.TrySetResult();
            }
        }
    }

    private sealed record SourceCheck(Guid Id, EventSourceTarget Target, CancellationTokenSource Cancellation, Task Task);
    private readonly Dictionary<int, SourceCheck> _sourceChecks = [];
    private readonly Dictionary<int, EventSourceObservation> _sourceObservations = [];
    private readonly HashSet<int> _failedEventUpdateYears = [];
    public string? EventSourceMessage
    {
        get
        {
            var messages = EventRecords.OrderBy(p => p.Key).Select(p =>
                (Year: p.Key, Warning: _sourceObservations.GetValueOrDefault(p.Key)?.Warning(p.Value, _clock.GetUtcNow())))
                .Where(p => p.Warning is not null).Select(p => $"{p.Year}年度：{p.Warning}").ToArray();
            return messages.Length == 0 ? null : string.Join("\n", messages);
        }
    }
    public string EventSourceDescription(int year) => EventRecords.TryGetValue(year, out var events)
        ? (_sourceObservations.GetValueOrDefault(year) ?? new(null, false)).Description(events, _clock.GetUtcNow()) : "学校行事は未取得です。";
    public string? EventsUpdateMessage { get; private set; }
    /// Metadata does not own Busy or the private-operation queue. Saved events
    /// remain immediately usable while the API checks its official source.
    private Task CheckEventSourceRequestAsync(int year, bool force, int remaining)
    {
        if (!_eventSourceChecksActive || _session.IsCancellationRequested || !EventRecords.TryGetValue(year, out var saved)) return Task.CompletedTask;
        var target = EventSourceTarget.From(saved);
        if (_sourceChecks.TryGetValue(year, out var running) && running.Target == target && !running.Cancellation.IsCancellationRequested)
            return running.Task;
        _sourceFollowUps.Remove(year, out var waiting); waiting?.Cancellation.Cancel();
        running?.Cancellation.Cancel();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var id = Guid.NewGuid(); var epoch = PrivateEpoch;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_session.Token);
        _sourceChecks[year] = new(id, target, cancellation, completion.Task);
        _ = Check();
        return completion.Task;
        async Task Check()
        {
            var followUp = false;
            try
            {
                var result = await _eventSourceChecker.CheckAsync(saved, force, cancellation.Token);
                if (cancellation.IsCancellationRequested || epoch != PrivateEpoch ||
                    !_sourceChecks.TryGetValue(year, out var current) || current.Id != id ||
                    !EventRecords.TryGetValue(year, out var active) || EventSourceTarget.From(active) != target) return;
                var checking = !result.Failed && result.Saved?.Response.Checking == true && result.Saved.Response.Matches(active);
                if (checking) { _sourceBudgets[year] = (target, remaining); result = result with { FollowUpExhausted = remaining == 0 }; }
                else _sourceBudgets.Remove(year);
                if (_sourceObservations.GetValueOrDefault(year) != result)
                {
                    _sourceObservations[year] = result;
                    SnapshotChanged?.Invoke();
                }
                followUp = checking;
            }
            catch (OperationCanceledException) { }
            catch { /* Advisory failure cannot invalidate saved events. */ }
            finally
            {
                if (_sourceChecks.TryGetValue(year, out var current) && current.Id == id)
                {
                    _sourceChecks.Remove(year);
                    if (followUp) ScheduleEventSourceFollowUp(year, target, remaining);
                }
                cancellation.Dispose(); completion.TrySetResult();
            }
        }
    }

}
