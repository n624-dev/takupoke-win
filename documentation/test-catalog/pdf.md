# Strict PDF・文字・罫線・試験・返却

対応関係・宣言名・実行方法のSHA-256：
`7d6c4808acf736e2424a7aa20fe56769b690fd58e842628a0b45a99dfef41b0a`

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
- [src/Takupoke.Infrastructure/Parsing/PdfPaintVisibility.cs](../../src/Takupoke.Infrastructure/Parsing/PdfPaintVisibility.cs)
- [tests/shared/FictionalTimetablePdf.cs](../../tests/shared/FictionalTimetablePdf.cs)
- [tests/Takupoke.Integration.Tests/fixtures/fictional-noto.json](../../tests/Takupoke.Integration.Tests/fixtures/fictional-noto.json)

## [tests/Takupoke.Integration.Tests/PdfParsingTests.cs](../../tests/Takupoke.Integration.Tests/PdfParsingTests.cs)

- `StrictDrawingGeometryUsesMetricsSpacingAndVerifiedText`
- `RejectsMissingUnicodeInvisibleTextUnfinishedTextAndMismatch`
- `CompositeFontsUseDefaultWidthWhenIndividualWidthsAreMissing`
- `CmapDecodesBfcharAndSequentialRanges`
- `CmapRejectsUnsupportedOrAmbiguousMappings`
- `ContentOrderSurvivesOverlappingCharacterBounds`
- `DisjointFragmentsMergeButContainedOrBackwardsFragmentsAreRejected`
- `TimetablePreservesParallelLessonsAndEmptyRoom`
- `TimetableRejectsAmbiguousParallelPairing`
- `CollapsesOnlyRepeatedVoicingMarksFollowingHalfwidthKana`
- `PaintedThinRectanglesProvideRulesWithoutBroadHighlightEdges`
- `ReaderVerifiesACompleteSyntheticPdfWithExplicitUnicodeAndFontMetrics`
- `ReaderRejectsMissingMappingAndFormObjects`

## [tests/Takupoke.Integration.Tests/PdfVisibilityTests.cs](../../tests/Takupoke.Integration.Tests/PdfVisibilityTests.cs)

- `RealFontControlUsesItsActualCidMappingAndProducesOneFictionalLesson`
- `DeviceColourSpaceWhiteInkStillRejectsTheWholeInput`
- `DeviceColourSpacesTrackRestorationImplicitSelectionAndInitialBlack`
- `UnverifiedSpacesAndIncorrectComponentCountsAreRejected`
- `StandardDeviceColourSpaceOperatorsProduceTheSameVerifiedBlackLesson`
- `VisibleBlackDeviceColoursAndRestoredStatesKeepTheLesson`
- `WhiteTextOnTheWhitePageRejectsTheWholeInput`
- `OpaqueColouredBackgroundsAreComparedRatherThanDroppingWhiteLetters`
- `StrokeAndFillModesRequireVerifiedVisibleInk`
- `UnprovedTransparencyAndBlendModesRejectInsteadOfPretendingOpaque`
- `LaterPaintAndMixedInvisibleCellsCannotBecomePartialSuccess`
- `PartialBackgroundsAndOverlappingFillSubpathsRemainUnverified`
- `ColourValidationPreservesCancellationAndRejectsUnverifiedColourSpaces`
- `InvisibleReplacementKeepsThePreviousAnalysisAndBothOriginalsAcrossRestart`
