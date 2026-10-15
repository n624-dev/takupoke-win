# リンク・検索・名称対応

対応関係・宣言名・実行方法のSHA-256：
`ef43c9d9f6c39ac9e4a22cd47f301fead3991553f1547a506a85af37ea9febec`

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
