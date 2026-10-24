# 固定iOS参照・独立期待値

対応関係・宣言名・実行方法のSHA-256：
`2a1cf88d8a35ea0a60a5532d9dcc178cf15be9c67452b3f6e9421cf1ae986342`

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

- `SpecialPeriodHeadingsDoNotTruncateNumericTokens`
- `SpecialConsecutiveHeadingsDoNotAdoptNumericSuffix`
- `SpecialWholeNumericTokensRetainAdjacentAndWidthNormalizedHeadings`
- `ExamMatchesIosCoveredClassesDatesTimesAndMergedLessons`
- `ExamSeparatesSubjectTeacherAndRoom`
- `ReturnMatchesIosSplitCellsAndDifferentTimesOnSubsequentDays`
- `MissingSpecialTimeCannotBeReplacedByOrdinaryTime`
- `ReturnDedicatedClockFollowsOriginalColumnAfterSortedDatePersistence`
- `DedicatedReturnDateSurvivesEncryptedSqliteStoreRestart`
