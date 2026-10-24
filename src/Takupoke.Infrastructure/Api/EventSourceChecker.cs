using Takupoke.Infrastructure.Storage;

namespace Takupoke.Infrastructure.Api;

public sealed record EventSourceStatus(string Version, int SchoolYear, string DataVersion,
    string AdoptedSourcePdfSha256, string Status, string? LastSuccessfulStatus,
    DateTimeOffset? LastSuccessAt, DateTimeOffset? LastAttemptAt, DateTimeOffset? NextAttemptAt,
    [property: System.Text.Json.Serialization.JsonRequired] bool Checking,
    [property: System.Text.Json.Serialization.JsonRequired] bool Stale)
{
    public static bool ValidDataVersion(string? value) => value is { Length: 71 } &&
        value.StartsWith("sha256-", StringComparison.Ordinal) && ApiPayloads.HexDigest(value[7..]);
    public EventSourceStatus Validated(int year)
    {
        if (Version != "v1" || SchoolYear != year || year is < 1900 or > 9998 || !ValidDataVersion(DataVersion)
            || AdoptedSourcePdfSha256 is null || !ApiPayloads.HexDigest(AdoptedSourcePdfSha256)
            || Status is not ("matched" or "source_differs" or "unavailable" or "unknown")
            || (LastSuccessfulStatus is null) != (LastSuccessAt is null)
            || LastSuccessfulStatus is not (null or "matched" or "source_differs")
            || Status is "matched" or "source_differs" && (LastSuccessfulStatus != Status || LastSuccessAt is null))
            throw new ApiException(ApiFailure.InvalidResponse);
        return this;
    }
    public bool Matches(SavedEvents saved) => SchoolYear == saved.Payload.SchoolYear &&
        AdoptedSourcePdfSha256 == saved.Payload.SourcePdfSha256 && DataVersion == saved.DataVersion;
}

public sealed record EventSourceTarget(int Year, string Hash, string? Version)
{
    public static EventSourceTarget From(SavedEvents saved) => new(saved.Payload.SchoolYear, saved.Payload.SourcePdfSha256, saved.DataVersion);
}

public sealed record SavedEventSourceStatus(DateTimeOffset ReceivedAt, EventSourceStatus Response,
    EventSourceTarget Target, EventSourceStatus? PreviousSuccess = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public EventSourceStatus? Successful => PreviousSuccess ?? (Response.LastSuccessAt is null ? null : Response);
    public bool IsFresh(DateTimeOffset now) => now >= ReceivedAt && now - ReceivedAt <
        (Response.Checking ? TimeSpan.FromSeconds(60) : TimeSpan.FromHours(12));
    public bool CanReuse(SavedEvents saved, DateTimeOffset now) => IsFresh(now) &&
        (Response.Matches(saved) || Target == EventSourceTarget.From(saved));
    public SavedEventSourceStatus Validated(int year)
    {
        Response.Validated(year);
        if (Target is null || Target.Year != year || !ApiPayloads.HexDigest(Target.Hash)
            || Target.Version is not null && !EventSourceStatus.ValidDataVersion(Target.Version)) throw new ApiException(ApiFailure.InvalidResponse);
        if (PreviousSuccess is { } prior)
        {
            prior.Validated(year);
            if (prior.LastSuccessAt is null || prior.AdoptedSourcePdfSha256 != Response.AdoptedSourcePdfSha256
                || prior.DataVersion != Response.DataVersion) throw new ApiException(ApiFailure.InvalidResponse);
        }
        return this;
    }
}

public sealed record EventSourceObservation(SavedEventSourceStatus? Saved, bool Failed)
{
    public string? Warning(SavedEvents events, DateTimeOffset now)
    {
        if (Saved is null) return Failed ? "元PDFの状態APIに接続できませんでした。保存済み行事は表示しています。" : "元PDFの更新状況は未確認です。";
        var response = Saved.Response;
        if (!response.Matches(events)) return "APIと保存済み行事の年度・元PDF・配信版が一致しません。学校行事を更新してください。";
        if (Failed) return "元PDFの状態APIを確認できませんでした。前回の成功結果と保存済み行事は保持しています。";
        if (response.Checking) return "APIが元PDFを確認中です。前回の確認結果を表示しています。";
        if (response.Status is "unavailable" or "unknown") return response.Status == "unknown" ? "元PDFの更新状況は未確認です。" : "APIで元PDFの更新状況を確認できませんでした。";
        if (Saved.PreviousSuccess is not null) return "APIから以前の結果が返りました。新しい成功結果を保持し、現在の更新状況は未確認としています。";
        if (response.Stale || !Saved.IsFresh(now) || response.LastSuccessAt is not { } success ||
            now < success || now - success >= TimeSpan.FromHours(24)) return "元PDFの確認結果は古くなっています。現在の更新状況は未確認です。";
        return response.Status == "source_differs" ? "元PDFの本文に差異があります。行事内容は確認待ちです。保存済み行事を表示しています。" : null;
    }
    public string Description(SavedEvents events, DateTimeOffset now)
    {
        var text = Warning(events, now) ?? "元PDFの本文は登録元と一致しています。";
        if (Saved?.Response.Matches(events) == true && Saved.Successful is { LastSuccessAt: { } at } success)
            text += $" 最終成功の結果：{(success.LastSuccessfulStatus == "matched" ? "登録元と一致" : "本文差異・内容確認待ち")}。最終成功確認：{at.ToOffset(TimeSpan.FromHours(9)):yyyy/MM/dd HH:mm:ss}（日本時間）。";
        return text;
    }
}

/// Only public API metadata is fetched. Cache failures never affect events.
public sealed class EventSourceChecker(ApiClient api, PublicEventsStore store, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<int, SavedEventSourceStatus> _saved = [];
    private readonly HashSet<int> _failed = [];
    public async Task<EventSourceObservation> CheckAsync(SavedEvents saved, bool force = false, CancellationToken token = default)
    {
        var year = saved.Payload.SchoolYear;
        await _gate.WaitAsync(token);
        try
        {
            _saved.TryGetValue(year, out var previous);
            try { previous ??= await store.LoadSourceStatusAsync(year, token); }
            catch (Exception error) when (error is not OperationCanceledException) { /* advisory cache only */ }
            if (!force && previous?.CanReuse(saved, _clock.GetUtcNow()) == true) return new(previous, _failed.Contains(year));
            try
            {
                var response = await api.DownloadEventSourceStatusAsync(year, token);
                var prior = previous?.Successful;
                if (prior is null || prior.SchoolYear != response.SchoolYear || prior.AdoptedSourcePdfSha256 != response.AdoptedSourcePdfSha256
                    || prior.DataVersion != response.DataVersion || response.LastSuccessAt >= prior.LastSuccessAt) prior = null;
                var result = new SavedEventSourceStatus(_clock.GetUtcNow(), response, EventSourceTarget.From(saved), prior);
                await store.SaveSourceStatusAsync(result, token);
                _saved[year] = result; _failed.Remove(year);
                return new(result, false);
            }
            catch (OperationCanceledException) { throw; }
            catch { _failed.Add(year); return new(previous, true); }
        }
        finally { _gate.Release(); }
    }
}
