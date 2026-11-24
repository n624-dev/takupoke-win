using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Takupoke.Infrastructure.Authentication;

namespace Takupoke.Infrastructure.Api;

public enum PublicService { Api, Authentication }
public enum FunctionalState { Unknown, Available, Unavailable }

public sealed record FunctionalFeature([property: JsonRequired] string Id, [property: JsonRequired] string State,
    [property: JsonRequired] DateTimeOffset CheckedAt, [property: JsonRequired] DateTimeOffset ValidUntil,
    DateTimeOffset? LastSuccessfulAt)
{
    public FunctionalState Current(DateTimeOffset now) =>
        now < CheckedAt || now >= ValidUntil ? FunctionalState.Unknown : State switch
        {
            "ok" => FunctionalState.Available,
            "unavailable" => FunctionalState.Unavailable,
            _ => FunctionalState.Unknown
        };
}

public sealed record PublicEventVersion(int SchoolYear, string Format, string DataVersion,
    string SourceState, DateTimeOffset? SourceCheckedAt, string ReviewState, DateTimeOffset? ReviewCheckedAt);

public sealed record PublicEventEvidence([property: JsonRequired] DateTimeOffset CheckedAt,
    [property: JsonRequired] DateTimeOffset ValidUntil, [property: JsonRequired] IReadOnlyList<PublicEventVersion> Versions)
{
    public PublicEventVersion? Find(int year, string format, DateTimeOffset now)
    {
        if (now < CheckedAt || now >= ValidUntil) return null;
        var matches = Versions.Where(version => version.SchoolYear == year && version.Format == format).Take(2).ToArray();
        return matches.Length == 1 && EventSourceStatus.ValidDataVersion(matches[0].DataVersion) ? matches[0] : null;
    }
}

public sealed record FunctionalStatus(int SchemaVersion, string Service, [property: JsonRequired] DateTimeOffset CheckedAt,
    [property: JsonRequired] DateTimeOffset ValidUntil, IReadOnlyList<FunctionalFeature> Features, FunctionalStatus.DataEvidence? Data)
{
    public sealed record DataEvidence(PublicEventEvidence? Events);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public FunctionalFeature? Find(string id)
    {
        var matches = Features.Where(feature => feature.Id == id).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    public static FunctionalStatus Decode(byte[] bytes, PublicService service)
    {
        var value = JsonSerializer.Deserialize<FunctionalStatus>(bytes, Json);
        if (value is null || value.SchemaVersion != 1 || value.CheckedAt >= value.ValidUntil
            || value.Service != (service == PublicService.Api ? "takupoke-api" : "takunin")
            || value.Features is not { Count: <= 32 } || value.Features.Any(feature => feature is null)
            || value.Data?.Events is { } events && (events.Versions is null || events.Versions.Any(version => version is null)))
            throw new ApiException(ApiFailure.InvalidResponse);
        return value;
    }
}

public sealed record PublicMonitorDay(DateOnly Date, string State);
public sealed record PublicMonitorUpdate(string Message, DateTimeOffset PublishedAt);
public sealed record PublicMonitorIncident(string Title, string State, DateTimeOffset? StartedAt,
    IReadOnlyList<PublicMonitorUpdate> Updates);
public sealed record PublicMonitor(string State, double? Availability, IReadOnlyList<PublicMonitorDay> History,
    IReadOnlyList<PublicMonitorIncident> Incidents);

public sealed class PublicMonitoring
{
    public static readonly Uri Page = new("https://status.n624.jp/");
    public readonly IReadOnlyDictionary<PublicService, PublicMonitor> Services;
    private PublicMonitoring(IReadOnlyDictionary<PublicService, PublicMonitor> services) => Services = services;
    private static readonly IReadOnlyDictionary<PublicService, string> Ids = new Dictionary<PublicService, string>
    {
        [PublicService.Api] = "9028864",
        [PublicService.Authentication] = "9028865"
    };

    private static JsonElement Member(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var member) ? member : default;
    private static string? Text(JsonElement value, string name) =>
        Member(value, name) is { ValueKind: JsonValueKind.String } text ? text.GetString() : null;
    private static IEnumerable<JsonElement> Items(JsonElement list) =>
        list.ValueKind == JsonValueKind.Array ? list.EnumerateArray() : Enumerable.Empty<JsonElement>();
    private static IEnumerable<JsonElement> References(JsonElement node, string name) =>
        Items(Member(Member(Member(node, "relationships"), name), "data"));
    private static DateTimeOffset? Timestamp(string? value) =>
        value is { Length: <= 40 } && DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var parsed) ? parsed : null;
    private static string ShortText(string? value, int maximum, string fallback) =>
        value is null ? fallback : value[..Math.Min(value.Length, maximum)];
    private static string State(string? value, bool historical = false) => value switch
    {
        "operational" or "degraded" or "downtime" or "maintenance" or "not_monitored" => value,
        "recovered" or "recovered_degraded" when historical => value,
        _ => "unknown"
    };

    public static PublicMonitoring Decode(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var page = Member(document.RootElement, "data");
        if (Text(page, "type") != "status_page" || Text(page, "id") != "267223")
            throw new ApiException(ApiFailure.InvalidResponse);
        var nodes = Items(Member(document.RootElement, "included"))
            .ToLookup(node => (Text(node, "type"), Text(node, "id")));
        JsonElement Unique(string type, string? id)
        {
            if (id is null) return default;
            var candidates = nodes[(type, id)].Take(2).ToArray();
            return candidates.Length == 1 ? candidates[0] : default;
        }
        var result = new Dictionary<PublicService, PublicMonitor>();
        foreach (var (service, id) in Ids)
        {
            var matches = References(page, "resources")
                .Count(reference => Text(reference, "id") == id && Text(reference, "type") == "status_page_resource");
            if (matches != 1) continue;
            var resource = Member(Unique("status_page_resource", id), "attributes");
            if (Text(resource, "resource_type") != "Monitor") continue;
            var available = Member(resource, "availability");
            double? availability = available.ValueKind == JsonValueKind.Number
                && available.TryGetDouble(out var rate) && rate is >= 0 and <= 1 ? rate : null;
            var days = new Dictionary<DateOnly, string>();
            foreach (var day in Items(Member(resource, "status_history")))
            {
                if (!DateOnly.TryParseExact(Text(day, "day"), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var date)) continue;
                days[date] = days.ContainsKey(date) ? "unknown" : State(Text(day, "status"), historical: true);
            }

            JsonElement[] Affected(JsonElement attributes) => Items(Member(attributes, "affected_resources"))
                .Where(item => Text(item, "status_page_resource_id") == id).Take(2).ToArray();
            var incidents = new List<PublicMonitorIncident>();
            foreach (var reportId in References(page, "status_reports").Where(x => Text(x, "type") == "status_report")
                .Select(x => Text(x, "id")).Distinct())
            {
                var node = Unique("status_report", reportId);
                var report = Member(node, "attributes");
                var affected = Affected(report);
                if (affected.Length == 0) continue;
                var updates = new List<PublicMonitorUpdate>();
                foreach (var updateId in References(node, "status_updates").Where(x => Text(x, "type") == "status_update")
                    .Select(x => Text(x, "id")).Distinct())
                {
                    var update = Member(Unique("status_update", updateId), "attributes");
                    if (Affected(update).Length == 0 || Timestamp(Text(update, "published_at")) is not { } published) continue;
                    updates.Add(new(ShortText(Text(update, "message"), 2000, ""), published));
                }
                incidents.Add(new(ShortText(Text(report, "title"), 300, "障害情報"),
                    affected.Length == 1 ? Text(affected[0], "status") ?? "unknown" : "unknown", Timestamp(Text(report, "starts_at")),
                    updates.OrderByDescending(value => value.PublishedAt).ToArray()));
            }
            result[service] = new(State(Text(resource, "status")), availability,
                days.OrderBy(day => day.Key).Select(day => new PublicMonitorDay(day.Key, day.Value)).ToArray(),
                incidents.OrderByDescending(incident => incident.StartedAt).ToArray());
        }
        return new(result);
    }
}

public sealed record PublicMonitoringResult(PublicMonitoring Snapshot, EntityTagHeaderValue? ETag, DateTimeOffset ReceivedAt);

public sealed class PublicStatusClient(HttpClient http, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private static readonly Uri Api = new("https://takupoke-api.n624.jp/status");
    private static readonly Uri Authentication = new(OidcClient.Issuer + "/status");

    public Task<FunctionalStatus> DownloadAsync(PublicService service, CancellationToken token) =>
        GetAsync(service == PublicService.Api ? Api : Authentication, null, 64 * 1024,
            (response, bytes) => FunctionalStatus.Decode(bytes, service), token);

    public Task<PublicMonitoringResult> DownloadMonitoringAsync(PublicMonitoringResult? cached, CancellationToken token) =>
        GetAsync(new Uri(PublicMonitoring.Page, "index.json"), cached?.ETag, 2 * 1024 * 1024, (response, bytes) =>
        {
            var etag = response.Headers.ETag;
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                if (cached?.ETag is null || bytes.Length != 0 || etag is not null && etag.Tag != cached.ETag.Tag)
                    throw new ApiException(ApiFailure.InvalidResponse);
                return cached with { ReceivedAt = _clock.GetUtcNow() };
            }
            return new PublicMonitoringResult(PublicMonitoring.Decode(bytes), etag, _clock.GetUtcNow());
        }, token);

    private async Task<T> GetAsync<T>(Uri uri, EntityTagHeaderValue? etag, int maximum,
        Func<HttpResponseMessage, byte[], T> read, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new("application/json"));
        if (etag is not null) request.Headers.IfNoneMatch.Add(etag);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        if (response.StatusCode == HttpStatusCode.NotModified && etag is not null)
            return read(response, await ApiClient.ReadBoundedAsync(response, 0, deadline.Token));
        if (response.StatusCode != HttpStatusCode.OK
            || response.Content.Headers.ContentType?.MediaType is not ("application/json" or "application/vnd.api+json"))
            throw new ApiException(ApiFailure.InvalidResponse);
        return read(response, await ApiClient.ReadBoundedAsync(response, maximum, deadline.Token));
    }
}
