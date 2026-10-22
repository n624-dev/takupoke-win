# リンク・検索・名称対応

対応関係・宣言名・実行方法のSHA-256：
`16b23e2f60a433d5c8320fa0c3810f5b93b4af4d902d26501b81bd161d5b2ed3`

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

## [tests/Takupoke.Core.Tests/LinkSearchTests.cs](../../tests/Takupoke.Core.Tests/LinkSearchTests.cs)

- `NormalizesKanaLatinAndPunctuation`
- `MatchesRomajiQueries`
- `AcceptsOnlySupportedLinkTargets`

## [tests/Takupoke.Core.Tests/MappingRulesTests.cs](../../tests/Takupoke.Core.Tests/MappingRulesTests.cs)

- `ClassSpecificRulesTakePriorityAndMetadataKeepsSeparators`
- `ContextualTeacherRequiresMatchingYearClassAndCanonicalSubject`
- `SeparatesOnlyConfirmedMetadata`
- `ConflictingExplicitMetadataPreservesOriginalSubject`
- `AmbiguousNormalizedAliasesAreNotGuessed`
- `ScopedInternationalRuleWinsOverGenericAliasRegardlessOfOrder`
- `ContextualTeacherAndConfirmedRoomSeparateInEitherOrder`
