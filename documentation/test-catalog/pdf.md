# Strict PDF・文字・罫線・試験・返却

対応ソース・テストのSHA-256：
`be3fe12277b5ea4b1d4883bd66cb434add213f171c9a139894d96dd4d9834332`

環境：Linux／Windows .NET（WinUI操作はWindows限定）

```bash
dotnet test tests/Takupoke.Core.Tests --configuration Release
dotnet test tests/Takupoke.Integration.Tests --configuration Release
```

## 変更時に確認するソース

- [src/Takupoke.Infrastructure/Parsing/BoundedXml.cs](../../src/Takupoke.Infrastructure/Parsing/BoundedXml.cs)
- [src/Takupoke.Infrastructure/Parsing/BoundedZip.cs](../../src/Takupoke.Infrastructure/Parsing/BoundedZip.cs)
- [src/Takupoke.Infrastructure/Parsing/PdfLayout.cs](../../src/Takupoke.Infrastructure/Parsing/PdfLayout.cs)
- [src/Takupoke.Infrastructure/Parsing/PdfPathEngine.cs](../../src/Takupoke.Infrastructure/Parsing/PdfPathEngine.cs)
- [src/Takupoke.Infrastructure/Parsing/PdfPigLayoutReader.cs](../../src/Takupoke.Infrastructure/Parsing/PdfPigLayoutReader.cs)
- [src/Takupoke.Infrastructure/Parsing/PdfScheduleParser.Special.cs](../../src/Takupoke.Infrastructure/Parsing/PdfScheduleParser.Special.cs)
- [src/Takupoke.Infrastructure/Parsing/PdfScheduleParser.cs](../../src/Takupoke.Infrastructure/Parsing/PdfScheduleParser.cs)
- [src/Takupoke.Infrastructure/Parsing/PdfTextEngine.cs](../../src/Takupoke.Infrastructure/Parsing/PdfTextEngine.cs)
- [src/Takupoke.Infrastructure/Parsing/PdfUnicodeMap.cs](../../src/Takupoke.Infrastructure/Parsing/PdfUnicodeMap.cs)

## [tests/Takupoke.Integration.Tests/PdfParsingTests.cs](../../tests/Takupoke.Integration.Tests/PdfParsingTests.cs)

- `StrictDrawingGeometryUsesMetricsSpacingAndVerifiedText`（宣言行 12）
- `RejectsMissingUnicodeInvisibleTextUnfinishedTextAndMismatch`（宣言行 20）
- `CompositeFontsUseDefaultWidthWhenIndividualWidthsAreMissing`（宣言行 30）
- `CmapDecodesBfcharAndSequentialRanges`（宣言行 38）
- `CmapRejectsUnsupportedOrAmbiguousMappings`（宣言行 44）
- `ContentOrderSurvivesOverlappingCharacterBounds`（宣言行 53）
- `DisjointFragmentsMergeButContainedOrBackwardsFragmentsAreRejected`（宣言行 60）
- `TimetablePreservesParallelLessonsAndEmptyRoom`（宣言行 69）
- `TimetableRejectsAmbiguousParallelPairing`（宣言行 77）
- `CollapsesOnlyRepeatedVoicingMarksFollowingHalfwidthKana`（宣言行 82）
- `PaintedThinRectanglesProvideRulesWithoutBroadHighlightEdges`（宣言行 87）
- `ReaderVerifiesACompleteSyntheticPdfWithExplicitUnicodeAndFontMetrics`（宣言行 95）
- `ReaderRejectsMissingMappingAndFormObjects`（宣言行 102）
