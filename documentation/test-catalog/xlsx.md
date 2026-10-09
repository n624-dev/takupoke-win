# 時間割変更・曜日・行除外・原文保持

対応ソース・テストのSHA-256：
`02cbd5ac1287b3a9f105b9efbf483c264a616c8e266d03e8b02451d307e977ad`

環境：Linux／Windows .NET（WinUI操作はWindows限定）

```bash
dotnet test tests/Takupoke.Core.Tests --configuration Release
dotnet test tests/Takupoke.Integration.Tests --configuration Release
```

## 変更時に確認するソース

- [src/Takupoke.Core/ChangeNormalizer.cs](../../src/Takupoke.Core/ChangeNormalizer.cs)
- [src/Takupoke.Infrastructure/Parsing/XlsxChangeReader.Review.cs](../../src/Takupoke.Infrastructure/Parsing/XlsxChangeReader.Review.cs)
- [src/Takupoke.Infrastructure/Parsing/XlsxChangeReader.cs](../../src/Takupoke.Infrastructure/Parsing/XlsxChangeReader.cs)
- [src/Takupoke.Infrastructure/Storage/ChangeRowSkipConsent.cs](../../src/Takupoke.Infrastructure/Storage/ChangeRowSkipConsent.cs)
- [tests/Takupoke.Core.Tests/fixtures/normalization.json](../../tests/Takupoke.Core.Tests/fixtures/normalization.json)
- [tests/shared/FictionalChangeWorkbook.cs](../../tests/shared/FictionalChangeWorkbook.cs)

## [tests/Takupoke.Core.Tests/ChangeNormalizerTests.cs](../../tests/Takupoke.Core.Tests/ChangeNormalizerTests.cs)

- `MatchesSharedIosAndReferenceFixtures`（宣言行 14）
- `MissingYearsUseSchoolYear`（宣言行 41）
- `RejectsUnconfirmedDates`（宣言行 47）
- `AliasColumnsUseHeaderOrderEvenWhenFirstIsEmpty`（宣言行 52）
- `UnknownAllRejectsEntireResult`（宣言行 61）

## [tests/Takupoke.Integration.Tests/ChangeRowConsentTests.cs](../../tests/Takupoke.Integration.Tests/ChangeRowConsentTests.cs)

- `ExplicitApprovalPersistsWithAnalysisAndSurvivesRefreshReparseAndRestart`（宣言行 36）
- `ContentChangeReselectionAndYearChangesInvalidateWithoutResurrection`（宣言行 60）
- `OldParserConsentIsClearedEvenIfTheContentIsUnchanged`（宣言行 82）
- `StalePreviewMissingSelectionCancellationAndReplacementCannotSave`（宣言行 94）
- `ExcludingEveryChangeRefusesAndPreservesLastGood`（宣言行 112）
- `StorageFailureRollsBackConsentAnalysisAndAttemptTogether`（宣言行 129）
- `PreviousPayloadsWithoutOptionalConsentRemainReadable`（宣言行 155）

## [tests/Takupoke.Integration.Tests/ChangeRowReaderTests.cs](../../tests/Takupoke.Integration.Tests/ChangeRowReaderTests.cs)

- `ExplicitSelectionPreservesOriginalFieldsAndExcludesWholeRows`（宣言行 13）
- `WeekdayOnlyFormulaIsInspectedWithoutEvaluationOrAutomaticSkipping`（宣言行 37）
- `ExcludedPopulatedRowsStillRequireValidDateYearAndClasses`（宣言行 57）
- `ExclusionCannotHideStructureOrMissingCacheOrAcceptAnEmptyDocument`（宣言行 69）
- `ExcludedClassCannotSupplyTheRemainingAllRow`（宣言行 94）

## [tests/Takupoke.Integration.Tests/XlsxChangeReaderTests.cs](../../tests/Takupoke.Integration.Tests/XlsxChangeReaderTests.cs)

- `ReadsRealOoxmlStructureAndKeepsSourceText`（宣言行 13）
- `ValidWeekdayFormulaUsesSavedValueWithoutEvaluation`（宣言行 22）
- `FormulaWarningsAreAvailableForPreviewButCannotBeAccepted`（宣言行 27）
- `RejectsFormulaInSubjectColumn`（宣言行 36）
- `RejectsUnsupportedOrAmbiguousStructures`（宣言行 48）
- `CancellationStopsReading`（宣言行 80）
- `ZipBoundsApplyBeforeReadingDocumentParts`（宣言行 86）
