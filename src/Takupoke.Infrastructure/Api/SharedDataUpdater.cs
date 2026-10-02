using Takupoke.Infrastructure.Storage;

namespace Takupoke.Infrastructure.Api;

public sealed record UpdateResult(DataSet Kind, bool Updated, ApiFailure? Failure = null);
public sealed record RevisionCheckResult(
    IReadOnlyDictionary<DataSet, RevisionResult> Revisions,
    IReadOnlyDictionary<DataSet, ApiFailure> Failures)
{
    public static RevisionCheckResult Empty => new(new Dictionary<DataSet, RevisionResult>(), new Dictionary<DataSet, ApiFailure>());

    // A failed check must retain the last known update notice. A check of one
    // dataset must not clear failures or update notices belonging to another.
    public RevisionCheckResult Merge(RevisionCheckResult next)
    {
        var revisions = Revisions.ToDictionary();
        var failures = Failures.ToDictionary();
        foreach (var pair in next.Revisions) { revisions[pair.Key] = pair.Value; failures.Remove(pair.Key); }
        foreach (var pair in next.Failures) failures[pair.Key] = pair.Value;
        return new(revisions, failures);
    }
}

/// <summary>Public checks never authenticate. A user-requested update authenticates once; each dataset commits independently.</summary>
public sealed class SharedDataUpdater(ApiClient api, SchoolDataStore store)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async Task<IReadOnlyDictionary<DataSet, RevisionResult>> CheckAsync(CancellationToken token = default)
        => (await CheckDetailedAsync(token)).Revisions;
    public Task<RevisionCheckResult> CheckDetailedAsync(CancellationToken token = default)
        => CheckDetailedAsync(Enum.GetValues<DataSet>(), token);
    public Task<RevisionCheckResult> CheckDetailedAsync(DataSet kind, CancellationToken token = default)
        => CheckDetailedAsync([kind], token);
    private async Task<RevisionCheckResult> CheckDetailedAsync(IEnumerable<DataSet> kinds, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            var lease = await store.BeginAsync(token);
            var results = new Dictionary<DataSet, RevisionResult>();
            var failures = new Dictionary<DataSet, ApiFailure>();
            foreach (var kind in kinds)
            {
                var installed = await InstalledAsync(kind, lease, token);
                try { results[kind] = await api.CheckRevisionAsync(kind, installed, token); }
                catch (ApiException error) { failures[kind] = error.Failure; }
            }
            return new(results, failures);
        }
        finally { _gate.Release(); }
    }
    private async Task<string?> InstalledAsync(DataSet kind, SchoolLease lease, CancellationToken token) => kind switch
    {
        DataSet.Links => (await store.ReadAsync<SavedLinks>(lease, "api.links", token))?.Revision,
        DataSet.Mapping => (await store.ReadAsync<SavedMapping>(lease, "api.mapping", token))?.Revision,
        _ => (await store.ReadAsync<SavedTimes>(lease, "api.times", token))?.Revision
    };
    public async Task<IReadOnlyList<UpdateResult>> UpdateAsync(Func<CancellationToken, Task<string>> authenticate, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            var lease = await store.BeginAsync(token);
            var revisions = new Dictionary<DataSet, string>();
            var results = new List<UpdateResult>();
            foreach (var kind in Enum.GetValues<DataSet>())
            {
                try
                {
                    var revision = await api.CheckRevisionAsync(kind, await InstalledAsync(kind, lease, token), token);
                    if (revision.Changed) revisions[kind] = revision.Revision;
                    else results.Add(new(kind, false));
                }
                catch (ApiException error) { results.Add(new(kind, false, error.Failure)); }
            }
            if (revisions.Count == 0) return results;
            // The token is transient and is never passed to storage.
            var accessToken = await authenticate(token);
            foreach (var pair in revisions)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    switch (pair.Key)
                    {
                        case DataSet.Links: await store.WriteAsync(lease, "api.links", await api.DownloadLinksAsync(accessToken, pair.Value, token), token); break;
                        case DataSet.Mapping: await store.WriteAsync(lease, "api.mapping", await api.DownloadMappingAsync(accessToken, pair.Value, token), token); break;
                        case DataSet.Times: await store.WriteAsync(lease, "api.times", await api.DownloadTimesAsync(accessToken, pair.Value, token), token); break;
                    }
                    results.Add(new(pair.Key, true));
                }
                catch (OperationCanceledException) { throw; }
                catch (ApiException error) { results.Add(new(pair.Key, false, error.Failure)); }
                catch { results.Add(new(pair.Key, false, ApiFailure.Storage)); }
            }
            return results;
        }
        finally { _gate.Release(); }
    }
}
