# OIDC・実認証・取消

対応関係・宣言名・実行方法のSHA-256：
`53d3b4df0e182036bbf8a87f6a9e68a1e8a6d334844efc2d909d4908051dfc78`

環境：Linux／Windows .NET（WinUI操作はWindows限定）

```bash
dotnet test tests/Takupoke.Core.Tests --configuration Release
dotnet test tests/Takupoke.Integration.Tests --configuration Release
```

## 変更時に確認するソース

- [src/Takupoke.Infrastructure/Authentication/OidcClient.cs](../../src/Takupoke.Infrastructure/Authentication/OidcClient.cs)

## [tests/Takupoke.Integration.Tests/OidcClientTests.cs](../../tests/Takupoke.Integration.Tests/OidcClientTests.cs)

- `AuthorizationUsesSeparateRandomStateNonceAndS256Verifier`
- `CallbackRequiresCorrectProtocolPathUniqueStateAndCode`
- `ValidEs256TokenIsVerifiedUsingJwksCoordinates`
- `RejectsEachInvalidSecurityClaimAndSignature`
- `ErrorCallbackMustMatchRedirectAndStateAndContainNoCode`
