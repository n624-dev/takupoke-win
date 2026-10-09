# OIDC・実認証・取消

対応ソース・テストのSHA-256：
`f293d2f75fe69a9b901fd198bbd80c9978595e3939c210f22173e3da3e860496`

環境：Linux／Windows .NET（WinUI操作はWindows限定）

```bash
dotnet test tests/Takupoke.Core.Tests --configuration Release
dotnet test tests/Takupoke.Integration.Tests --configuration Release
```

## 変更時に確認するソース

- [src/Takupoke.Infrastructure/Authentication/OidcClient.cs](../../src/Takupoke.Infrastructure/Authentication/OidcClient.cs)

## [tests/Takupoke.Integration.Tests/OidcClientTests.cs](../../tests/Takupoke.Integration.Tests/OidcClientTests.cs)

- `AuthorizationUsesSeparateRandomStateNonceAndS256Verifier`（宣言行 14）
- `CallbackRequiresCorrectProtocolPathUniqueStateAndCode`（宣言行 23）
- `ValidEs256TokenIsVerifiedUsingJwksCoordinates`（宣言行 37）
- `RejectsEachInvalidSecurityClaimAndSignature`（宣言行 43）
- `ErrorCallbackMustMatchRedirectAndStateAndContainNoCode`（宣言行 81）
