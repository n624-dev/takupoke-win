# 固定iOS参照・独立期待値

対応関係・宣言名・実行方法のSHA-256：
`561b0d05fbb90cea51be3187be210f341910daabbed2e59f8c203c54e08c52c7`

環境：Linux／Windows .NET（WinUI操作はWindows限定）

```bash
dotnet test tests/Takupoke.Core.Tests --configuration Release
dotnet test tests/Takupoke.Integration.Tests --configuration Release
```

## 変更時に確認するソース

- [scripts/check-ios-parity.py](../../scripts/check-ios-parity.py)
- [tests/Takupoke.Core.Tests/fixtures/ios-parity.json](../../tests/Takupoke.Core.Tests/fixtures/ios-parity.json)
- [tests/reference/ExportParity.swift](../../tests/reference/ExportParity.swift)
- [tests/reference/ios-source.json](../../tests/reference/ios-source.json)

## [tests/Takupoke.Core.Tests/IosParityTests.cs](../../tests/Takupoke.Core.Tests/IosParityTests.cs)

- `ScheduleMatchesOriginalSwiftWithReviewedClockCorrections`
- `XlsxNormalizationMatchesOriginalSwift`
- `TextSearchClassesAndColorsMatchOriginalSwift`

## [tests/Takupoke.Integration.Tests/IosPdfParityTests.cs](../../tests/Takupoke.Integration.Tests/IosPdfParityTests.cs)

- `IdenticalDrawingGeometryMatchesTheOriginalSwiftParser`

## [tests/Takupoke.Integration.Tests/SpecialPdfParityTests.cs](../../tests/Takupoke.Integration.Tests/SpecialPdfParityTests.cs)

- `SegmentedCellTopBordersDefineTheCompleteExamBody`
- `BlankRuledEdgeRowsCannotDisappearFromExamScope`
- `DeclaredExamPageCountMustMatchTheSelectedDocument`
- `ExamUsesActualHeadersAndKeepsPageBoundScopeAfterSerialization`
- `ExamRejectsDuplicateScopeMissingClassAndUnassignedDateBody`
- `FiveExamPagesAndReorderedSixPagesKeepAllLessons`
- `SpecialPeriodHeadingsDoNotTruncateNumericTokens`
- `SpecialConsecutiveHeadingsDoNotAdoptNumericSuffix`
- `MissingContinuousHeadingKeepsVerifiedSinglePeriodFallback`
- `SpecialWholeNumericTokensRetainAdjacentAndWidthNormalizedHeadings`
- `ExamMatchesIosCoveredClassesDatesTimesAndMergedLessons`
- `ExamSeparatesSubjectTeacherAndRoom`
- `ReturnMatchesIosSplitCellsAndDifferentTimesOnSubsequentDays`
- `MissingSpecialTimeCannotBeReplacedByOrdinaryTime`
- `ReturnDedicatedClockFollowsOriginalColumnAfterSortedDatePersistence`
- `DedicatedReturnDateSurvivesEncryptedSqliteStoreRestart`
