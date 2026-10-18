# リンク・検索・名称対応

対応関係・宣言名・実行方法のSHA-256：
`75bc13909deda92681e253d23f0ad95469e74e7fe87358804e2419b7bdf163c0`

環境：Linux／Windows .NET（WinUI操作はWindows限定）

```bash
dotnet test tests/Takupoke.Core.Tests --configuration Release
dotnet test tests/Takupoke.Integration.Tests --configuration Release
```

## 変更時に確認するソース

- [src/Takupoke.Core/DisplayText.cs](../../src/Takupoke.Core/DisplayText.cs)
- [src/Takupoke.Core/LinkModels.cs](../../src/Takupoke.Core/LinkModels.cs)
- [src/Takupoke.Core/LinkSearch.cs](../../src/Takupoke.Core/LinkSearch.cs)
- [src/Takupoke.Core/MappingRules.cs](../../src/Takupoke.Core/MappingRules.cs)
- [src/Takupoke.Core/BrowserNavigationPolicy.cs](../../src/Takupoke.Core/BrowserNavigationPolicy.cs)

## [tests/Takupoke.Core.Tests/LinkSearchTests.cs](../../tests/Takupoke.Core.Tests/LinkSearchTests.cs)

- `SyllabusChoiceOnlyChangesTheKnownTargetAndUsesCalendarYear`
- `NormalizesKanaLatinAndPunctuation`
- `MatchesRomajiQueries`
- `AcceptsOnlySupportedLinkTargets`

## [tests/Takupoke.Core.Tests/MappingRulesTests.cs](../../tests/Takupoke.Core.Tests/MappingRulesTests.cs)

- `ClassSpecificRulesTakePriorityAndMetadataKeepsSeparators`
- `ContextualTeacherRequiresMatchingYearClassAndCanonicalSubject`
- `SeparatesOnlyConfirmedMetadata`
- `ConflictingExplicitMetadataPreservesOriginalSubject`
- `AmbiguousNormalizedAliasesAreNotGuessed`

## [tests/Takupoke.Core.Tests/BrowserNavigationPolicyTests.cs](../../tests/Takupoke.Core.Tests/BrowserNavigationPolicyTests.cs)

- `DownloadedLinksOnlyAcceptHttpsOrLocalHttp`
- `RemotePagesCannotRedirectIntoLocalNetworkAndLocalPagesStayOnTheirHost`
- `FailureMessagesDistinguishDnsConnectionsAndCertificates`
