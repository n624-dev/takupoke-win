# 時間割変更・曜日・行除外・原文保持

対応関係・宣言名・実行方法のSHA-256：
`6a3861b89e64e5a460b9a4048a840e36794d2481bdf2d0c22640a36d050d506a`

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

- `LeadingZeroGradesFromWorkbookRemainSelectableAndExpandAll`
- `TypedIsoDateCellsKeepTheirWrittenCalendarDay`
- `InvalidTypedDatesRemainRejected`
- `Utf8BomAtTheStartOfXmlDoesNotChangeLessons`
- `CachedStringWeekdaysDecodeSpreadsheetEscapesOnce`
- `PlainStringCellsDecodeEscapesWithoutRecursingOrRepairingSurrogates`
- `ReadsRealOoxmlStructureAndKeepsSourceText`
- `ValidWeekdayFormulaUsesSavedValueWithoutEvaluation`
- `FormulaWarningsAreAvailableForPreviewButCannotBeAccepted`
- `RejectsFormulaInSubjectColumn`
- `RejectsUnsupportedOrAmbiguousStructures`
- `CancellationStopsReading`
- `ZipBoundsApplyBeforeReadingDocumentParts`
- `WorkbookKindIsIndependentOfRemarksAndEscapesDecodeOnlyOnce`
- `WorkbookMakeupWithRemarksKeepsPriorityOverFollowingCancellation`
