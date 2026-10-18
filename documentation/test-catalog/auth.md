# OIDC・実認証・取消

対応関係・宣言名・実行方法のSHA-256：
`83081ddb448dfb044c3c9a14fc7235ca95f08f82d184ea1957da7dcd4ef04090`

環境：Linux／Windows .NET（WinUI操作はWindows限定）

```bash
dotnet test tests/Takupoke.Core.Tests --configuration Release
dotnet test tests/Takupoke.Integration.Tests --configuration Release
```

## 変更時に確認するソース

- [src/Takupoke.Infrastructure/Authentication/OidcClient.cs](../../src/Takupoke.Infrastructure/Authentication/OidcClient.cs)

## [tests/Takupoke.Integration.Tests/OidcClientTests.cs](../../tests/Takupoke.Integration.Tests/OidcClientTests.cs)

- `NetworkTokenCacheIsScopeBoundExpiresAndCannotSurviveCancellationDuringExchange`
- `TokenCacheDoesNotExtendExpiryWhileFetchingKeys`
- `AllResourceCombinationsSelectOneAuthenticationBeforeAnyUpdate`
- `AuthorizationUsesSeparateRandomStateNonceAndS256Verifier`
- `CallbackRequiresCorrectProtocolPathUniqueStateAndCode`
- `ValidEs256TokenIsVerifiedUsingJwksCoordinates`
- `RejectsEachInvalidSecurityClaimAndSignature`
- `ErrorCallbackMustMatchRedirectAndStateAndContainNoCode`
