using Takupoke.Core;

namespace Takupoke.Infrastructure.Api;

public enum DataSet { Links, Mapping, Times }
public enum ApiFailure { Unavailable, InvalidResponse, Authentication, Changed, UnsupportedYear, Storage, AuthenticationTimeout }
public sealed class ApiException(ApiFailure failure) : Exception(failure switch
{
    ApiFailure.Authentication => "学校アカウントの認証を完了できませんでした。",
    ApiFailure.AuthenticationTimeout => "ブラウザでの認証結果を受け取れないか、認証処理が時間切れになりました。もう一度取得を開始してください。",
    ApiFailure.Changed => "取得中にデータが更新されました。もう一度お試しください。",
    ApiFailure.UnsupportedYear => "この年度の学校行事はまだ公開されていません。",
    ApiFailure.InvalidResponse => "取得したデータの内容を確認できませんでした。保存済みの結果は保持しています。",
    ApiFailure.Storage => "データを保存できませんでした。保存済みの結果は保持しています。",
    _ => "データを取得できませんでした。通信状態を確認して再試行してください。"
}) { public ApiFailure Failure { get; } = failure; }
public sealed record RevisionResult(string Revision, bool Changed);
public sealed record SavedLinks(LinksPayload Payload, string ApiETag, DateTimeOffset CheckedAt, string Revision);
public sealed record SavedMapping(string Revision, string Version, int SchemaVersion, string ArchiveETag,
    string ArchiveSha256, string PublishedAt, DateTimeOffset FetchedAt, MappingRules Rules);
public sealed record SavedTimes(string Revision, DateTimeOffset FetchedAt, ScheduleTimes Data);
public sealed record EventRow(string StartDate, string EndDate, string Title, string Tag);
public sealed record EventsPayload(string Version, int SchoolYear, string SourcePdfSha256, string? SourcePdfETag, IReadOnlyList<EventRow> Events)
{
    public static IReadOnlySet<string> Tags { get; } = new HashSet<string>
    { "授業なし", "曜日振替", "補講日", "行事（授業なし）", "行事（授業あり）", "行事", "行事（時間割変更）", "テスト", "テスト返却", "行事メモ" };
    private static int? OverrideDay(string value) => value.Trim() switch
    { "月曜日授業" => 1, "火曜日授業" => 2, "水曜日授業" => 3, "木曜日授業" => 4, "金曜日授業" => 5, _ => null };
    public EventsPayload Validated(int requestedYear)
    {
        if (Version != "v1" || SchoolYear != requestedYear || SchoolYear is < 1900 or > 9998 || SourcePdfSha256 is null || !ApiPayloads.HexDigest(SourcePdfSha256)
            || SourcePdfETag is not null && !ApiPayloads.SourceETag(SourcePdfETag) || Events is null || Events.Count is < 1 or > 2000) throw new ApiException(ApiFailure.InvalidResponse);
        foreach (var row in Events)
            if (row is null || !SchoolDate.TryParse(row.StartDate, out var start) || !SchoolDate.TryParse(row.EndDate, out var end)
                || start < new DateOnly(SchoolYear, 4, 1) || start > end || end > new DateOnly(SchoolYear + 1, 3, 31)
                || string.IsNullOrWhiteSpace(row.Title) || row.Title.Length > 200 || !Tags.Contains(row.Tag)
                || row.Tag == "曜日振替" && OverrideDay(row.Title) is null) throw new ApiException(ApiFailure.InvalidResponse);
        return this;
    }
    public IReadOnlyList<SchoolEvent> Project() => Events.Select(row => new SchoolEvent(row.StartDate, row.Title, row.Tag,
        row.EndDate == row.StartDate ? null : row.EndDate, row.Tag switch
        { "授業なし" => EventClassification.NoClass, "曜日振替" => EventClassification.WeekdayOverride,
            "補講日" => EventClassification.Supplementary, "行事（授業なし）" => EventClassification.SchoolEventNoClass, _ => EventClassification.None },
        OverrideDay(row.Title))).ToArray();
}
public sealed record SavedEvents(DateTimeOffset FetchedAt, EventsPayload Payload, string? ApiETag);
