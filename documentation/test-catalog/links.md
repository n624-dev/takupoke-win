# リンク・検索・名称対応

対応ソース・テストのSHA-256：
`dc2ca942a0e82a6788554ea4c5144371c3f13620dbef39eecfe29795efea6577`

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

- `NormalizesKanaLatinAndPunctuation`（宣言行 8）
- `MatchesRomajiQueries`（宣言行 13）
- `AcceptsOnlySupportedLinkTargets`（宣言行 18）

## [tests/Takupoke.Core.Tests/MappingRulesTests.cs](../../tests/Takupoke.Core.Tests/MappingRulesTests.cs)

- `ClassSpecificRulesTakePriorityAndMetadataKeepsSeparators`（宣言行 12）
- `ContextualTeacherRequiresMatchingYearClassAndCanonicalSubject`（宣言行 20）
- `SeparatesOnlyConfirmedMetadata`（宣言行 27）
- `ConflictingExplicitMetadataPreservesOriginalSubject`（宣言行 34）
- `AmbiguousNormalizedAliasesAreNotGuessed`（宣言行 41）
