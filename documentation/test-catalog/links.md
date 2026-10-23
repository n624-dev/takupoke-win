# リンク・検索・名称対応

対応関係・宣言名・実行方法のSHA-256：
`4a5f19bd85d59bb84203a0b570b1dd91a751dd6e7b2149f099994c57bd28574c`

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

- `LateSearchTermsRetainMatchesAndMatchKindPriority`
- `LongVowelVariantsKeepTheirVowel`
- `LongUVowelNeverBecomesO`
- `NormalizesKanaLatinAndPunctuation`
- `MatchesRomajiQueries`
- `AcceptsOnlySupportedLinkTargets`

## [tests/Takupoke.Core.Tests/MappingRulesTests.cs](../../tests/Takupoke.Core.Tests/MappingRulesTests.cs)

- `ExactSubjectSpellingTakesPriorityWithinThePreferredClass`
- `AnExplicitOrdinarySubjectIsNotHiddenByOmittedInternationalPrefix`
- `ConditionalTeacherUsesExactThenUniqueWidthMatchInItsOwnContext`
- `OmittedInternationalShortSubjectStillResolvesItsConditionalTeacher`
- `ClassSpecificRulesTakePriorityAndMetadataKeepsSeparators`
- `ContextualTeacherRequiresMatchingYearClassAndCanonicalSubject`
- `SeparatesOnlyConfirmedMetadata`
- `ConflictingExplicitMetadataPreservesOriginalSubject`
- `AmbiguousNormalizedAliasesAreNotGuessed`
- `ScopedInternationalRuleWinsOverGenericAliasRegardlessOfOrder`
- `ContextualTeacherAndConfirmedRoomSeparateInEitherOrder`
