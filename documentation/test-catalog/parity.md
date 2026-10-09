# 固定iOS参照・独立期待値

対応ソース・テストのSHA-256：
`c678767a6d13af61c3655375d8db2b0bb34a6148dc73d911d32672281903beba`

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

- `ScheduleMatchesOriginalSwift`（宣言行 16）
- `XlsxNormalizationMatchesOriginalSwift`（宣言行 45）
- `TextSearchClassesAndColorsMatchOriginalSwift`（宣言行 58）

## [tests/Takupoke.Integration.Tests/IosPdfParityTests.cs](../../tests/Takupoke.Integration.Tests/IosPdfParityTests.cs)

- `IdenticalDrawingGeometryMatchesTheOriginalSwiftParser`（宣言行 10）

## [tests/Takupoke.Integration.Tests/SpecialPdfParityTests.cs](../../tests/Takupoke.Integration.Tests/SpecialPdfParityTests.cs)

- `ExamMatchesIosCoveredClassesDatesTimesAndMergedLessons`（宣言行 10）
- `ExamSeparatesSubjectTeacherAndRoom`（宣言行 20）
- `ReturnMatchesIosSplitCellsAndDifferentTimesOnSubsequentDays`（宣言行 27）
- `MissingSpecialTimeCannotBeReplacedByOrdinaryTime`（宣言行 44）
