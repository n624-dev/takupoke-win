using System.Net;
using Takupoke.Infrastructure.Api;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class EventSourceCheckerTests
{
    private sealed class Handler(HttpStatusCode status, string? etag) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++; Assert.Equal("example.invalid", request.RequestUri!.Host); Assert.Equal(HttpMethod.Head, request.Method);
            Assert.Null(request.Headers.Authorization); Assert.Null(request.Headers.IfNoneMatch.FirstOrDefault());
            var response = new HttpResponseMessage(status);
            if (etag is not null) response.Headers.TryAddWithoutValidation("ETag", etag);
            return Task.FromResult(response);
        }
    }
    private static SavedEvents Saved(int year, string? tag) => new(DateTimeOffset.UtcNow, new("v1", year, new('a', 64), tag, []), null);
    [Theory]
    [InlineData(200, "\"fake-original\"", EventSourceState.Matches)]
    [InlineData(200, "\"fake-new\"", EventSourceState.Changed)]
    [InlineData(200, null, EventSourceState.Unavailable)]
    [InlineData(404, "\"fake-original\"", EventSourceState.Unavailable)]
    [InlineData(302, "\"fake-original\"", EventSourceState.Unavailable)]
    public async Task ChecksOnlyHeadAndReportsDifferenceWithoutReplacingSavedEvents(int status, string? etag, EventSourceState expected)
    {
        var handler = new Handler((HttpStatusCode)status, etag); using var http = new HttpClient(handler);
        var saved = Saved(2026, "\"fake-original\"");
        Assert.Equal(expected, await new EventSourceChecker(http, new("https://example.invalid/events.pdf")).CheckAsync(saved));
        Assert.Equal("\"fake-original\"", saved.Payload.SourcePdfETag); Assert.Equal(1, handler.Calls);
    }
    [Fact]
    public async Task UnavailableYearOrMissingSavedSourceTagDoesNotContactSchoolSite()
    {
        var handler = new Handler(HttpStatusCode.OK, null); using var http = new HttpClient(handler);
        var checker = new EventSourceChecker(http, new("https://example.invalid/events.pdf"));
        Assert.Equal(EventSourceState.NotApplicable, await checker.CheckAsync(null));
        Assert.Equal(EventSourceState.NotApplicable, await checker.CheckAsync(Saved(2032, "\"fake\"")));
        Assert.Equal(EventSourceState.MissingETag, await checker.CheckAsync(Saved(2026, null)));
        Assert.Equal(0, handler.Calls);
    }
}
