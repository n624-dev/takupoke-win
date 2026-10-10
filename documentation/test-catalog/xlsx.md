# 時間割変更・曜日・行除外・原文保持

対応関係・宣言名・実行方法のSHA-256：
`d68467f36bfb5651eab04a3b9769317b43840619042de6400b46ed5e1b7537e5`

環境：Linux／Windows .NET（WinUI操作はWindows限定）

```bash
dotnet test tests/Takupoke.Core.Tests --configuration Release
dotnet test tests/Takupoke.Integration.Tests --configuration Release
```

## 変更時に確認するソース

- [src/Takupoke.Core/ChangeNormalizer.cs](../../src/Takupoke.Core/ChangeNormalizer.cs)
- [src/Takupoke.Infrastructure/Parsing/ChangeReviewGroup.cs](../../src/Takupoke.Infrastructure/Parsing/ChangeReviewGroup.cs)
- [src/Takupoke.Infrastructure/Parsing/XlsxChangeReader.Review.cs](../../src/Takupoke.Infrastructure/Parsing/XlsxChangeReader.Review.cs)
- [src/Takupoke.Infrastructure/Parsing/XlsxChangeReader.cs](../../src/Takupoke.Infrastructure/Parsing/XlsxChangeReader.cs)
- [src/Takupoke.Infrastructure/Storage/ChangeRowSkipConsent.cs](../../src/Takupoke.Infrastructure/Storage/ChangeRowSkipConsent.cs)
- [tests/Takupoke.Core.Tests/fixtures/normalization.json](../../tests/Takupoke.Core.Tests/fixtures/normalization.json)
- [tests/shared/FictionalChangeWorkbook.cs](../../tests/shared/FictionalChangeWorkbook.cs)

## [tests/Takupoke.Core.Tests/ChangeNormalizerTests.cs](../../tests/Takupoke.Core.Tests/ChangeNormalizerTests.cs)

- `MatchesSharedIosAndReferenceFixtures`
- `MissingYearsUseSchoolYear`
- `RejectsUnconfirmedDates`
- `AliasColumnsUseHeaderOrderEvenWhenFirstIsEmpty`
- `UnknownAllRejectsEntireResult`

## [tests/Takupoke.Integration.Tests/ChangeRowConsentTests.cs](../../tests/Takupoke.Integration.Tests/ChangeRowConsentTests.cs)

- `ExplicitApprovalPersistsWithAnalysisAndSurvivesRefreshReparseAndRestart`
- `ContentChangeReselectionAndYearChangesInvalidateWithoutResurrection`
- `OldParserConsentIsClearedEvenIfTheContentIsUnchanged`
- `StalePreviewMissingSelectionCancellationAndReplacementCannotSave`
- `ExcludingEveryChangeRefusesAndPreservesLastGood`
- `StorageFailureRollsBackConsentAnalysisAndAttemptTogether`
- `PreviousPayloadsWithoutOptionalConsentRemainReadable`

## [tests/Takupoke.Integration.Tests/ChangeRowReaderTests.cs](../../tests/Takupoke.Integration.Tests/ChangeRowReaderTests.cs)

- `IdenticalWeekdayTailGroupsPhysicalRowsButStillRequiresFullExplicitConsent`
- `GroupingPreservesGapsDifferentWeekdaysAndPopulatedRows`
- `ExplicitSelectionPreservesOriginalFieldsAndExcludesWholeRows`
- `WeekdayOnlyFormulaIsInspectedWithoutEvaluationOrAutomaticSkipping`
- `ExcludedPopulatedRowsStillRequireValidDateYearAndClasses`
- `ExclusionCannotHideStructureOrMissingCacheOrAcceptAnEmptyDocument`
- `ExcludedClassCannotSupplyTheRemainingAllRow`

## [tests/Takupoke.Integration.Tests/XlsxChangeReaderTests.cs](../../tests/Takupoke.Integration.Tests/XlsxChangeReaderTests.cs)

- `ReadsRealOoxmlStructureAndKeepsSourceText`
- `ValidWeekdayFormulaUsesSavedValueWithoutEvaluation`
- `FormulaWarningsAreAvailableForPreviewButCannotBeAccepted`
- `RejectsFormulaInSubjectColumn`
- `RejectsUnsupportedOrAmbiguousStructures`
- `CancellationStopsReading`
- `ZipBoundsApplyBeforeReadingDocumentParts`
