using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Takupoke.Infrastructure.Api;

namespace Takupoke.Infrastructure.Authentication;

public sealed record AuthorizationAttempt(string Verifier, string State, string Nonce, Uri AuthorizationUri);

public sealed class OidcClient(HttpClient http, TimeProvider? timeProvider = null)
{
    public const string Issuer = "https://takuma-gakunin.n624.jp";
    public const string ClientId = "takupoke-win";
    public const string RedirectUri = "jp.n624.takupoke.win:/oauth/callback";
    public const string Scopes = "openid mapping.read links.read";
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    public static string Base64Url(ReadOnlySpan<byte> data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string RandomToken() => Base64Url(RandomNumberGenerator.GetBytes(32));
    public static AuthorizationAttempt CreateAttempt()
    {
        var verifier = RandomToken(); var state = RandomToken(); var nonce = RandomToken();
        var fields = new Dictionary<string, string>
        {
            ["response_type"] = "code", ["client_id"] = ClientId, ["redirect_uri"] = RedirectUri, ["scope"] = Scopes,
            ["state"] = state, ["nonce"] = nonce, ["code_challenge"] = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))), ["code_challenge_method"] = "S256"
        };
        var query = string.Join("&", fields.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
        return new(verifier, state, nonce, new(Issuer + "/oauth/authorize?" + query));
    }
    public static string ValidateCallback(Uri callback, AuthorizationAttempt attempt)
    {
        if (!callback.IsAbsoluteUri || callback.Scheme != "jp.n624.takupoke.win" || callback.Host.Length > 0 || callback.AbsolutePath != "/oauth/callback"
            || callback.Fragment.Length > 0 || callback.OriginalString.Length > 16000) throw new ApiException(ApiFailure.Authentication);
        var fields = callback.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(part =>
        {
            var pair = part.Split('=', 2);
            return (Name: Uri.UnescapeDataString(pair[0].Replace('+', ' ')), Value: pair.Length == 2 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : "");
        }).ToArray();
        var states = fields.Where(p => p.Name == "state").ToArray(); var codes = fields.Where(p => p.Name == "code").ToArray();
        if (states.Length != 1 || codes.Length != 1 || codes[0].Value.Length is < 1 or > 8192 || fields.Any(p => p.Name == "error")
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(states[0].Value), Encoding.UTF8.GetBytes(attempt.State))) throw new ApiException(ApiFailure.Authentication);
        return codes[0].Value;
    }
    public static bool IsAuthenticatedErrorCallback(Uri callback, AuthorizationAttempt attempt)
    {
        if (!callback.IsAbsoluteUri || callback.Scheme != "jp.n624.takupoke.win" || callback.Host.Length != 0
            || callback.AbsolutePath != "/oauth/callback" || callback.Fragment.Length != 0 || callback.OriginalString.Length > 16000) return false;
        try
        {
            var fields = callback.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(part =>
            { var pair = part.Split('=', 2); return (Name: Uri.UnescapeDataString(pair[0].Replace('+', ' ')), Value: pair.Length == 2 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : ""); }).ToArray();
            var states = fields.Where(p => p.Name == "state").ToArray(); var errors = fields.Where(p => p.Name == "error").ToArray();
            return states.Length == 1 && errors.Length == 1 && errors[0].Value.Length > 0 && !fields.Any(p => p.Name == "code")
                && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(states[0].Value), Encoding.UTF8.GetBytes(attempt.State));
        }
        catch { return false; }
    }
    public async Task<string> ExchangeAsync(Uri callback, AuthorizationAttempt attempt, CancellationToken token = default)
    {
        try
        {
            var code = ValidateCallback(callback, attempt);
            using var request = new HttpRequestMessage(HttpMethod.Post, Issuer + "/oauth/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code", ["code"] = code, ["client_id"] = ClientId,
                    ["redirect_uri"] = RedirectUri, ["code_verifier"] = attempt.Verifier
                })
            };
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode != HttpStatusCode.OK) throw new ApiException(ApiFailure.Authentication);
            var bytes = await ApiClient.ReadBoundedAsync(response, 64 * 1024, token);
            using var document = ApiPayloads.Json(bytes, 64 * 1024);
            var data = document.RootElement;
            var expires = data.GetProperty("expires_in").GetInt32();
            var scope = data.GetProperty("scope").GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var accessToken = data.GetProperty("access_token").GetString()!;
            if (data.GetProperty("token_type").GetString() != "Bearer" || expires is < 1 or > 600 || !scope.Contains("mapping.read") || !scope.Contains("links.read")
                || string.IsNullOrEmpty(accessToken) || accessToken.Any(char.IsControl)) throw new ApiException(ApiFailure.Authentication);
            using var jwksRequest = new HttpRequestMessage(HttpMethod.Get, Issuer + "/oauth/jwks"); jwksRequest.Headers.Accept.ParseAdd("application/json");
            using var jwksResponse = await http.SendAsync(jwksRequest, HttpCompletionOption.ResponseHeadersRead, token);
            if (jwksResponse.StatusCode != HttpStatusCode.OK) throw new ApiException(ApiFailure.Authentication);
            ValidateIdToken(data.GetProperty("id_token").GetString()!, await ApiClient.ReadBoundedAsync(jwksResponse, 64 * 1024, token), attempt.Nonce, _clock.GetUtcNow());
            return accessToken;
        }
        catch (OperationCanceledException) { throw; }
        catch { throw new ApiException(ApiFailure.Authentication); }
    }
    public static void ValidateIdToken(string jwt, byte[] jwksBytes, string expectedNonce, DateTimeOffset now)
    {
        try
        {
            if (jwt.Length > 32000) throw new ApiException(ApiFailure.Authentication);
            var parts = jwt.Split('.');
            if (parts.Length != 3) throw new ApiException(ApiFailure.Authentication);
            using var headerDocument = ApiPayloads.Json(DecodeUrl(parts[0]), 16 * 1024);
            using var payloadDocument = ApiPayloads.Json(DecodeUrl(parts[1]), 32 * 1024);
            using var keysDocument = ApiPayloads.Json(jwksBytes, 64 * 1024);
            var header = headerDocument.RootElement; var claims = payloadDocument.RootElement;
            var kid = header.GetProperty("kid").GetString()!;
            var exp = claims.GetProperty("exp").GetDouble(); var iat = claims.GetProperty("iat").GetDouble();
            var amr = claims.GetProperty("amr").EnumerateArray().Select(e => e.GetString()).ToArray();
            if (header.GetProperty("alg").GetString() != "ES256" || header.GetProperty("typ").GetString() != "JWT" || string.IsNullOrEmpty(kid)
                || header.TryGetProperty("crit", out _) || claims.GetProperty("iss").GetString() != Issuer || claims.GetProperty("aud").GetString() != ClientId
                || claims.GetProperty("nonce").GetString() != expectedNonce || claims.GetProperty("acr").GetString() != "urn:takunin:assurance:strict"
                || amr.Length != 1 || amr[0] != "microsoft" || string.IsNullOrEmpty(claims.GetProperty("sub").GetString())
                || !double.IsFinite(exp) || !double.IsFinite(iat) || exp <= now.ToUnixTimeSeconds() || iat > now.ToUnixTimeSeconds() + 30 || exp <= iat || exp - iat > 600)
                throw new ApiException(ApiFailure.Authentication);
            var matches = keysDocument.RootElement.GetProperty("keys").EnumerateArray().Where(key =>
                key.GetProperty("kid").GetString() == kid && key.GetProperty("kty").GetString() == "EC" && key.GetProperty("crv").GetString() == "P-256"
                && key.GetProperty("alg").GetString() == "ES256" && key.GetProperty("use").GetString() == "sig").ToArray();
            if (matches.Length != 1) throw new ApiException(ApiFailure.Authentication);
            var x = DecodeUrl(matches[0].GetProperty("x").GetString()!); var y = DecodeUrl(matches[0].GetProperty("y").GetString()!);
            var signature = DecodeUrl(parts[2]);
            if (x.Length != 32 || y.Length != 32 || signature.Length != 64) throw new ApiException(ApiFailure.Authentication);
            using var key = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = x, Y = y } });
            if (!key.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                throw new ApiException(ApiFailure.Authentication);
        }
        catch { throw new ApiException(ApiFailure.Authentication); }
    }
    private static byte[] DecodeUrl(string value)
    {
        if (!Regex.IsMatch(value, "^[A-Za-z0-9_-]+$")) throw new ApiException(ApiFailure.Authentication);
        return Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));
    }
}
