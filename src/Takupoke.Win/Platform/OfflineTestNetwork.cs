using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Takupoke.Core;
using Takupoke.Infrastructure.Api;
using Takupoke.Infrastructure.Authentication;
using Takupoke.Infrastructure.Storage;

namespace Takupoke.Win.Platform;

// CI-only in-memory transport. It never delegates any request to a network handler.
// The same built/installed executable exercises callback routing and OIDC validation.
internal sealed class OfflineTestNetwork : HttpMessageHandler
{
    private readonly string _root;
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private string? _nonce;
    private int _tokenRequests;
    private int _privateRequests;
    internal OfflineTestNetwork(string root)
    {
        if (Environment.GetEnvironmentVariable("TAKUPOKE_OFFLINE_TEST_MODE") != "1") throw new InvalidOperationException("An isolated offline test is required.");
        _root = root;
    }
    private string Mode => File.Exists(Path.Combine(_root, "offline-auth-mode.txt")) ? ReadProbe(Path.Combine(_root, "offline-auth-mode.txt")).Trim() : "disabled";
    private string Revision => File.Exists(Path.Combine(_root, "offline-auth-revision.txt")) ? ReadProbe(Path.Combine(_root, "offline-auth-revision.txt")).Trim() : new string('B', 43);
    internal void OpenBrowser(Uri authorization)
    {
        if (Mode == "disabled") throw new InvalidOperationException("CI does not launch a real browser or authenticate.");
        var query = authorization.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2))
            .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]));
        _nonce = query["nonce"];
        // Only this synthetic state's file is used to send a fake OS callback.
        WriteProbe("offline-auth-state.txt", query["state"]);
    }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (Mode == "disabled") throw new InvalidOperationException("CI does not perform network requests.");
        var uri = request.RequestUri!;
        if (uri.Host != "takupoke-api.n624.jp" && uri.Host != "takuma-gakunin.n624.jp") throw new InvalidOperationException("Unexpected offline request.");
        if (uri.AbsolutePath.EndsWith("-revision", StringComparison.Ordinal))
        {
            var response = Response([]); response.Headers.ETag = new('"' + Revision + '"'); return response;
        }
        if (uri.AbsolutePath == "/oauth/token")
        {
            WriteProbe("offline-token-requests.txt", (++_tokenRequests).ToString());
            while (Mode == "hold") await Task.Delay(50, token);
            if (Mode == "fail") return new(HttpStatusCode.BadRequest) { Content = new StringContent("{}") };
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var header = new { alg = "ES256", typ = "JWT", kid = "fake-offline-key" };
            var claims = new { iss = OidcClient.Issuer, aud = OidcClient.ClientId, nonce = _nonce, sub = "fake-offline-subject",
                acr = "urn:takunin:assurance:strict", amr = new[] { "microsoft" }, iat = now, exp = now + 300 };
            var signing = OidcClient.Base64Url(DataCodec.Encode(header)) + "." + OidcClient.Base64Url(DataCodec.Encode(claims));
            var jwt = signing + "." + OidcClient.Base64Url(_key.SignData(Encoding.ASCII.GetBytes(signing), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
            return Response(DataCodec.Encode(new { token_type = "Bearer", expires_in = 300, scope = OidcClient.Scopes, access_token = "fake-offline-access", id_token = jwt }), "application/json");
        }
        if (uri.AbsolutePath == "/oauth/jwks")
        {
            var key = _key.ExportParameters(false);
            return Response(DataCodec.Encode(new { keys = new[] { new { kid = "fake-offline-key", kty = "EC", crv = "P-256", alg = "ES256", use = "sig",
                x = OidcClient.Base64Url(key.Q.X!), y = OidcClient.Base64Url(key.Q.Y!) } } }), "application/json");
        }
        if (request.Headers.Authorization?.Parameter != "fake-offline-access") throw new InvalidOperationException("A validated fake token is required.");
        WriteProbe("offline-private-requests.txt", (++_privateRequests).ToString());
        if (uri.AbsolutePath == "/links")
        {
            var link = new LinkItem("fake-study", "fake-category", "架空学習リンク", "https://example.invalid/", "blue", true, 1, true, 1, [], "架空学習リンク|かくうがくしゅうりんく|kakuugakushuurinku");
            var payload = new LinksPayload("v1", "sha256-" + new string('a', 64), [new("fake-category", "架空カテゴリ", 1,
                [link, link with { Id = "fake-second", Label = "架空の別リンク", SortOrder = 0, SearchTerms = "別リンク" }])]);
            var response = Response(DataCodec.Encode(payload), "application/json"); response.Headers.Add("X-Links-Revision", Revision); return response;
        }
        if (uri.AbsolutePath == "/timetable-times")
        { var response = Response(DataCodec.Encode(new { schemaVersion = 1, days = Array.Empty<object>() }), "application/json"); response.Headers.Add("X-Timetable-Times-Revision", Revision); return response; }
        if (uri.AbsolutePath == "/mappings/current")
        {
            var bytes = DataCodec.Encode(new { subjects = Array.Empty<object>(), teachers = Array.Empty<object>(), rooms = Array.Empty<object>() });
            var manifest = DataCodec.Encode(new { schemaVersion = 1, version = "fake-v1", publishedAt = "2032-04-01T00:00:00Z", mappings = new { sha256 = NotificationDiff.Digest(bytes), bytes = bytes.Length } });
            using var output = new MemoryStream();
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            { using (var entry = zip.CreateEntry("mappings.json").Open()) entry.Write(bytes); using (var entry = zip.CreateEntry("manifest.json").Open()) entry.Write(manifest); }
            var response = Response(output.ToArray(), "application/zip"); response.Headers.Add("X-Mapping-Version", "fake-v1"); response.Headers.Add("X-Mapping-Revision", Revision); return response;
        }
        throw new InvalidOperationException("Unexpected offline request.");
    }
    private static HttpResponseMessage Response(byte[] bytes, string? type = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        response.Headers.ETag = new("\"fake-offline-etag\"");
        if (type is not null) response.Content.Headers.ContentType = new MediaTypeHeaderValue(type);
        return response;
    }
    private static string ReadProbe(string path)
    { using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); using var text = new StreamReader(file); return text.ReadToEnd(); }
    private void WriteProbe(string name, string value)
    { var path = Path.Combine(_root, name); File.WriteAllText(path + ".tmp", value); File.Move(path + ".tmp", path, true); }
    protected override void Dispose(bool disposing) { if (disposing) _key.Dispose(); base.Dispose(disposing); }
}
