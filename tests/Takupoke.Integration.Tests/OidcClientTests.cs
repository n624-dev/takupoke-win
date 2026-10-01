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
    private static (string Jwt, byte[] Jwks) Token(string? mutation = null)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = key.ExportParameters(false);
        var header = new { alg = mutation == "algorithm" ? "none" : "ES256", typ = "JWT", kid = "fake-key" };
        var claims = new JsonObject
        {
            ["iss"] = mutation == "issuer" ? "https://example.invalid/" : OidcClient.Issuer,
            ["aud"] = mutation == "audience" ? "takupoke-ios" : OidcClient.ClientId,
            ["nonce"] = mutation == "nonce" ? "wrong" : "fake-nonce", ["sub"] = "fake-subject",
            ["acr"] = mutation == "assurance" ? "fake-assurance" : "urn:takunin:assurance:strict",
            ["amr"] = new JsonArray(mutation == "method" ? "fake-method" : "microsoft"),
            ["iat"] = Now.ToUnixTimeSeconds() + (mutation == "issuedFuture" ? 31 : 0),
            ["exp"] = Now.ToUnixTimeSeconds() + (mutation == "expired" ? 0 : mutation == "tooLong" ? 601 : 300)
        };
        var signing = OidcClient.Base64Url(DataCodec.Encode(header)) + "." + OidcClient.Base64Url(Encoding.UTF8.GetBytes(claims.ToJsonString()));
        var signature = key.SignData(Encoding.ASCII.GetBytes(signing), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        if (mutation == "signature") signature[0] ^= 1;
        var jwk = new { kid = "fake-key", kty = "EC", crv = "P-256", alg = "ES256", use = "sig", x = OidcClient.Base64Url(publicKey.Q.X!), y = OidcClient.Base64Url(publicKey.Q.Y!) };
        return (signing + "." + OidcClient.Base64Url(signature), DataCodec.Encode(new { keys = mutation == "duplicateKey" ? new[] { jwk, jwk } : [jwk] }));
    }
}
