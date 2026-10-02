using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Takupoke.Infrastructure.Api;
using Takupoke.Infrastructure.Storage;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class SharedDataUpdaterTests
{
    private const string Revision = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private sealed class Protector : IKeyProtector, IDisposable
    {
        private readonly EnvelopeCipher _cipher = new(RandomNumberGenerator.GetBytes(32));
        public byte[] Protect(byte[] key) => _cipher.Encrypt(key, "fake-key");
        public byte[] Unprotect(byte[] bytes) => _cipher.Decrypt(bytes, "fake-key");
        public void Dispose() => _cipher.Dispose();
    }
    private sealed class Handler : HttpMessageHandler
    {
        public int PublicRequests { get; private set; }
        public int AuthenticatedRequests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal("example.invalid", request.RequestUri!.Host);
            var path = request.RequestUri.AbsolutePath;
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") };
            if (path.EndsWith("-revision", StringComparison.Ordinal))
            {
                Assert.Null(request.Headers.Authorization); PublicRequests++;
                response.Headers.ETag = new('"' + Revision + '"');
            }
            else
            {
                Assert.Equal("fake-token", request.Headers.Authorization?.Parameter); AuthenticatedRequests++;
                if (path == "/links") response.StatusCode = HttpStatusCode.ServiceUnavailable;
                else if (path == "/timetable-times")
                {
                    response.Content = new StringContent("{\"schemaVersion\":1,\"days\":[]}", Encoding.UTF8, "application/json");
                    response.Headers.TryAddWithoutValidation("X-Timetable-Times-Revision", Revision);
                    response.Headers.ETag = new("\"fake-times\"");
                }
                else
                {
                    response.Content = new ByteArrayContent(ApiClientTests.MappingZip());
                    response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
                    response.Headers.TryAddWithoutValidation("X-Mapping-Version", "fake-v1");
                    response.Headers.TryAddWithoutValidation("X-Mapping-Revision", Revision);
                    response.Headers.ETag = new("\"fake-mapping\"");
                }
            }
            return Task.FromResult(response);
        }
    }
    [Fact]
    public async Task SingleAuthenticationAndFailureOfOneDatasetDoesNotRollBackOthers()
    {
        var root = Path.Combine(Path.GetTempPath(), "takupoke-update-tests-" + Guid.NewGuid().ToString("N"));
        using var protector = new Protector();
        try
        {
            await using var store = new SchoolDataStore(root, protector);
            var handler = new Handler(); using var http = new HttpClient(handler);
            var updater = new SharedDataUpdater(new(http, new("https://example.invalid/")), store);
            var authentications = 0;
            Assert.Equal(3, (await updater.CheckAsync()).Count);
            Assert.Equal(0, authentications);
            var result = await updater.UpdateAsync(_ => { authentications++; return Task.FromResult("fake-token"); });
            Assert.Equal(1, authentications);
            Assert.False(result.Single(r => r.Kind == DataSet.Links).Updated);
            Assert.True(result.Single(r => r.Kind == DataSet.Mapping).Updated);
            Assert.True(result.Single(r => r.Kind == DataSet.Times).Updated);
            var lease = await store.BeginAsync();
            Assert.NotNull(await store.ReadAsync<SavedMapping>(lease, "api.mapping"));
            Assert.NotNull(await store.ReadAsync<SavedTimes>(lease, "api.times"));
            Assert.Null(await store.ReadAsync<SavedLinks>(lease, "api.links"));
            Assert.Equal(3, handler.AuthenticatedRequests);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public async Task AuthenticationCancellationLeavesPreviouslySavedDataUntouched()
    {
        var root = Path.Combine(Path.GetTempPath(), "takupoke-update-tests-" + Guid.NewGuid().ToString("N"));
        using var protector = new Protector();
        try
        {
            await using var store = new SchoolDataStore(root, protector);
            var lease = await store.BeginAsync();
            var saved = new SavedTimes(Revision, DateTimeOffset.UtcNow, new(1, []));
            await store.WriteAsync(lease, "api.times", saved);
            var handler = new Handler(); using var http = new HttpClient(handler);
            var updater = new SharedDataUpdater(new(http, new("https://example.invalid/")), store);
            await Assert.ThrowsAsync<OperationCanceledException>(() => updater.UpdateAsync(_ => throw new OperationCanceledException()));
            Assert.Equal(saved.Revision, (await store.ReadAsync<SavedTimes>(lease, "api.times"))!.Revision);
            Assert.Equal(0, handler.AuthenticatedRequests);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public async Task CurrentRevisionsRequireNeitherAuthenticationNorDownloads()
    {
        var root = Path.Combine(Path.GetTempPath(), "takupoke-update-tests-" + Guid.NewGuid().ToString("N"));
        using var protector = new Protector();
        try
        {
            await using var store = new SchoolDataStore(root, protector); var lease = await store.BeginAsync();
            await store.WriteAsync(lease, "api.links", new SavedLinks(new("v1", "sha256-" + new string('a', 64), [new("fake-category", "架空カテゴリ", 0, [])]), "\"fake-links\"", DateTimeOffset.UtcNow, Revision));
            await store.WriteAsync(lease, "api.mapping", new SavedMapping(Revision, "fake-v1", 1, "\"fake-mapping\"", new string('a', 64), "2032-04-01T00:00:00Z", DateTimeOffset.UtcNow, new([], [], [])));
            await store.WriteAsync(lease, "api.times", new SavedTimes(Revision, DateTimeOffset.UtcNow, new(1, [])));
            var handler = new Handler(); using var http = new HttpClient(handler);
            var updater = new SharedDataUpdater(new(http, new("https://example.invalid/")), store);
            var result = await updater.UpdateAsync(_ => throw new InvalidOperationException("Current data must not authenticate."));
            Assert.Equal(3, result.Count); Assert.All(result, item => { Assert.False(item.Updated); Assert.Null(item.Failure); });
            Assert.Equal(3, handler.PublicRequests); Assert.Equal(0, handler.AuthenticatedRequests);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

}
