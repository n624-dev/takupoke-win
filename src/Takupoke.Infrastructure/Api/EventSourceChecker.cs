using System.Net;

namespace Takupoke.Infrastructure.Api;

public enum EventSourceState { NotApplicable, MissingETag, Matches, Changed, Unavailable }
public sealed class EventSourceChecker(HttpClient http, Uri? sourceUri = null)
{
    private readonly Uri _sourceUri = sourceUri ?? new("https://www.kagawa-nct.ac.jp/school_affairs/event/calendar.pdf");
    // The iOS baseline checks this published source against the saved 2026 API dataset.
    public const int SourceSchoolYear = 2026;
    public async Task<EventSourceState> CheckAsync(SavedEvents? saved, CancellationToken token = default)
    {
        if (saved is null || saved.Payload.SchoolYear != SourceSchoolYear) return EventSourceState.NotApplicable;
        if (saved.Payload.SourcePdfETag is not { } expected) return EventSourceState.MissingETag;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, _sourceUri);
            request.Headers.CacheControl = new() { NoCache = true, NoStore = true };
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode != HttpStatusCode.OK || !response.Headers.TryGetValues("ETag", out var values)) return EventSourceState.Unavailable;
            var tags = values.ToArray();
            if (tags.Length != 1 || !ApiPayloads.SourceETag(tags[0])) return EventSourceState.Unavailable;
            return tags[0] == expected ? EventSourceState.Matches : EventSourceState.Changed;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { return EventSourceState.Unavailable; }
    }
    public static string? Message(EventSourceState state) => state switch
    {
        EventSourceState.Changed => "学校サイトの学校行事PDFが更新された可能性があります。学校行事を取得し直して、配信データの更新を確認してください。保存済みの学校行事は表示しています。",
        EventSourceState.Unavailable => "学校サイトの学校行事PDFを確認できませんでした。保存済みの学校行事は表示しています。",
        EventSourceState.MissingETag => "元資料の更新確認に必要な情報がありません。学校行事を取得し直してください。",
        _ => null
    };
}
