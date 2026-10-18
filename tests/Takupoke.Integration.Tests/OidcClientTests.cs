using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Takupoke.Infrastructure.Api;
using Takupoke.Infrastructure.Authentication;
using Takupoke.Infrastructure.Storage;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class OidcClientTests
{
    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now = OidcClientTests.Now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class TokenTransport(string jwt, byte[] jwks, string scope) : HttpMessageHandler
    {
        public Action? BeforeKeys;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            Assert.Equal(new Uri(OidcClient.Issuer).Host, request.RequestUri!.Host);
            byte[] bytes;
            if (request.RequestUri.AbsolutePath == "/oauth/token")
                bytes = DataCodec.Encode(new { access_token = "synthetic-access", token_type = "Bearer", expires_in = 300, scope, id_token = jwt });
            else { Assert.Equal("/oauth/jwks", request.RequestUri.AbsolutePath); BeforeKeys?.Invoke(); bytes = jwks; }
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }
    [Fact]
    public async Task NetworkTokenCacheIsScopeBoundExpiresAndCannotSurviveCancellationDuringExchange()
    {
        var clock = new TestClock();
        var attempt = OidcClient.CreateAttempt([DataSet.Links]);
        var signed = Token("network", attempt.Nonce);
        var transport = new TokenTransport(signed.Jwt, signed.Jwks, attempt.RequestedScopes);
        using var http = new HttpClient(transport);
        var client = new OidcClient(http, clock);
        var callback = new Uri(OidcClient.RedirectUri + "?code=synthetic-code&state=" + attempt.State);
        Assert.Equal("synthetic-access", await client.ExchangeAsync(callback, attempt));
        Assert.True(client.TryCached([DataSet.Links], out var cached));
        Assert.Equal("synthetic-access", cached);
        Assert.False(client.TryCached([DataSet.Mapping], out _));
        Assert.False(client.TryCached([DataSet.Links, DataSet.Times], out _));
        clock.Now = Now.AddSeconds(271);
        Assert.False(client.TryCached([DataSet.Links], out _));
        clock.Now = Now;
        client.ClearCache();
        Assert.False(client.TryCached([DataSet.Links], out _));
        transport.BeforeKeys = client.ClearCache;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ExchangeAsync(callback, attempt));
        Assert.False(client.TryCached([DataSet.Links], out _));
    }
    [Fact]
    public async Task TokenCacheDoesNotExtendExpiryWhileFetchingKeys()
    {
        var clock = new TestClock();
        var attempt = OidcClient.CreateAttempt([DataSet.Links]);
        var signed = Token("network", attempt.Nonce);
        var transport = new TokenTransport(signed.Jwt, signed.Jwks, attempt.RequestedScopes) {
            BeforeKeys = () => clock.Now = Now.AddSeconds(60)
        };
        using var http = new HttpClient(transport);
        var client = new OidcClient(http, clock);
        await client.ExchangeAsync(new(OidcClient.RedirectUri + "?code=synthetic-code&state=" + attempt.State), attempt);
        Assert.True(client.TryCached([DataSet.Links], out _));
        clock.Now = Now.AddSeconds(310);
        Assert.False(client.TryCached([DataSet.Links], out _));
    }
    [Fact]
    public void AllResourceCombinationsSelectOneAuthenticationBeforeAnyUpdate()
    {
        for (var mask = 1; mask < 8; mask++)
        {
            var selected = Enum.GetValues<DataSet>().Where((_, i) => (mask & (1 << i)) != 0).ToArray();
            var attempt = OidcClient.CreateAttempt(selected);
            Assert.Equal(selected.SequenceEqual([DataSet.Links]), attempt.AllowSchoolNetwork);
            Assert.Equal(selected.Contains(DataSet.Links), attempt.RequestedScopes.Contains("links.read"));
            Assert.Equal(selected.Any(k => k != DataSet.Links), attempt.RequestedScopes.Contains("mapping.read"));
            Assert.Equal(attempt.AllowSchoolNetwork, attempt.AuthorizationUri.Query.Contains("acr_values="));
        }
        Assert.Throws<ArgumentException>(() => OidcClient.CreateAttempt([]));
        var network = Token("network");
        Assert.Throws<ApiException>(() => OidcClient.ValidateIdToken(network.Jwt, network.Jwks, "fake-nonce", Now));
        OidcClient.ValidateIdToken(network.Jwt, network.Jwks, "fake-nonce", Now, allowSchoolNetwork: true);
        var strict = Token();
        OidcClient.ValidateIdToken(strict.Jwt, strict.Jwks, "fake-nonce", Now, allowSchoolNetwork: true);
    }
    private static readonly DateTimeOffset Now = new(2032, 4, 5, 0, 0, 0, TimeSpan.Zero);
    [Fact]
    public void AuthorizationUsesSeparateRandomStateNonceAndS256Verifier()
    {
        var first = OidcClient.CreateAttempt(); var second = OidcClient.CreateAttempt();
        Assert.NotEqual(first.State, first.Nonce); Assert.NotEqual(first.State, second.State);
        Assert.Equal(43, first.Verifier.Length);
        Assert.Contains("code_challenge_method=S256", first.AuthorizationUri.Query);
        Assert.Contains("code_challenge=" + OidcClient.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(first.Verifier))), first.AuthorizationUri.Query);
    }
    [Fact]
    public void CallbackRequiresCorrectProtocolPathUniqueStateAndCode()
    {
        var attempt = OidcClient.CreateAttempt();
        Assert.Equal("fake-code", OidcClient.ValidateCallback(new(OidcClient.RedirectUri + "?code=fake-code&state=" + attempt.State), attempt));
        foreach (var callback in new[]
        {
            OidcClient.RedirectUri + "?code=fake-code&state=wrong",
            OidcClient.RedirectUri + "?code=fake-code&state=" + attempt.State + "&state=" + attempt.State,
            OidcClient.RedirectUri + "?code=fake-code&state=" + attempt.State + "&error=fake",
            "https://example.invalid/oauth/callback?code=fake-code&state=" + attempt.State,
            "jp.n624.takupoke.win://example.invalid/oauth/callback?code=fake-code&state=" + attempt.State
        }) Assert.Throws<ApiException>(() => OidcClient.ValidateCallback(new(callback), attempt));
    }
    [Fact]
    public void ValidEs256TokenIsVerifiedUsingJwksCoordinates()
    {
        var token = Token();
        OidcClient.ValidateIdToken(token.Jwt, token.Jwks, "fake-nonce", Now);
    }
    [Theory]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("nonce")]
    [InlineData("expired")]
    [InlineData("issuedFuture")]
    [InlineData("tooLong")]
    [InlineData("assurance")]
    [InlineData("method")]
    [InlineData("algorithm")]
    [InlineData("signature")]
    [InlineData("duplicateKey")]
    public void RejectsEachInvalidSecurityClaimAndSignature(string mutation)
    {
        var token = Token(mutation);
        Assert.Equal(ApiFailure.Authentication, Assert.Throws<ApiException>(() => OidcClient.ValidateIdToken(token.Jwt, token.Jwks, "fake-nonce", Now)).Failure);
    }
    private static (string Jwt, byte[] Jwks) Token(string? mutation = null, string? nonce = null)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = key.ExportParameters(false);
        var header = new { alg = mutation == "algorithm" ? "none" : "ES256", typ = "JWT", kid = "fake-key" };
        var claims = new JsonObject
        {
            ["iss"] = mutation == "issuer" ? "https://example.invalid/" : OidcClient.Issuer,
            ["aud"] = mutation == "audience" ? "takupoke-ios" : OidcClient.ClientId,
            ["nonce"] = mutation == "nonce" ? "wrong" : nonce ?? "fake-nonce", ["sub"] = "fake-subject",
            ["acr"] = mutation == "assurance" ? "fake-assurance" : mutation == "network" ? "urn:takunin:assurance:standard" : "urn:takunin:assurance:strict",
            ["amr"] = new JsonArray(mutation == "method" ? "fake-method" : mutation == "network" ? "school_network" : "microsoft"),
            ["iat"] = Now.ToUnixTimeSeconds() + (mutation == "issuedFuture" ? 31 : 0),
            ["exp"] = Now.ToUnixTimeSeconds() + (mutation == "expired" ? 0 : mutation == "tooLong" ? 601 : 300)
        };
        var signing = OidcClient.Base64Url(DataCodec.Encode(header)) + "." + OidcClient.Base64Url(Encoding.UTF8.GetBytes(claims.ToJsonString()));
        var signature = key.SignData(Encoding.ASCII.GetBytes(signing), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        if (mutation == "signature") signature[0] ^= 1;
        var jwk = new { kid = "fake-key", kty = "EC", crv = "P-256", alg = "ES256", use = "sig", x = OidcClient.Base64Url(publicKey.Q.X!), y = OidcClient.Base64Url(publicKey.Q.Y!) };
        return (signing + "." + OidcClient.Base64Url(signature), DataCodec.Encode(new { keys = mutation == "duplicateKey" ? new[] { jwk, jwk } : [jwk] }));
    }
    [Fact]
    public void ErrorCallbackMustMatchRedirectAndStateAndContainNoCode()
    {
        var attempt = OidcClient.CreateAttempt();
        Assert.True(OidcClient.IsAuthenticatedErrorCallback(new(OidcClient.RedirectUri + "?error=access_denied&state=" + attempt.State), attempt));
        Assert.False(OidcClient.IsAuthenticatedErrorCallback(new(OidcClient.RedirectUri + "?error=access_denied&state=forged"), attempt));
        Assert.False(OidcClient.IsAuthenticatedErrorCallback(new(OidcClient.RedirectUri + "?error=access_denied&state=" + attempt.State + "&code=fake"), attempt));
        Assert.False(OidcClient.IsAuthenticatedErrorCallback(new("https://example.invalid/oauth/callback?error=access_denied&state=" + attempt.State), attempt));
    }

}
