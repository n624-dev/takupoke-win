using System.IO.Compression;
using System.Net;
using System.Text;
using Takupoke.Core;
using Takupoke.Infrastructure.Api;
using Takupoke.Infrastructure.Storage;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class ApiClientTests
{
    private const string Revision = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Assert.Equal("example.invalid", request.RequestUri!.Host);
            return Task.FromResult(send(request));
        }
    }
    private static HttpResponseMessage Response(HttpStatusCode status, string body = "", string type = "application/json", params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, type) };
        foreach (var pair in headers) response.Headers.TryAddWithoutValidation(pair.Name, pair.Value);
        return response;
    }
    private static ApiClient Client(HttpClient http) => new(http, new("https://example.invalid/"));
    private sealed class WaitingHandler : HttpMessageHandler
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal("example.invalid", request.RequestUri!.Host);
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("The fake request must be cancelled.");
        }
    }
    private static Task Request(ApiClient client, string kind, CancellationToken token) => kind switch
    {
        "revision" => client.CheckRevisionAsync(DataSet.Links, null, token),
        "download" => client.DownloadLinksAsync("fake-token", Revision, token),
        _ => client.DownloadEventsAsync(2032, null, token)
    };
    [Theory]
    [InlineData("revision")]
    [InlineData("download")]
    [InlineData("events")]
    public async Task HttpTimeoutIsReportedAsCommunicationFailure(string kind)
    {
        using var http = new HttpClient(new WaitingHandler()) { Timeout = TimeSpan.FromMilliseconds(50) };
        var failure = await Assert.ThrowsAsync<ApiException>(() => Request(Client(http), kind, CancellationToken.None));
        Assert.Equal(ApiFailure.Unavailable, failure.Failure);
    }
    [Theory]
    [InlineData("revision")]
    [InlineData("download")]
    [InlineData("events")]
    public async Task CallerCanCancelAnActiveRequestWithoutReportingCommunicationFailure(string kind)
    {
        var handler = new WaitingHandler();
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var request = Request(Client(http), kind, cancellation.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
    }
    [Fact]
    public async Task PublicRevisionNeverSendsAuthenticationAndUsesInstalledRevision()
    {
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Null(request.Headers.Authorization);
            Assert.Equal('"' + Revision + '"', Assert.Single(request.Headers.IfNoneMatch).Tag);
            return Response(HttpStatusCode.NotModified);
        }));
        Assert.False((await Client(http).CheckRevisionAsync(DataSet.Links, Revision)).Changed);
    }
    [Theory]
    [InlineData("nonempty", "\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"")]
    [InlineData("", "\"invalid-revision\"")]
    public async Task RejectsRevisionBodyOrMalformedIdentifier(string body, string etag)
    {
        using var http = new HttpClient(new Handler(_ => Response(HttpStatusCode.OK, body, headers: [("ETag", etag)])));
        Assert.Equal(ApiFailure.InvalidResponse, (await Assert.ThrowsAsync<ApiException>(() => Client(http).CheckRevisionAsync(DataSet.Links, null))).Failure);
    }
    [Fact]
    public async Task ChangedDuringDownloadDoesNotProduceNewPayload()
    {
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            return Response(HttpStatusCode.OK, "{}", headers: [("X-Links-Revision", new string('B', 43))]);
        }));
        Assert.Equal(ApiFailure.Changed, (await Assert.ThrowsAsync<ApiException>(() => Client(http).DownloadLinksAsync("fake-token", Revision))).Failure);
    }
    [Fact]
    public async Task ValidLinksAreDecodedOnlyAfterHeadersAndContentTypeMatch()
    {
        var payload = new LinksPayload("v1", "sha256-" + new string('a', 64),
            [new("fake-category", "架空カテゴリ", 1, [new("fake-link", "fake-category", "架空リンク", "https://example.invalid/fake", "blue", true, 1, true, 1, ["かくう"], "かくう|fake")])]);
        using var http = new HttpClient(new Handler(_ => Response(HttpStatusCode.OK, Encoding.UTF8.GetString(DataCodec.Encode(payload)),
            headers: [("X-Links-Revision", Revision), ("ETag", "\"fake-links\"")])));
        Assert.Equal("fake-link", Assert.Single((await Client(http).DownloadLinksAsync("fake-token", Revision)).Payload.Items).Id);
    }
    [Fact]
    public async Task MatchingWeakEventsEtagReusesSavedPayload()
    {
        var payload = new EventsPayload("v1", 2032, new string('a', 64), "\"fake-source\"", [new("2032-04-05", "2032-04-05", "架空行事", "行事")]);
        var saved = new SavedEvents(DateTimeOffset.UtcNow, payload, "\"fake-events\"");
        using var http = new HttpClient(new Handler(_ => Response(HttpStatusCode.NotModified, headers: [("ETag", "W/\"fake-events\"")])));
        Assert.Same(payload, (await Client(http).DownloadEventsAsync(2032, saved)).Payload);
        await Assert.ThrowsAsync<ApiException>(() => Client(http).DownloadEventsAsync(2032, null));
    }
    [Fact]
    public async Task StreamingLimitAppliesWithoutContentLength()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(new byte[1025])) };
        await Assert.ThrowsAsync<ApiException>(() => ApiClient.ReadBoundedAsync(response, 1024, CancellationToken.None));
    }
    [Theory]
    [InlineData("digest")]
    [InlineData("extraFile")]
    [InlineData("extraProperty")]
    [InlineData("falseInternational")]
    [InlineData("duplicateContext")]
    public void MappingRejectsUnverifiedOrAmbiguousPackages(string mutation)
    {
        Assert.ThrowsAny<Exception>(() => ApiPayloads.Mapping(MappingZip(mutation), "fake-v1", Revision, "\"fake-archive\"", DateTimeOffset.UtcNow));
    }
    [Fact]
    public void MappingAcceptsVerifiedV2ContextWithoutExposingSourceCsv()
    {
        var saved = ApiPayloads.Mapping(MappingZip(), "fake-v1", Revision, "\"fake-archive\"", DateTimeOffset.UtcNow);
        Assert.Equal(2, saved.SchemaVersion);
        Assert.Equal("架空教員A", Assert.Single(saved.Rules.TeacherContexts!).FullName);
    }
    [Fact]
    public void ApiTimesRejectUnknownFieldsRatherThanIgnoringThem()
    {
        Assert.Throws<ApiException>(() => ApiPayloads.Times("{\"schemaVersion\":1,\"days\":[],\"extra\":true}"u8.ToArray()));
        Assert.Empty(ApiPayloads.Times("{\"schemaVersion\":1,\"days\":[]}"u8.ToArray()).Days);
    }
    internal static byte[] MappingZip(string? mutation = null)
    {
        const string context = "{\"alias\":\"教A\",\"fullName\":\"架空教員A\",\"subject\":\"架空正式科目A\",\"className\":\"1_CN\",\"schoolYear\":2032}";
        var json = "{\"subjects\":[{\"alias\":\"科A\",\"fullName\":\"架空正式科目A\"}],\"teachers\":[],\"rooms\":[],\"teacherContexts\":[" + context + "]}";
        if (mutation == "extraProperty") json = json.Replace("\"alias\":\"科A\"", "\"alias\":\"科A\",\"source\":\"fake\"");
        if (mutation == "falseInternational") json = json.Replace("\"alias\":\"科A\"", "\"alias\":\"科A\",\"internationalStudent\":false");
        if (mutation == "duplicateContext") json = json.Replace(context, context + "," + context);
        var bytes = Encoding.UTF8.GetBytes(json);
        var manifest = DataCodec.Encode(new { schemaVersion = 2, version = "fake-v1", publishedAt = "2032-01-02T03:04:05Z", mappings = new { bytes = bytes.Length, sha256 = mutation == "digest" ? new string('0', 64) : NotificationDiff.Digest(bytes) } });
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            using (var entry = archive.CreateEntry("manifest.json").Open()) entry.Write(manifest);
            using (var entry = archive.CreateEntry("mappings.json").Open()) entry.Write(bytes);
            if (mutation == "extraFile") { using var entry = archive.CreateEntry("fake.csv").Open(); entry.Write("fake"u8); }
        }
        return output.ToArray();
    }
}
