using System.Net;
using System.Net.Http.Headers;
using Takupoke.Infrastructure.Storage;

namespace Takupoke.Infrastructure.Api;

public sealed class ApiClient(HttpClient http, Uri? baseUri = null, TimeProvider? timeProvider = null)
{
    private readonly Uri _baseUri = baseUri ?? new("https://takupoke-api.n624.jp/");
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    public static HttpClient CreateHttpClient() => new(new SocketsHttpHandler
    { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.None, ConnectTimeout = TimeSpan.FromSeconds(15) })
    { Timeout = TimeSpan.FromSeconds(90) };
    public static string RevisionPath(DataSet kind) => kind switch { DataSet.Links => "links-revision", DataSet.Mapping => "mapping-revision", _ => "timetable-times-revision" };
    private static string Header(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues(name, out var values)) throw new ApiException(ApiFailure.InvalidResponse);
        var all = values.ToArray();
        return all.Length == 1 ? all[0] : throw new ApiException(ApiFailure.InvalidResponse);
    }
    public static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, int maximum, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength is { } length && length > maximum) throw new ApiException(ApiFailure.InvalidResponse);
        await using var input = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream(); var buffer = new byte[32 * 1024];
        int read;
        while ((read = await input.ReadAsync(buffer, token)) > 0)
        { if (output.Length + read > maximum) throw new ApiException(ApiFailure.InvalidResponse); await output.WriteAsync(buffer.AsMemory(0, read), token); }
        return output.ToArray();
    }
    private HttpRequestMessage Request(string path, string? accessToken = null, string? accept = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseUri, path));
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        if (accessToken is not null)
        {
            if (string.IsNullOrEmpty(accessToken) || accessToken.Any(char.IsControl) || accessToken.Length > 16000) throw new ApiException(ApiFailure.Authentication);
            request.Headers.Authorization = new("Bearer", accessToken);
        }
        if (accept is not null) request.Headers.Accept.Add(new(accept));
        return request;
    }
    private static void Success(HttpResponseMessage response, string contentType)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new ApiException(ApiFailure.Authentication);
        if (response.StatusCode != HttpStatusCode.OK) throw new ApiException(ApiFailure.Unavailable);
        if (response.Content.Headers.ContentType?.MediaType?.Equals(contentType, StringComparison.OrdinalIgnoreCase) != true) throw new ApiException(ApiFailure.InvalidResponse);
    }
    private static async Task<T> Guard<T>(Func<Task<T>> action, CancellationToken token)
    {
        try { return await action(); }
        catch (ApiException) { throw; }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new ApiException(ApiFailure.Unavailable); }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException) { throw new ApiException(ApiFailure.Unavailable); }
        catch { throw new ApiException(ApiFailure.InvalidResponse); }
    }
    public Task<RevisionResult> CheckRevisionAsync(DataSet kind, string? installed, CancellationToken token = default) => Guard(async () =>
    {
        if (installed is not null && !ApiPayloads.ValidRevision(installed)) throw new ApiException(ApiFailure.InvalidResponse);
        using var request = Request(RevisionPath(kind));
        if (installed is not null) request.Headers.IfNoneMatch.Add(new('"' + installed + '"'));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if ((await ReadBoundedAsync(response, 0, token)).Length != 0) throw new ApiException(ApiFailure.InvalidResponse);
        if (response.StatusCode == HttpStatusCode.NotModified && installed is not null) return new RevisionResult(installed, false);
        if (response.StatusCode != HttpStatusCode.OK) throw new ApiException(ApiFailure.Unavailable);
        var etag = Header(response, "ETag");
        if (etag.Length != 45 || etag[0] != '"' || etag[^1] != '"' || !ApiPayloads.ValidRevision(etag[1..^1])) throw new ApiException(ApiFailure.InvalidResponse);
        return new RevisionResult(etag[1..^1], etag[1..^1] != installed);
    }, token);
    public Task<SavedLinks> DownloadLinksAsync(string accessToken, string expectedRevision, CancellationToken token = default) => Download(
        "links", "application/json", "X-Links-Revision", accessToken, expectedRevision, 3_000_000,
        (bytes, response) => new SavedLinks(ApiPayloads.Links(bytes), ValidETag(response), _clock.GetUtcNow(), expectedRevision), token);
    public Task<SavedTimes> DownloadTimesAsync(string accessToken, string expectedRevision, CancellationToken token = default) => Download(
        "timetable-times", "application/json", "X-Timetable-Times-Revision", accessToken, expectedRevision, 128 * 1024,
        (bytes, response) => new SavedTimes(expectedRevision, _clock.GetUtcNow(), ApiPayloads.Times(bytes)), token);
    public Task<SavedMapping> DownloadMappingAsync(string accessToken, string expectedRevision, CancellationToken token = default) => Download(
        "mappings/current", "application/zip", "X-Mapping-Revision", accessToken, expectedRevision, 8 * 1024 * 1024,
        (bytes, response) => ApiPayloads.Mapping(bytes, Header(response, "X-Mapping-Version"), expectedRevision, ValidETag(response), _clock.GetUtcNow(), token), token);
    private static string ValidETag(HttpResponseMessage response)
    { var tag = Header(response, "ETag"); return ApiPayloads.ValidETag(tag) ? tag : throw new ApiException(ApiFailure.InvalidResponse); }
    private Task<T> Download<T>(string path, string type, string revisionHeader, string accessToken, string expected,
        int maximum, Func<byte[], HttpResponseMessage, T> decode, CancellationToken token) => Guard(async () =>
    {
        if (!ApiPayloads.ValidRevision(expected)) throw new ApiException(ApiFailure.InvalidResponse);
        using var request = Request(path, accessToken, type);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        Success(response, type);
        if (Header(response, revisionHeader) != expected) throw new ApiException(ApiFailure.Changed);
        return decode(await ReadBoundedAsync(response, maximum, token), response);
    }, token);
    public Task<SavedEvents> DownloadEventsAsync(int schoolYear, SavedEvents? saved, CancellationToken token = default) => Guard(async () =>
    {
        if (schoolYear is < 1900 or > 9998) throw new ApiException(ApiFailure.InvalidResponse);
        if (saved is not null) saved.Payload.Validated(schoolYear);
        using var request = Request("events?schoolYear=" + schoolYear, accept: "application/json");
        if (saved?.ApiETag is { } sent)
        { if (!ApiPayloads.ValidETag(sent)) throw new ApiException(ApiFailure.InvalidResponse); request.Headers.IfNoneMatch.Add(new(sent)); }
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new ApiException(ApiFailure.UnsupportedYear);
        var etag = response.Headers.Contains("ETag") ? ValidETag(response) : null;
        var bytes = await ReadBoundedAsync(response, response.StatusCode == HttpStatusCode.NotModified ? 0 : 1_000_000, token);
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            if (saved?.ApiETag is not { } old || etag is null || ApiPayloads.OpaqueETag(old) != ApiPayloads.OpaqueETag(etag)) throw new ApiException(ApiFailure.InvalidResponse);
            return new SavedEvents(_clock.GetUtcNow(), saved.Payload, etag);
        }
        Success(response, "application/json");
        return new SavedEvents(_clock.GetUtcNow(), ApiPayloads.Events(bytes, schoolYear), etag);
    }, token);
}
